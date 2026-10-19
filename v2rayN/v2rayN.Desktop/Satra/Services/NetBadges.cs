using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>CDN / tunnel detection and exit-country bookkeeping for server rows.</summary>
public static class NetBadges
{
    private static readonly (string name, string[] cidrs)[] Cdns =
    [
        ("Cloudflare", ["173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20", "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13", "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22"]),
        ("Fastly", ["23.235.32.0/20", "43.249.72.0/22", "103.244.50.0/24", "103.245.222.0/23", "103.245.224.0/24", "104.156.80.0/20", "140.248.64.0/18", "140.248.128.0/17", "146.75.0.0/17", "151.101.0.0/16", "157.52.64.0/18", "167.82.0.0/17", "167.82.128.0/20", "167.82.160.0/20", "167.82.224.0/20", "172.111.64.0/18", "185.31.16.0/22", "199.27.72.0/21", "199.232.0.0/16"]),
        ("Arvan", ["185.143.232.0/22", "185.215.232.0/22", "188.229.116.16/29", "94.101.182.0/27", "2.144.3.128/28", "89.45.48.64/28", "37.32.16.0/27", "37.32.17.0/27", "37.32.18.0/27", "37.32.19.0/27", "45.15.200.0/22"]),
    ];

    private static readonly ConcurrentDictionary<string, string?> CdnCache = new();
    private static readonly ConcurrentDictionary<string, string?> HostCountry = new();

    private static bool InCidr(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        var net = IPAddress.Parse(parts[0]).GetAddressBytes();
        var b = ip.GetAddressBytes();
        if (b.Length != net.Length) return false;
        var bits = int.Parse(parts[1]);
        for (var i = 0; i < b.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((b[i] & mask) != (net[i] & mask)) return false;
        }
        return true;
    }

    public static string? CdnOfIp(IPAddress ip) => Cdns.FirstOrDefault(c => c.cidrs.Any(x => InCidr(ip, x))).name;

    private static async Task<IPAddress?> Resolve(string host)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return ip;
        try
        {
            var a = await Dns.GetHostAddressesAsync(host);
            return a.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork) ?? a.FirstOrDefault();
        }
        catch { return null; }
    }

    public static async Task<string?> CdnAsync(string host)
    {
        if (CdnCache.TryGetValue(host, out var v)) return v;
        var ip = await Resolve(host);
        v = ip == null ? null : CdnOfIp(ip);
        CdnCache[host] = v;
        return v;
    }

    /// <summary>Country of the server's own address (cached); used to spot tunnels (entry in Iran, exit abroad).</summary>
    public static async Task<string?> EntryCountryAsync(string host)
    {
        if (HostCountry.TryGetValue(host, out var v)) return v;
        HostCountry[host] = null;
        var ip = await Resolve(host);
        if (ip == null || CdnOfIp(ip) != null) return null;
        try
        {
            using var res = await Net.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"http://ip-api.com/json/{ip}?fields=countryCode"));
            if (res?.IsSuccessStatusCode == true)
            {
                v = JsonNode.Parse(await res.Content.ReadAsStringAsync())?["countryCode"]?.ToString();
                HostCountry[host] = v;
            }
        }
        catch { }
        return v;
    }

    /// Stable per-config key (survives subscription refreshes, which re-create index ids).
    public static string GeoKey(ProfileItemModel p) => Pages.ServersPage.Key(p);

    /// <summary>Real exit country measured by <see cref="ExitProbe"/>; null (blank flag) when unknown.</summary>
    public static string? ExitCc(ProfileItemModel p) =>
        SatraState.I.Geo.TryGetValue(GeoKey(p), out var g) && g.Cc.Length == 2 ? g.Cc : null;

    public static void ForgetExit(IEnumerable<ProfileItemModel> items)
    {
        lock (SatraState.I.Geo) foreach (var p in items) SatraState.I.Geo.Remove(GeoKey(p));
    }

    /// Windows has no colour flag glyphs (it shows two letters), so draw a neat code tile there.
    public static Control FlagView(string? cc, double size)
    {
        if (!OperatingSystem.IsWindows()) { var t = Ui.T(Flag(cc), size); t.VerticalAlignment = VerticalAlignment.Center; return t; }
        var label = cc is { Length: 2 } ? cc.ToUpperInvariant() : "··";
        return new Border
        {
            Width = size * 1.25, Height = size * 0.9, CornerRadius = new CornerRadius(5), Background = P.B(P.Fill),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = label, FontSize = size * 0.42, FontWeight = FontWeight.Bold, Foreground = P.B(P.Text2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    public static string Flag(string? cc)
    {
        if (OperatingSystem.IsWindows()) return cc is { Length: 2 } ? cc.ToUpperInvariant() : "";
        if (cc is not { Length: 2 }) return "🌐";
        return string.Concat(cc.ToUpperInvariant().Select(ch => char.ConvertFromUtf32(0x1F1E6 + ch - 'A')));
    }
}
