namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Persisted per-app and per-site traffic history (bytes per day). Fed from the core's connection table
/// (sing-box Clash API: process name/path on Windows and Linux, sniffed host otherwise). Polls every 5 s
/// only while connected; nothing runs when Off.
/// </summary>
public static class UsageHistory
{
    private static readonly Dictionary<string, (ulong up, ulong down)> Last = [];
    private static bool _busy;
    private static int _n;

    public static string AppName(MetadataItem m) =>
        m.process is { Length: > 0 } p ? p : m.processPath is { Length: > 0 } pp ? Path.GetFileName(pp) : "";

    public static string Site(MetadataItem m)
    {
        var h = (m.host is { Length: > 0 } x ? x : m.sniffHost) ?? "";
        h = h.Trim().TrimEnd('.').ToLowerInvariant();
        if (h.Length == 0 || System.Net.IPAddress.TryParse(h, out _)) return h;
        // fold to registrable-ish domain: keep last two labels (three for co.uk/ac.ir style)
        var parts = h.Split('.');
        if (parts.Length <= 2) return h;
        var two = parts[^2];
        var keep = two.Length <= 3 && parts[^1].Length == 2 && parts.Length >= 3 ? 3 : 2;
        return string.Join('.', parts[^keep..]);
    }

    public static async Task PollAsync()
    {
        if (_busy || SatraCore.I.Phase != ConnPhase.On) { Last.Clear(); return; }
        _busy = true;
        try
        {
            var ret = await ClashApiManager.Instance.GetConnections();
            if (ret?.connections == null) return;
            var day = SatraState.I.Usage.TryGetValue(DateTime.Today.ToString("yyyy-MM-dd"), out var d) ? d
                : SatraState.I.Usage[DateTime.Today.ToString("yyyy-MM-dd")] = d = [];
            var seen = new HashSet<string>();
            foreach (var c in ret.connections)
            {
                if (c.id == null || c.metadata == null) continue;
                seen.Add(c.id);
                Last.TryGetValue(c.id, out var prev);
                var dB = (long)((c.upload >= prev.up ? c.upload - prev.up : 0) + (c.download >= prev.down ? c.download - prev.down : 0));
                Last[c.id] = (c.upload, c.download);
                if (dB <= 0) continue;
                var app = AppName(c.metadata);
                var site = Site(c.metadata);
                if (app.Length > 0) d["a:" + app] = d.GetValueOrDefault("a:" + app) + dB;
                if (site.Length > 0) d["d:" + site] = d.GetValueOrDefault("d:" + site) + dB;
            }
            foreach (var k in Last.Keys.Where(k => !seen.Contains(k)).ToList()) Last.Remove(k);
            if (++_n % 12 == 0) Prune();
        }
        catch (Exception ex) { Logging.SaveLog("satra usage", ex); }
        finally { _busy = false; }
    }

    private static void Prune()
    {
        var cut = DateTime.Today.AddDays(-92).ToString("yyyy-MM-dd");
        foreach (var k in SatraState.I.Usage.Keys.Where(k => string.CompareOrdinal(k, cut) < 0).ToList()) SatraState.I.Usage.Remove(k);
        // keep each day bounded
        foreach (var d in SatraState.I.Usage.Values.Where(v => v.Count > 600))
            foreach (var k in d.Where(kv => kv.Key[0] is 'a' or 'd').OrderBy(kv => kv.Value).Take(d.Count - 500).Select(kv => kv.Key).ToList()) d.Remove(k);
    }

    /// <summary>Top entries over the last <paramref name="days"/> days. kind: 'a' apps, 'd' sites.</summary>
    public static List<(string Name, long Bytes)> Top(char kind, int days, string? filter = null, int take = 30)
    {
        var acc = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < days; i++)
        {
            if (!SatraState.I.Usage.TryGetValue(DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"), out var d)) continue;
            foreach (var (k, v) in d)
                if (k.Length > 2 && k[0] == kind) acc[k[2..]] = acc.GetValueOrDefault(k[2..]) + v;
        }
        return acc.Where(kv => string.IsNullOrWhiteSpace(filter) || kv.Key.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Value).Take(take).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Per-config bytes, recorded from the core's stats every second while connected (any mode).</summary>
    public static void AddConfig(string? id, long up, long down)
    {
        if (string.IsNullOrEmpty(id) || up + down <= 0) return;
        var key = DateTime.Today.ToString("yyyy-MM-dd");
        lock (SatraState.I.Usage)
        {
            if (!SatraState.I.Usage.TryGetValue(key, out var d)) SatraState.I.Usage[key] = d = [];
            if (up > 0) d["u:" + id] = d.GetValueOrDefault("u:" + id) + up;
            if (down > 0) d["w:" + id] = d.GetValueOrDefault("w:" + id) + down;
        }
    }

    public static Dictionary<string, (long Up, long Down)> PerConfig(int days)
    {
        var acc = new Dictionary<string, (long Up, long Down)>();
        lock (SatraState.I.Usage)
            for (var i = 0; i < days; i++)
            {
                if (!SatraState.I.Usage.TryGetValue(DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"), out var d)) continue;
                foreach (var (k, v) in d)
                {
                    if (k.Length < 3 || k[1] != ':' || (k[0] != 'u' && k[0] != 'w')) continue;
                    var id = k[2..];
                    var c = acc.GetValueOrDefault(id);
                    acc[id] = k[0] == 'u' ? (c.Up + v, c.Down) : (c.Up, c.Down + v);
                }
            }
        return acc;
    }
}
