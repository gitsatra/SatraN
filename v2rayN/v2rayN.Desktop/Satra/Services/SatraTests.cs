using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace v2rayN.Desktop.Satra.Services;

public enum PingStatus { None, Pending, Ok, Timeout }
public readonly record struct PingResult(PingStatus Status, int Ms);

/// <summary>
/// Deterministic real-ping engine. Each server has a version number; starting a test bumps it and marks the
/// row Pending. A result is accepted only if it carries the current version, so overlapping or cancelled runs
/// can never resurrect a stale value. Results live in memory only (no persisted "old ping" fallback).
/// </summary>
public sealed class PingEngine
{
    public static readonly PingEngine I = new();
    private readonly ConcurrentDictionary<string, PingResult> _res = new();
    private readonly ConcurrentDictionary<string, long> _ver = new();
    private CancellationTokenSource? _allCts;
    private long _seq;
    public event Action? Changed;
    public bool Running => Volatile.Read(ref _active) > 0;
    public int Total { get; private set; }
    public int Done { get; private set; }
    private int _active;

    public PingResult Get(string? id) => id != null && _res.TryGetValue(id, out var r) ? r : default;
    /// <summary>Delay in v2rayN convention for sorting: &gt;0 ms, -1 timeout, 0 unknown/pending.</summary>
    public int Ms(string? id) => Get(id) switch { { Status: PingStatus.Ok } r => r.Ms, { Status: PingStatus.Timeout } => -1, _ => 0 };

    public Task PingAllAsync(IList<ProfileItemModel> items)
    {
        _allCts?.Cancel();
        _allCts = new CancellationTokenSource();
        return RunAsync(items, _allCts.Token);
    }

    public Task PingOneAsync(ProfileItemModel p) => RunAsync([p], CancellationToken.None);

    private async Task RunAsync(IList<ProfileItemModel> items, CancellationToken ct)
    {
        var list = items.Where(p => !string.IsNullOrEmpty(p.IndexId) && p.ConfigType != EConfigType.Custom).ToList();
        if (list.Count == 0) return;
        var mine = new Dictionary<string, long>();
        foreach (var p in list)
        {
            var v = Interlocked.Increment(ref _seq);
            _ver[p.IndexId] = v; mine[p.IndexId] = v;
            _res[p.IndexId] = new PingResult(PingStatus.Pending, 0);
        }
        Interlocked.Increment(ref _active);
        if (list.Count > 1) { Total = list.Count; Done = 0; }
        Changed?.Invoke();
        try
        {
            // batches of 20 per throw-away core, like v2rayN, but each result is version-checked
            foreach (var batch in list.Chunk(20))
            {
                if (ct.IsCancellationRequested) break;
                await RunBatchAsync(batch, mine, ct);
            }
        }
        finally
        {
            foreach (var p in list) // anything still pending from THIS run and not superseded -> timeout
                if (Current(p.IndexId, mine) && Get(p.IndexId).Status == PingStatus.Pending)
                    _res[p.IndexId] = ct.IsCancellationRequested ? default : new PingResult(PingStatus.Timeout, 0);
            Interlocked.Decrement(ref _active);
            Changed?.Invoke();
        }
    }

    private bool Current(string id, Dictionary<string, long> mine) => _ver.TryGetValue(id, out var v) && v == mine[id];

    private async Task RunBatchAsync(ProfileItemModel[] batch, Dictionary<string, long> mine, CancellationToken ct)
    {
        var map = await AppManager.Instance.GetProfileItemsByIndexIdsAsMap(batch.Select(b => b.IndexId).ToList());
        var items = batch.Where(p => map.ContainsKey(p.IndexId)).Select((p, i) =>
        {
            var prof = map[p.IndexId];
            return new ServerTestItem { IndexId = p.IndexId, Address = p.Address, Port = p.Port, ConfigType = p.ConfigType, QueueNum = i,
                Profile = prof, CoreType = AppManager.Instance.GetCoreType(prof, p.ConfigType), AllowTest = true };
        }).ToList();
        ServiceLib.Services.ProcessService? proc = null;
        try
        {
            proc = await CoreManager.Instance.LoadCoreConfigSpeedtest(items);
            if (proc == null) { foreach (var it in items) Set(it.IndexId, mine, -1); return; }
            await Task.Delay(800, ct);
            await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = items.Count, CancellationToken = ct }, async (it, ict) =>
            {
                int ms;
                try { ms = it.AllowTest ? await ConnectionHandler.GetRealPingTime(new WebProxy($"socks5://{Global.Loopback}:{it.Port}"), ict) : -1; }
                catch (OperationCanceledException) { return; }
                catch { ms = -1; }
                Set(it.IndexId, mine, ms);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.SaveLog("satra ping", ex); }
        finally { if (proc != null) await proc.StopAsync(); }
    }

    private void Set(string id, Dictionary<string, long> mine, int ms)
    {
        if (!Current(id, mine)) return; // superseded by a newer test: drop
        _res[id] = ms > 0 && ms < 10_000 ? new PingResult(PingStatus.Ok, ms) : new PingResult(PingStatus.Timeout, 0);
        ProfileExManager.Instance.SetTestDelay(id, ms > 0 ? ms : -1);
        Done++;
        Changed?.Invoke();
    }
}

