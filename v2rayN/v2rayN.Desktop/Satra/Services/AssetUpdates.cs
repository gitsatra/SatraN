using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Settings › Updates: geo data, sing-box rule sets, Xray and sing-box cores. Each update is validated before
/// it goes live (cores: "version" runs; geo: Xray loads a config that uses the Iran/China/Russia lists;
/// rule sets: SRS header) and the previous files are kept for one-tap rollback.
/// Geo/rule files live in the user bin dir (XRAY_LOCATION_ASSET). On a .deb install the cores live in
/// /opt/satran (they carry CAP_NET_ADMIN for VPN mode), so core updates/rollbacks there go through pkexec.
/// </summary>
public static class AssetUpdates
{
    public enum Kind { Geo, Rules, Xray, SingBox }

    public record Info(Kind Kind, string Current, string? Latest, DateTime? UpdatedAt, bool CanRollback);

    private static string Bin(string f = "") => Utils.GetBinPath(f);
    private static string Rb(Kind k) => Path.Combine(Utils.StartupPath(), "rollback", k.ToString().ToLowerInvariant());
    private static bool Win => OperatingSystem.IsWindows();
    private static string Exe(string n) => Win ? n + ".exe" : n;

    private static readonly string[] Srs =
    [
        "geoip-private", "geosite-private", "geosite-cn", "geoip-cn", "geosite-category-ru", "geoip-ru",
        "geosite-category-ir", "geoip-ir", "geosite-category-ads-all", "geosite-google", "geosite-geolocation-!cn", "geosite-geolocation-cn",
    ];

    // ---------- where the live core binary is ----------
    private static string PackagedCore(Kind k) => k == Kind.Xray
        ? Path.Combine(Utils.GetBaseDirectory("bin"), "xray", Exe("xray"))
        : Path.Combine(Utils.GetBaseDirectory("bin"), "sing_box", Exe("sing-box"));

    private static string LiveCore(Kind k)
    {
        var user = k == Kind.Xray ? Utils.GetBinPath(Exe("xray"), "xray") : Utils.GetBinPath(Exe("sing-box"), "sing_box");
        if (File.Exists(user))
        {
            var fi = new FileInfo(user);
            return fi.LinkTarget != null ? Path.GetFullPath(fi.LinkTarget, Path.GetDirectoryName(user)!) : user;
        }
        return PackagedCore(k);
    }

    public static bool CoreNeedsRoot(Kind k) => NeedsRoot(LiveCore(k));
    private static bool NeedsRoot(string path) => OperatingSystem.IsLinux() && path.StartsWith("/opt/", StringComparison.Ordinal);

