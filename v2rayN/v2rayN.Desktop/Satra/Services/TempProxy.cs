using System.Net;
using System.Net.Http;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// A local SOCKS port for one config without connecting the app: the running core's port when SatraN is on,
/// otherwise a throw-away core (same engine the ping test uses) that is stopped on dispose. Nothing system-wide changes.
/// </summary>
public sealed class TempProxy : IAsyncDisposable
{
    public int Port { get; }
    public bool Temporary => _proc != null;
    public ProfileItemModel? Profile { get; }
    private readonly ServiceLib.Services.ProcessService? _proc;

    private TempProxy(int port, ProfileItemModel? p, ServiceLib.Services.ProcessService? proc) { Port = port; Profile = p; _proc = proc; }

    /// <summary>The running config when on; else <paramref name="want"/>, the selected one, then the fastest pinged ones.</summary>
    public static async Task<TempProxy?> AcquireAsync(ProfileItemModel? want = null, bool tryOthers = false, CancellationToken ct = default)
    {
        var core = SatraCore.I;
        if (core.Phase == ConnPhase.On && (want == null || want.IndexId == core.Active?.IndexId))
            return new TempProxy(AppManager.Instance.GetLocalPort(EInboundProtocol.socks), core.Active, null);
        var cands = new List<ProfileItemModel>();
        if (want != null) cands.Add(want);
        if (core.Active is { } a && !cands.Contains(a)) cands.Add(a);
        if (tryOthers)
            cands.AddRange(core.Profiles.ProfileItems.Where(p => !cands.Contains(p) && PingEngine.I.Ms(p.IndexId) > 0)
                .OrderBy(p => PingEngine.I.Ms(p.IndexId)).Take(3));
        foreach (var p in cands)
        {
            ct.ThrowIfCancellationRequested();
            var t = await StartAsync(p, ct);
            if (t != null) return t;
        }
        return null;
    }

    private static async Task<TempProxy?> StartAsync(ProfileItemModel p, CancellationToken ct)
    {
        try
        {
            var prof = await AppManager.Instance.GetProfileItem(p.IndexId);
            if (prof == null || prof.ConfigType == EConfigType.Custom) return null;
            var item = new ServerTestItem { IndexId = p.IndexId, Address = prof.Address, Port = prof.Port, ConfigType = prof.ConfigType, Profile = prof,
                CoreType = AppManager.Instance.GetCoreType(prof, prof.ConfigType), AllowTest = true };
            var proc = await CoreManager.Instance.LoadCoreConfigSpeedtest(item);
            if (proc == null) return null;
            var t = new TempProxy(item.Port, p, proc);
            // wait until the port answers and the server actually passes traffic
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(350, ct);
                if (await WorksAsync(t.Port, ct)) return t;
            }
            await t.DisposeAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Logging.SaveLog("temp proxy", ex); }
        return null;
    }

    public static async Task<bool> WorksAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var h = new SocketsHttpHandler { Proxy = new WebProxy($"socks5://127.0.0.1:{port}"), UseProxy = true, ConnectTimeout = TimeSpan.FromSeconds(4) };
            using var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(6) };
            using var r = await c.GetAsync("https://www.gstatic.com/generate_204", ct);
            return (int)r.StatusCode is 204 or 200;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_proc != null) { try { await _proc.StopAsync(); } catch { } }
    }
}
