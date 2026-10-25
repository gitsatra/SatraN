using System.Net;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Invisible subscription rescue: when a subscription link is filtered and the direct fetch fails,
/// retry through the connected tunnel's local port, then through up to 3 best-pinging configs
/// (each started in a private test core). No button, no popup — only the normal "updated" state.
/// </summary>
public static class SubFallback
{
    public static string? LastVia;

    public static void Install() => SubscriptionHandler.FallbackFetch = FetchAsync;


    private static async Task<string?> FetchAsync(Func<IWebProxy, Task<string?>> attempt)
    {
        // 1) the connected tunnel (proxy-only or TUN) — its local mixed inbound
        if (SatraCore.I.Phase == ConnPhase.On)
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var r = await attempt(new WebProxy($"socks5://{Global.Loopback}:{port}"));
            if (!string.IsNullOrEmpty(r)) { LastVia = "active"; return r; }
        }
        // 2) best-pinging configs, cycled, each through its own temporary core
        foreach (var p in Candidates())
        {
            var r = await ThroughAsync(p, attempt);
            if (!string.IsNullOrEmpty(r)) { LastVia = p.Remarks; return r; }
        }
        return null;
    }

    internal static IEnumerable<ProfileItemModel> Candidates()
    {
        var all = SatraCore.I.Profiles.ProfileItems.Where(p => p.ConfigType != EConfigType.Custom).ToList();
        var active = SatraCore.I.Active;
        var ranked = all.Where(p => PingEngine.I.Ms(p.IndexId) > 0).OrderBy(p => PingEngine.I.Ms(p.IndexId)).ToList();
        var list = new List<ProfileItemModel>();
        if (active != null) list.Add(active);
        list.AddRange(ranked.Where(p => p.IndexId != active?.IndexId));
        if (list.Count < 3) list.AddRange(all.Where(p => !list.Contains(p)).Take(3 - list.Count));
        return list.Take(3);
    }

    private static async Task<string?> ThroughAsync(ProfileItemModel pm, Func<IWebProxy, Task<string?>> attempt)
    {
        var prof = await AppManager.Instance.GetProfileItem(pm.IndexId);
        if (prof == null) return null;
        var it = new ServerTestItem
        {
            IndexId = prof.IndexId, Address = prof.Address, Port = prof.Port, ConfigType = prof.ConfigType, QueueNum = 0,
            Profile = prof, CoreType = AppManager.Instance.GetCoreType(prof, prof.ConfigType),
        };
        ServiceLib.Services.ProcessService? proc = null;
        try
        {
            proc = await CoreManager.Instance.LoadCoreConfigSpeedtest([it]);
            if (proc == null || !it.AllowTest) return null;
            await Task.Delay(900);
            var r = await attempt(new WebProxy($"socks5://{Global.Loopback}:{it.Port}"));
            return r;
        }
        catch (Exception ex) { Logging.SaveLog("satra sub fallback", ex); return null; }
        finally { if (proc != null) await proc.StopAsync(); }
    }

}