/// <summary>Download-only multi-stream speed test. Through a server (throw-away core), the active connection, or direct.</summary>
public static class SpeedEngine
{
    // Cloudflare rejects bytes > ~25 MB (403), so stream many 25 MB chunks; fallbacks if it is unreachable.
    public static readonly string[] Urls =
    [
        "https://speed.cloudflare.com/__down?bytes=25000000",
        "https://cachefly.cachefly.net/100mb.test",
        "https://proof.ovh.net/files/100Mb.dat",
        "http://speedtest.tele2.net/100MB.zip",
    ];
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);
    public const int Streams = 4;

    public sealed class Result { public double Mbps = -1; public string? Error; public string? Via; public long Bytes; }

    /// <summary>p == null: through the running connection if connected, else direct.</summary>
    public static async Task<Result> RunAsync(ProfileItemModel? p, Action<double, double> onSample, CancellationToken ct, TimeSpan? duration = null)
    {
        ServiceLib.Services.ProcessService? proc = null;
        IWebProxy? proxy = null;
        var r = new Result();
        try
        {
            if (p != null && !(SatraCore.I.Phase == ConnPhase.On && p.IndexId == SatraCore.I.Active?.IndexId))
            {
                var prof = await AppManager.Instance.GetProfileItem(p.IndexId);
                if (prof == null) { r.Error = "server not found"; return r; }
                var item = new ServerTestItem { IndexId = p.IndexId, Address = p.Address, Port = p.Port, ConfigType = p.ConfigType, Profile = prof,
                    CoreType = AppManager.Instance.GetCoreType(prof, p.ConfigType), AllowTest = true };
                proc = await CoreManager.Instance.LoadCoreConfigSpeedtest(item);
                if (proc == null) { r.Error = "could not start core for this server"; return r; }
                await Task.Delay(900, ct);
                proxy = new WebProxy($"socks5://{Global.Loopback}:{item.Port}");
                r.Via = "server";
            }
            else if (SatraCore.I.Phase == ConnPhase.On)
            {
                proxy = new WebProxy($"socks5://{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
                r.Via = "vpn";
            }
            else r.Via = "direct";

            using var http = new HttpClient(new SocketsHttpHandler { Proxy = proxy, UseProxy = proxy != null, ConnectTimeout = TimeSpan.FromSeconds(6),
                MaxConnectionsPerServer = Streams * 2, AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SatraN/1.4");

            // pick the first URL that answers
            string? url = null; string? lastErr = null;
            foreach (var u in Urls)
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probe.CancelAfter(TimeSpan.FromSeconds(6));
                try
                {
                    using var res = await http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, probe.Token);
                    if (res.IsSuccessStatusCode) { url = u; break; }
                    lastErr = $"HTTP {(int)res.StatusCode}";
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { lastErr = "timeout"; }
                catch (HttpRequestException ex) { lastErr = ex.InnerException?.Message ?? ex.Message; }
            }
            if (url == null) { r.Error = (proxy != null ? "no route through the tunnel: " : "no internet: ") + lastErr; return r; }

            long total = 0;
            var sw = Stopwatch.StartNew();
            using var win = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var dur = duration ?? Duration;
            win.CancelAfter(dur);
            async Task Worker()
            {
                var buf = new byte[64 * 1024];
                while (!win.IsCancellationRequested)
                {
                    try
                    {
                        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, win.Token);
                        if (!res.IsSuccessStatusCode) { await Task.Delay(300, win.Token); continue; }
                        await using var s = await res.Content.ReadAsStreamAsync(win.Token);
                        int n;
                        while ((n = await s.ReadAsync(buf, win.Token)) > 0) Interlocked.Add(ref total, n);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception) { try { await Task.Delay(300, win.Token); } catch { return; } }
                }
            }
            var workers = Enumerable.Range(0, Streams).Select(_ => Task.Run(Worker)).ToArray();
            double ewma = 0; long lastB = 0; var lastT = 0.0;
            while (!win.IsCancellationRequested)
            {
                try { await Task.Delay(200, win.Token); } catch (OperationCanceledException) { break; }
                var t = sw.Elapsed.TotalSeconds; var b = Interlocked.Read(ref total);
                var inst = (b - lastB) * 8 / 1e6 / Math.Max(0.05, t - lastT);
                ewma = ewma == 0 ? inst : ewma * 0.65 + inst * 0.35;
                lastB = b; lastT = t;
                onSample(ewma, Math.Min(1, t / dur.TotalSeconds));
            }
            await Task.WhenAll(workers);
            ct.ThrowIfCancellationRequested();
            r.Bytes = Interlocked.Read(ref total);
            if (r.Bytes < 64 * 1024) { r.Error = "connected, but almost no data arrived (blocked or very slow)"; return r; }
            r.Mbps = r.Bytes * 8 / 1e6 / Math.Max(0.5, sw.Elapsed.TotalSeconds);
            if (p != null) ProfileExManager.Instance.SetTestSpeed(p.IndexId, (decimal)Math.Round(r.Mbps / 8, 2));
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { Logging.SaveLog("satra speed", ex); r.Error = ex.Message; return r; }
        finally { if (proc != null) await proc.StopAsync(); }
    }
}
