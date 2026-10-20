namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Linux VPN (TUN) like Outline: CAP_NET_ADMIN is applied in the .deb postinst as root.
/// The app never prompts for a password and never calls pkexec/sudo.
/// </summary>
public static class TunPrivilege
{
    public static string? SingBox => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box), out _);

    private const string PackagedSingBox = "/opt/satran/bin/sing_box/sing-box";

    public static bool Ready =>
        LinuxCaps.HasNetAdmin(SingBox) || LinuxCaps.HasNetAdmin(PackagedSingBox);

    public static Task<bool> EnsureAsync()
    {
        if (!OperatingSystem.IsLinux()) return Task.FromResult(true);
        if (Ready) return Task.FromResult(true);
        Logging.SaveLog("satra tun: CAP_NET_ADMIN missing on packaged cores — reinstall with: sudo dpkg -i satran_*.deb");
        return Task.FromResult(false);
    }
}
