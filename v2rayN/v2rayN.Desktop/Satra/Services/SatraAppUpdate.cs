using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Checks GitHub releases for SatraN (liamlope/satran) and downloads/installs the matching artifact.
/// </summary>
public static class SatraAppUpdate
{
    private const string Repo = "liamlope/satran";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public record CheckResult(bool HasUpdate, string? LatestTag, string? Version, string? AssetUrl, string? AssetName);

    static SatraAppUpdate()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(Identity.App);
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static async Task<CheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode) return new(false, null, null, null, null);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
            if (!IsNewer(tag, Identity.Version)) return new(false, tag, tag, null, null);

            string? url = null, name = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var n = a.GetProperty("name").GetString() ?? "";
                    var u = a.GetProperty("browser_download_url").GetString();
                    if (u.IsNullOrEmpty()) continue;
                    if (OperatingSystem.IsWindows() &&
                        (n.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                         (n.Contains("win-x64", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))))
                    {
                        // Prefer Setup.exe when present
                        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || url == null)
                        {
                            url = u; name = n;
                            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) break;
                        }
                    }
                    else if (OperatingSystem.IsLinux() &&
                             (n.EndsWith("_amd64.deb", StringComparison.OrdinalIgnoreCase) ||
                              n.Contains("linux-x64", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (n.EndsWith(".deb", StringComparison.OrdinalIgnoreCase) || url == null)
                        {
                            url = u; name = n;
                            if (n.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)) break;
                        }
                    }
                }
            }
            return new(url != null, tag, tag, url, name);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SatraAppUpdate", ex);
            return new(false, null, null, null, null);
        }
    }

    public static async Task<(bool ok, string? path, string? error)> DownloadAsync(
        string url,
        string fileName,
        IProgress<(long got, long? total)>? progress,
        CancellationToken ct = default)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "SatraN-update");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, fileName);
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(dest);
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await input.ReadAsync(buf, ct)) > 0)
            {
                await output.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                progress?.Report((got, total));
            }
            return (true, dest, null);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("SatraAppUpdate", ex);
            return (false, null, ex.Message);
        }
    }

    public static async Task InstallAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            }
            else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Extract next to a temp folder and run Setup.cmd / Install script if present
                var dest = Path.Combine(Path.GetTempPath(), "SatraN-update-extract");
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(path, dest);
                var setup = Directory.GetFiles(dest, "SatraN-Setup.cmd", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.GetFiles(dest, "Install-SatraN.ps1", SearchOption.AllDirectories).FirstOrDefault();
                if (setup != null)
                {
                    if (setup.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{setup}\"") { UseShellExecute = true, Verb = "runas" });
                    else
                        Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas" });
                }
            }
            return;
        }

        if (OperatingSystem.IsLinux() && path.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("pkexec", $"apt-get install -y \"{path}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
    }

    private static bool IsNewer(string remote, string local)
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
