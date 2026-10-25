namespace ServiceLib.Satra;

/// <summary>
/// Satra (v1.8): how traffic reaches the running core. The core itself is either on or off; on top of it:
/// Ports (127.0.0.1 SOCKS/HTTP only, nothing in the OS changes — default, like v2rayN),
/// SystemProxy (OS proxy points at the ports), Tun (whole device through the privileged helper).
/// </summary>
public static class ConnModes
{
    public const string Ports = "socks";
    public const string SystemProxy = "proxy";
    public const string Tun = "tun";
    public const string Default = Ports;
    public static readonly string[] All = [Ports, SystemProxy, Tun];

    public static bool IsValid(string? m) => m is Ports or SystemProxy or Tun;
    public static string Normalize(string? m) => IsValid(m) ? m! : Default;

    /// <summary>Ports and system proxy share the same running core: switching between them only flips the OS proxy.</summary>
    public static bool CanSwitchLive(string from, string to) => from != to && from != Tun && to != Tun;

    /// <summary>UI string key of a mode's short label.</summary>
    public static string Label(string m) => m switch { Tun => "mode_vpn", SystemProxy => "mode_proxy", _ => "mode_proxy_only" };

    /// <summary>Best configs for quick switching: lowest positive delay first, active excluded, unknown/failed last.</summary>
    public static List<T> QuickPicks<T>(IEnumerable<T> items, Func<T, string> id, Func<T, int> delay, string? activeId, int n = 3)
        => items.Where(x => id(x) != activeId)
            .Select((x, i) => (x, i, d: delay(x)))
            .OrderBy(t => t.d > 0 ? 0 : 1).ThenBy(t => t.d > 0 ? t.d : int.MaxValue).ThenBy(t => t.i)
            .Take(n).Select(t => t.x).ToList();
}