    public static async Task<string> CoreVersionAsync(Kind k)
    {
        try
        {
            var psi = new ProcessStartInfo(LiveCore(k), "version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var parts = o.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
            // "Xray 26.10.21 (...)" / "sing-box version 1.14.3"
            return k == Kind.Xray ? parts.ElementAtOrDefault(1) ?? "?" : parts.ElementAtOrDefault(2) ?? "?";
        }
        catch { return "?"; }
    }

    private static string Meta(Kind k) => SatraState.I.Assets.TryGetValue(k.ToString(), out var v) ? v : "";

    public static async Task<Info> InfoAsync(Kind k, bool online, CancellationToken ct = default)
    {
        var cur = k switch
        {
            Kind.Xray or Kind.SingBox => await CoreVersionAsync(k),
            _ => Meta(k) is { Length: > 0 } m ? m : L.S("asset_bundled"),
        };
        string? latest = null;
        if (online)
        {
            try { latest = await LatestAsync(k, ct); } catch (Exception ex) { Logging.SaveLog("asset latest " + k, ex); }
        }
        DateTime? at = SatraState.I.Assets.TryGetValue(k + ".at", out var a) && DateTime.TryParse(a, out var d) ? d : null;
        return new Info(k, cur, latest, at, Directory.Exists(Rb(k)) && Directory.EnumerateFileSystemEntries(Rb(k)).Any());
    }

    private static async Task<string> GhLatestTag(string repo, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await UpdNet.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", ct));
        return doc.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v');
    }

    private static Task<string> LatestAsync(Kind k, CancellationToken ct) => k switch
    {
        Kind.Geo => GhLatestTag("Loyalsoldier/v2ray-rules-dat", ct),
        Kind.Xray => GhLatestTag("XTLS/Xray-core", ct),
        Kind.SingBox => GhLatestTag("SagerNet/sing-box", ct),
        _ => Task.FromResult(DateTime.UtcNow.ToString("yyyy-MM-dd")),
    };

    public static bool IsNewer(Info i) => i.Latest != null && (i.Kind == Kind.Rules
        ? i.UpdatedAt == null || i.UpdatedAt < DateTime.Now.AddDays(-3)
        : i.Current != i.Latest && (i.Kind == Kind.Geo || SatraAppUpdate.IsNewer(i.Latest, i.Current)));

    // ---------- update ----------
    public static async Task<string?> UpdateAsync(Kind k, IProgress<(long, long?)>? prog, CancellationToken ct = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "satran-asset-" + k + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(tmp);
        try
        {
            var ver = await LatestAsync(k, ct);
            switch (k)
            {
                case Kind.Geo:
                {
                    const string b = "https://github.com/Loyalsoldier/v2ray-rules-dat/releases/latest/download/";
                    foreach (var f in new[] { "geoip.dat", "geosite.dat" })
                    {
                        var sha = (await UpdNet.GetStringAsync(b + f + ".sha256sum", ct)).Split(' ')[0].Trim();
                        await UpdNet.DownloadAsync(b + f, Path.Combine(tmp, f), sha, prog, ct);
                    }
                    if (await ValidateGeoAsync(tmp) is { } bad) return bad;
                    Swap(k, tmp, Bin(), ["geoip.dat", "geosite.dat"]);
                    break;
                }
                case Kind.Rules:
                {
                    var got = new List<string>();
                    foreach (var t in Srs)
                    {
                        var kind = t[..t.IndexOf('-')];
                        var urls = t.EndsWith("-ir")
                            ? new[] { $"https://raw.githubusercontent.com/chocolate4u/Iran-sing-box-rules/rule-set/{t}.srs" }
                            : [$"https://raw.githubusercontent.com/2dust/sing-box-rules/rule-set-{kind}/{t}.srs"];
                        foreach (var u in urls)
                        {
                            try
                            {
                                var dst = Path.Combine(tmp, t + ".srs");
                                await UpdNet.DownloadAsync(u, dst, null, null, ct);
                                var head = new byte[3];
                                await using (var fs = File.OpenRead(dst)) _ = await fs.ReadAsync(head, ct);
                                if (head is [(byte)'S', (byte)'R', (byte)'S']) { got.Add(t + ".srs"); break; }
                                File.Delete(dst);
                            }
                            catch (Exception ex) when (!ct.IsCancellationRequested) { Logging.SaveLog("srs " + t, ex); }
                        }
                    }
                    if (got.Count == 0) return L.S("asset_err_download");
                    Directory.CreateDirectory(Bin("srss"));
                    Swap(k, tmp, Bin("srss"), got);
                    break;
                }
                case Kind.Xray or Kind.SingBox:
                {
                    var (url, inner) = CoreAsset(k, ver);
                    var arc = Path.Combine(tmp, Path.GetFileName(url));
                    await UpdNet.DownloadAsync(url, arc, await CoreSha(k, url, ct), prog, ct);
                    var x = Path.Combine(tmp, "x");
                    if (arc.EndsWith(".zip")) ZipFile.ExtractToDirectory(arc, x);
                    else
                    {
                        Directory.CreateDirectory(x);
                        using var t = Process.Start("tar", ["-xzf", arc, "-C", x])!;
                        await t.WaitForExitAsync(ct);
                    }
                    var exe = Directory.GetFiles(x, inner, SearchOption.AllDirectories).FirstOrDefault();
                    if (exe == null) return L.S("asset_err_download");
                    if (!Win) File.SetUnixFileMode(exe, (UnixFileMode)0b111_101_101);
                    if (!await RunsAsync(exe)) return L.S("asset_err_invalid");
                    if (await InstallCoreAsync(k, exe) is { } err) return err;
                    break;
                }
            }
            SatraState.I.Assets[k.ToString()] = ver;
            SatraState.I.Assets[k + ".at"] = DateTime.Now.ToString("o");
            SatraState.Save();
            await ReconnectIfOn();
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logging.SaveLog("asset update " + k, ex);
            return ex is InvalidDataException ? L.S("asset_err_invalid") : L.S("asset_err_download") + " (" + ex.Message + ")";
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    private static (string url, string inner) CoreAsset(Kind k, string ver)
    {
        var arm = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
        if (k == Kind.Xray)
        {
            var n = Win ? "Xray-windows-64" : OperatingSystem.IsMacOS() ? (arm ? "Xray-macos-arm64-v8a" : "Xray-macos-64") : arm ? "Xray-linux-arm64-v8a" : "Xray-linux-64";
            return ($"https://github.com/XTLS/Xray-core/releases/download/v{ver}/{n}.zip", Exe("xray"));
        }
        var os = Win ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var ext = Win ? "zip" : "tar.gz";
        return ($"https://github.com/SagerNet/sing-box/releases/download/v{ver}/sing-box-{ver}-{os}-{(arm ? "arm64" : "amd64")}.{ext}", Exe("sing-box"));
    }

    private static async Task<string?> CoreSha(Kind k, string url, CancellationToken ct)
    {
        if (k != Kind.Xray) return null; // sing-box publishes no per-file digests; TLS + "version" check instead
        try
        {
            var dg = await UpdNet.GetStringAsync(url + ".dgst", ct);
            var line = dg.Split('\n').FirstOrDefault(l => l.StartsWith("SHA2-256", StringComparison.OrdinalIgnoreCase) || l.StartsWith("SHA256", StringComparison.OrdinalIgnoreCase));
            return line?.Split('=').LastOrDefault()?.Trim();
        }
        catch { return null; }
    }

    private static async Task<bool> RunsAsync(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
            var o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 && o.Length > 0;
        }
        catch { return false; }
    }

    private static async Task<string?> ValidateGeoAsync(string dir)
    {
        var cfg = Path.Combine(dir, "t.json");
        await File.WriteAllTextAsync(cfg, """
{"outbounds":[{"protocol":"freedom","tag":"d"}],"routing":{"rules":[
{"type":"field","domain":["geosite:category-ir","geosite:cn","geosite:category-ru","geosite:private"],"outboundTag":"d"},
{"type":"field","ip":["geoip:ir","geoip:cn","geoip:ru","geoip:private"],"outboundTag":"d"}]}}
""");
        var psi = new ProcessStartInfo(LiveCore(Kind.Xray), ["run", "-test", "-c", cfg]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.Environment["XRAY_LOCATION_ASSET"] = dir;
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync() + await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        File.Delete(cfg);
        return p.ExitCode == 0 ? null : L.S("asset_err_invalid") + " " + o.Split('\n').LastOrDefault(l => l.Length > 0);
    }

    /// <summary>Move current files to the rollback folder, then put the new ones in place.</summary>
    private static void Swap(Kind k, string from, string to, IEnumerable<string> names)
    {
        var rb = Rb(k);
        if (Directory.Exists(rb)) Directory.Delete(rb, true);
        Directory.CreateDirectory(rb);
        foreach (var n in names)
        {
            var dst = Path.Combine(to, n);
            if (File.Exists(dst)) File.Copy(dst, Path.Combine(rb, n), true);
            else File.WriteAllText(Path.Combine(rb, n + ".absent"), "");
            File.Copy(Path.Combine(from, n), dst + ".new", true);
            File.Move(dst + ".new", dst, true);
        }
        File.WriteAllText(Path.Combine(rb, ".target"), to);
        File.WriteAllText(Path.Combine(rb, ".version"), Meta(k));
    }

    private static async Task<string?> InstallCoreAsync(Kind k, string exe)
    {
        var live = LiveCore(k);
        var rb = Rb(k);
        if (Directory.Exists(rb)) Directory.Delete(rb, true);
        Directory.CreateDirectory(rb);
        if (File.Exists(live)) File.Copy(live, Path.Combine(rb, Path.GetFileName(live)), true);
        File.WriteAllText(Path.Combine(rb, ".target"), live);
        File.WriteAllText(Path.Combine(rb, ".version"), await CoreVersionAsync(k));
        await PauseCore();
        if (NeedsRoot(live)) return await RootCopyAsync(exe, live);
        File.Copy(exe, live + ".new", true);
        File.Move(live + ".new", live, true);
        return null;
    }

    /// <summary>pkexec (admin password) copy into /opt + restore VPN capabilities.</summary>
    private static async Task<string?> RootCopyAsync(string src, string dst)
    {
        var stage = Path.Combine(Path.GetTempPath(), "satran-core-" + Guid.NewGuid().ToString("N")[..6]);
        File.Copy(src, stage, true);
        var sh = "set -e; install -m 0755 \"$1\" \"$2.new\"; mv -f \"$2.new\" \"$2\"; " +
                 "S=$(command -v setcap || echo /usr/sbin/setcap); $S cap_net_admin,cap_net_bind_service,cap_net_raw+ep \"$2\" && touch \"$2.caps\"";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pkexec", ["/bin/sh", "-c", sh, "sh", stage, dst]) { UseShellExecute = false, RedirectStandardError = true })!;
            var e = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? null : p.ExitCode is 126 or 127 ? L.S("asset_err_cancelled") : e.Trim();
        }
        finally { try { File.Delete(stage); } catch { } }
    }

    public static async Task<string?> RollbackAsync(Kind k)
    {
        var rb = Rb(k);
        if (!Directory.Exists(rb) || !File.Exists(Path.Combine(rb, ".target"))) return L.S("asset_no_rollback");
        try
        {
            var target = File.ReadAllText(Path.Combine(rb, ".target"));
            var prevVer = File.Exists(Path.Combine(rb, ".version")) ? File.ReadAllText(Path.Combine(rb, ".version")) : "";
            if (k is Kind.Xray or Kind.SingBox)
            {
                var src = Path.Combine(rb, Path.GetFileName(target));
                await PauseCore();
                if (NeedsRoot(target)) { if (await RootCopyAsync(src, target) is { } e) return e; }
                else File.Copy(src, target, true);
            }
            else
            {
                foreach (var f in Directory.GetFiles(rb))
                {
                    var n = Path.GetFileName(f);
                    if (n.StartsWith('.')) continue;
                    if (n.EndsWith(".absent")) { File.Delete(Path.Combine(target, n[..^7])); continue; }
                    File.Copy(f, Path.Combine(target, n), true);
                }
            }
            Directory.Delete(rb, true);
            if (prevVer.Length > 0 && k is Kind.Geo or Kind.Rules) SatraState.I.Assets[k.ToString()] = prevVer;
            else SatraState.I.Assets.Remove(k.ToString());
            SatraState.I.Assets.Remove(k + ".at");
            SatraState.Save();
            await ReconnectIfOn();
            return null;
        }
        catch (Exception ex) { Logging.SaveLog("asset rollback " + k, ex); return ex.Message; }
    }

    private static bool _paused;

    /// <summary>Windows locks running executables: stop the tunnel while a core is replaced.</summary>
    private static async Task PauseCore()
    {
        if (!Win || SatraCore.I.Phase != ConnPhase.On) return;
        _paused = true;
        await SatraCore.I.DisconnectAsync();
    }

    private static async Task ReconnectIfOn()
    {
        if (_paused) { _paused = false; await SatraCore.I.ConnectAsync(); }
        else if (SatraCore.I.Phase == ConnPhase.On) await SatraCore.I.ReconnectAsync();
    }
}
