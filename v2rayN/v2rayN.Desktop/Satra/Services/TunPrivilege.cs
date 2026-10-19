namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Linux VPN mode needs CAP_NET_ADMIN for the TUN device and routes. The .deb grants it in postinst;
/// otherwise (tarball) we ask once through polkit (pkexec) to setcap the bundled sing-box, then never again.
/// </summary>
public static class TunPrivilege
{
    public static string? SingBox => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box), out _);

    public static async Task<bool> EnsureAsync()
    {
        var sb = SingBox;
        if (string.IsNullOrEmpty(sb) || !File.Exists(sb)) return false;
        if (File.Exists(sb + ".caps")) return true;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("pkexec", ["/bin/sh", "-c", $"setcap cap_net_admin,cap_net_bind_service,cap_net_raw+ep '{sb}' && touch '{sb}.caps'"]) { UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0 && File.Exists(sb + ".caps");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra tun privilege", ex);
            return false;
        }
    }
}
