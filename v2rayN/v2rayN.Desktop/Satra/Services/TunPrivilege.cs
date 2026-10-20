namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Linux VPN (TUN) like OpenVPN/Outline: packaged cores under /opt keep CAP_NET_ADMIN after .deb
/// install. User-data paths are symlinks. If caps are missing, repair once via pkexec + polkit
/// action <c>com.satran.fix-caps</c> (no typed password for an active local session).
/// </summary>
public static class TunPrivilege
{
    public static string? SingBox => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box), out _);
    public static string? Xray => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray), out _);

    private static string? FixCapsHelper =>
        new[] { "/opt/satran/bin/satran-fix-caps", Path.Combine(Utils.GetBaseDirectory("bin"), "satran-fix-caps") }
            .FirstOrDefault(File.Exists);

    public static async Task<bool> EnsureAsync()
    {
        var sb = SingBox;
        if (string.IsNullOrEmpty(sb) || !File.Exists(sb)) return false;
        if (LinuxCaps.HasNetAdmin(sb)) return true;

        var helper = FixCapsHelper;
        if (string.IsNullOrEmpty(helper))
        {
            Logging.SaveLog("satra tun privilege: satran-fix-caps helper missing");
            return false;
        }

        try
        {
            // polkit action com.satran.fix-caps → allow_active=yes → no password prompt
            var psi = new System.Diagnostics.ProcessStartInfo("pkexec", [helper])
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            // Touch markers next to user symlinks so older checks stay happy
            foreach (var t in new[] { sb, Xray })
            {
                if (string.IsNullOrEmpty(t)) continue;
                try { File.WriteAllBytes(t + ".caps", []); } catch { }
            }
            return p.ExitCode == 0 && LinuxCaps.HasNetAdmin(sb);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra tun privilege", ex);
            return false;
        }
    }
}
