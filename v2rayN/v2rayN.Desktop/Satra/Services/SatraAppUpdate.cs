using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// App self-update from Satra Hub (<c>/satra/dl/latest.json</c>): pick the right artifact for this OS,
/// download with SHA-256 check, install, restart. Linux .deb installs go through pkexec+apt (same package
/// that `apt upgrade` delivers); portable Linux builds are replaced in place; Windows runs the Setup.
/// </summary>
public static class SatraAppUpdate
{
    public record CheckResult(bool HasUpdate, string? LatestTag, string? Version, string? AssetUrl, string? AssetName, string? Sha256 = null);

    public static bool IsDebInstall => OperatingSystem.IsLinux() && AppContext.BaseDirectory.StartsWith("/opt/satran", StringComparison.Ordinal);

    public static async Task<CheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await UpdNet.GetStringAsync(UpdNet.HubDl + "latest.json", ct));
            var root = doc.RootElement;
            var ver = root.GetProperty("version").GetString() ?? "";
            if (!IsNewer(ver, Identity.Version)) return new(false, ver, ver, null, null);
            string? want = null;
            if (OperatingSystem.IsWindows()) want = $"SatraN-Setup-{ver}.exe";
            else if (OperatingSystem.IsLinux()) want = IsDebInstall ? $"satran_{ver}_amd64.deb" : $"SatraN-{ver}-linux-x64.tar.gz";
            else if (OperatingSystem.IsMacOS()) want = $"SatraN-{ver}-osx-{(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64")}.app.zip";
            if (want == null || !root.GetProperty("files").TryGetProperty(want, out var f)) return new(false, ver, ver, null, null);
            return new(true, ver, ver, UpdNet.HubDl + f.GetProperty("url").GetString(), want, f.GetProperty("sha256").GetString());
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SatraAppUpdate", ex);
            return new(false, null, null, null, null);
        }
    }

    public static async Task<(bool ok, string? path, string? error)> DownloadAsync(string url, string fileName,
        IProgress<(long got, long? total)>? progress, CancellationToken ct = default, string? sha256 = null)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "SatraN-update");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, fileName);
            await UpdNet.DownloadAsync(url, dest, sha256, progress, ct);
            return (true, dest, null);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SatraAppUpdate", ex);
            return (false, null, ex.Message);
        }
    }

    /// <summary>Installs and restarts. Returns an error message, or null when the app is about to restart.</summary>
    public static async Task<string?> InstallAsync(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Setup closes/replaces the app and starts the new one; quit so files aren't locked.
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
                await Quit();
                return null;
            }
            if (OperatingSystem.IsLinux() && path.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo("pkexec", ["apt-get", "install", "-y", "--allow-downgrades", path])
                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                using var p = Process.Start(psi)!;
                var err = p.StandardError.ReadToEndAsync();
                await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                {
                    var e = (await err).Trim();
                    return p.ExitCode is 126 or 127 ? "cancelled" : (e.Length > 0 ? e.Split('\n')[^1] : "apt exit " + p.ExitCode);
                }
                Relaunch("/opt/satran/SatraN");
                await Quit();
                return null;
            }
            if (OperatingSystem.IsLinux() && path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                var baseDir = AppContext.BaseDirectory.TrimEnd('/');
                var stage = baseDir + ".new";
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                Directory.CreateDirectory(stage);
                var tar = Process.Start("tar", ["-xzf", path, "-C", stage, "--strip-components=1"])!;
                await tar.WaitForExitAsync();
                if (tar.ExitCode != 0 || !File.Exists(Path.Combine(stage, "SatraN"))) return "extract failed";
                // swap after we exit: a tiny shell script waits for our pid
                var sh = $"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.3; done; rm -rf '{baseDir}.old'; mv '{baseDir}' '{baseDir}.old' && mv '{stage}' '{baseDir}' && rm -rf '{baseDir}.old'; setsid '{baseDir}/SatraN' >/dev/null 2>&1 &";
                Process.Start(new ProcessStartInfo("sh", ["-c", sh]) { UseShellExecute = false });
                await Quit();
                return null;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SatraAppUpdate", ex);
            return ex.Message;
        }
    }

    private static void Relaunch(string exe)
    {
        var sh = $"while kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.3; done; setsid '{exe}' >/dev/null 2>&1 &";
        Process.Start(new ProcessStartInfo("sh", ["-c", sh]) { UseShellExecute = false });
    }

    private static async Task Quit()
    {
        SatraState.Save();
        await AppManager.Instance.AppExitAsync(false);
        Environment.Exit(0);
    }

    public static bool IsNewer(string remote, string local)
    {
        static int[] Parts(string v) =>
            Regex.Matches(v, @"\d+").Select(m => int.Parse(m.Value)).DefaultIfEmpty(0).ToArray();
        var a = Parts(remote);
        var b = Parts(local);
        var n = Math.Max(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
