namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Linux VPN mode needs CAP_NET_ADMIN on the core that actually runs (usually under
/// ~/.local/share/SatraN/bin/…). The .deb setcaps /opt/satran, but copies into the user
/// data dir lose capabilities — so we verify with getcap and setcap the live binary once.
/// </summary>
public static class TunPrivilege
{
    public static string? SingBox => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box), out _);
    public static string? Xray => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray), out _);

    public static async Task<bool> EnsureAsync()
    {
        var sb = SingBox;
        if (string.IsNullOrEmpty(sb) || !File.Exists(sb)) return false;
        if (await HasNetAdminAsync(sb))
        {
            TouchMarker(sb);
            return true;
        }
        try
        {
            var targets = new List<string> { sb };
            var xr = Xray;
            if (!string.IsNullOrEmpty(xr) && File.Exists(xr) && !await HasNetAdminAsync(xr)) targets.Add(xr);
            // Quote-safe: paths have no spaces in our layout; still escape single quotes
            static string Q(string p) => p.Replace("'", "'\\''");
            var cmds = string.Join("; ", targets.Select(t =>
                $"setcap cap_net_admin,cap_net_bind_service,cap_net_raw+ep '{Q(t)}' && touch '{Q(t)}.caps'"));
            var psi = new System.Diagnostics.ProcessStartInfo("pkexec", ["/bin/sh", "-c", cmds]) { UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) return false;
            return await HasNetAdminAsync(sb);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra tun privilege", ex);
            return false;
        }
    }

    private static void TouchMarker(string path)
    {
        try { File.WriteAllBytes(path + ".caps", []); } catch { }
    }

    private static async Task<bool> HasNetAdminAsync(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("getcap", [path])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return o.Contains("cap_net_admin", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Fallback: trust a marker next to a binary that already has +x from the package
            return File.Exists(path + ".caps") && File.Exists("/opt/satran/bin/sing_box/sing-box.caps");
        }
    }
}
