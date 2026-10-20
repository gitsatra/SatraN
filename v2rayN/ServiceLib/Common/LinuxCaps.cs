namespace ServiceLib.Common;

/// <summary>Linux file capabilities helpers. Desktop apps often lack /usr/sbin on PATH.</summary>
public static class LinuxCaps
{
    private static readonly string[] GetCapBins = ["/usr/sbin/getcap", "/sbin/getcap", "getcap"];

    /// <summary>True when path (or its symlink target) has CAP_NET_ADMIN.</summary>
    public static bool HasNetAdmin(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            foreach (var pth in Resolve(path))
            {
                if (RunGetCap(pth).Contains("cap_net_admin", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return File.Exists(path + ".caps");
    }

    public static IEnumerable<string> Resolve(string path)
    {
        var list = new List<string> { path };
        try
        {
            var li = new FileInfo(path);
            if (li.LinkTarget is { Length: > 0 } t)
            {
                var real = Path.IsPathRooted(t) ? t : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, t));
                if (!string.Equals(real, path, StringComparison.Ordinal)) list.Add(real);
            }
        }
        catch { }
        return list;
    }

    private static string RunGetCap(string path)
    {
        foreach (var bin in GetCapBins)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(bin, [path])
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) continue;
                var o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(2000);
                // getcap on a symlink prints nothing — keep trying other bins/paths
                if (!string.IsNullOrWhiteSpace(o)) return o;
            }
            catch (System.ComponentModel.Win32Exception) { /* next candidate */ }
            catch { }
        }
        return "";
    }
}
