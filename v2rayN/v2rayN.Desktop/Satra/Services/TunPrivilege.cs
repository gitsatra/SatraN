namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Linux VPN needs CAP_NET_ADMIN. After .deb install the packaged cores under /opt/satran keep
/// capabilities; CoreManager symlinks the user-data copies to those so VPN works like Outline —
/// no password prompts on every connect.
/// </summary>
public static class TunPrivilege
{
    public static string? SingBox => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box), out _);
    public static string? Xray => CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray), out _);

    public static async Task<bool> EnsureAsync()
    {
        var sb = SingBox;
        if (string.IsNullOrEmpty(sb) || !File.Exists(sb)) return false;
        if (LinuxCaps.HasNetAdmin(sb)) return true;

        // Last resort: one-time pkexec only if the packaged binary also lacks caps (tarball install)
        try
        {
            var targets = new List<string>();
            foreach (var t in new[] { sb, Xray })
            {
                if (string.IsNullOrEmpty(t) || !File.Exists(t) || LinuxCaps.HasNetAdmin(t)) continue;
                // Never setcap a symlink — resolve to the real file (usually under /opt/satran)
                var real = LinuxCaps.Resolve(t).LastOrDefault() ?? t;
                if (!targets.Contains(real)) targets.Add(real);
            }
            if (targets.Count == 0) return LinuxCaps.HasNetAdmin(sb);

            static string Q(string p) => p.Replace("'", "'\\''");
            var cmds = string.Join("; ", targets.Select(t =>
                $"setcap cap_net_admin,cap_net_bind_service,cap_net_raw+ep '{Q(t)}' && touch '{Q(t)}.caps'"));
            // Also mark the user-data paths so CoreManager sees the marker next to the symlink
            foreach (var t in new[] { sb, Xray })
            {
                if (!string.IsNullOrEmpty(t)) cmds += $"; touch '{Q(t)}.caps'";
            }
            var setcap = File.Exists("/usr/sbin/setcap") ? "/usr/sbin/setcap"
                : File.Exists("/sbin/setcap") ? "/sbin/setcap" : "setcap";
            cmds = cmds.Replace("setcap ", setcap + " ");
            var psi = new System.Diagnostics.ProcessStartInfo("pkexec", ["/bin/sh", "-c", cmds]) { UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0 && LinuxCaps.HasNetAdmin(sb);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra tun privilege", ex);
            return false;
        }
    }
}
