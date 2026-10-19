using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Measures the REAL exit country of every config without connecting to it: one throw-away core exposes a
/// local port per config, and each port is asked "what is my IP" (4 at a time, with fallback services).
/// Runs when a subscription is added/updated, on launch for configs never measured, and for the active config
/// on connect. Never during ping. Unknown stays blank; the entry (server) country is never shown as the exit.
/// </summary>
public static class ExitProbe
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static bool Running { get; private set; }

    private static readonly (string url, Func<JsonNode, (string? ip, string? cc)> parse)[] Services =
    [
        ("https://api.ip.sb/geoip", j => (j["ip"]?.ToString(), j["country_code"]?.ToString())),
        ("http://ip-api.com/json/?fields=status,countryCode,query", j => (j["query"]?.ToString(), j["countryCode"]?.ToString())),
        ("https://api.country.is/", j => (j["ip"]?.ToString(), j["country"]?.ToString())),
        ("https://ipwho.is/", j => (j["ip"]?.ToString(), j["country_code"]?.ToString())),
    ];

    /// <summary>Ask the fallback chain through a given local proxy port.</summary>
    public static async Task<(string ip, string cc)?> LookupAsync(int port, CancellationToken ct = default)
    {
        using var h = new SocketsHttpHandler { Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"), UseProxy = true, ConnectTimeout = TimeSpan.FromSeconds(10) };
        using var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(14) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.5");
        foreach (var (url, parse) in Services)
        {
            try
            {
                var s = await c.GetStringAsync(url, ct);
                var (ip, cc) = parse(JsonNode.Parse(s)!);
                if (cc is { Length: 2 } && !string.IsNullOrEmpty(ip)) return (ip!, cc.ToUpperInvariant());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        return null;
    }

    public static IEnumerable<ProfileItemModel> Missing() =>
        SatraCore.I.Profiles.ProfileItems.Where(p => !SatraState.I.Geo.ContainsKey(NetBadges.GeoKey(p)));

    /// <summary>Probe the given configs (default: all with no known exit).</summary>
    public static async Task ProbeAsync(IEnumerable<ProfileItemModel>? which = null)
    {
        if (!await Gate.WaitAsync(0)) return;
        Running = true;
        try
        {
            var list = (which ?? Missing()).Where(p => p.ConfigType != EConfigType.Custom).ToList();
            var ids = list.Select(p => p.IndexId).ToList();
            if (ids.Count == 0) return;
            var map = await AppManager.Instance.GetProfileItemsByIndexIdsAsMap(ids);
            var keys = list.ToDictionary(p => p.IndexId, NetBadges.GeoKey);
            foreach (var chunk in list.Chunk(24))
            {
                var items = new List<ServerTestItem>();
                var i = 0;
                foreach (var p in chunk)
                {
                    if (!map.TryGetValue(p.IndexId, out var prof)) continue;
                    if (!prof.ConfigType.IsComplexType() && prof.Port <= 0) continue;
                    items.Add(new ServerTestItem
                    {
                        IndexId = p.IndexId, Address = prof.Address, Port = prof.Port, ConfigType = prof.ConfigType, QueueNum = i++,
                        Profile = prof, CoreType = AppManager.Instance.GetCoreType(prof, prof.ConfigType),
                    });
                }
                if (items.Count == 0) continue;
                ServiceLib.Services.ProcessService? proc = null;
                try
                {
                    proc = await CoreManager.Instance.LoadCoreConfigSpeedtest(items);
                    if (proc == null) continue;
                    await Task.Delay(1200);
                    await Parallel.ForEachAsync(items.Where(x => x.AllowTest), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (it, ct) =>
                    {
                        var r = await LookupAsync(it.Port, ct);
                        if (r is { } v)
                        {
                            lock (SatraState.I.Geo) SatraState.I.Geo[keys[it.IndexId!]] = new ExitGeo { Cc = v.cc, Ip = v.ip, At = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                            Dispatcher.UIThread.Post(() => SatraCore.I.RaiseChanged());
                        }
                    });
                }
                catch (Exception ex) { Logging.SaveLog("satra exit probe", ex); }
                finally { if (proc != null) await proc.StopAsync(); }
            }
            Dispatcher.UIThread.Post(SatraState.Save);
        }
        finally
        {
            Running = false;
            Gate.Release();
        }
    }
}
