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
        if (await HasNetAdminAsync(sb)) return true;

        // Last resort: one-time pkexec only if the packaged binary also lacks caps (tarball install)
        try
        {
            var targets = new List<string> { sb };
            var xr = Xray;
            if (!string.IsNullOrEmpty(xr) && File.Exists(xr) && !await HasNetAdminAsync(xr)) targets.Add(xr);
            static string Q(string p) => p.Replace("'", "'\\''");
            var cmds = string.Join("; ", targets.Select(t =>
                $"setcap cap_net_admin,cap_net_bind_service,cap_net_raw+ep '{Q(t)}' && touch '{Q(t)}.caps'"));
            var psi = new System.Diagnostics.ProcessStartInfo("pkexec", ["/bin/sh", "-c", cmds]) { UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0 && await HasNetAdminAsync(sb);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra tun privilege", ex);
            return false;
        }
    }

    private static async Task<bool> HasNetAdminAsync(string path)
    {
        try
        {
            // Resolve symlink so getcap sees the packaged binary with real capabilities
            var real = path;
            try { var li = new FileInfo(path); if (li.LinkTarget != null) real = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, li.LinkTarget)); } catch { }
            var psi = new System.Diagnostics.ProcessStartInfo("getcap", [real])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (o.Contains("cap_net_admin", StringComparison.OrdinalIgnoreCase)) return true;
            // Also try the path itself (kernel may report on the symlink)
            if (!string.Equals(real, path, StringComparison.Ordinal))
            {
                psi = new System.Diagnostics.ProcessStartInfo("getcap", [path])
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p2 = System.Diagnostics.Process.Start(psi)!;
                o = await p2.StandardOutput.ReadToEndAsync();
                await p2.WaitForExitAsync();
                return o.Contains("cap_net_admin", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        catch
        {
            return File.Exists(path + ".caps");
        }
    }
}
