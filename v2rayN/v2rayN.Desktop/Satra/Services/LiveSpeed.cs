using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Download-only speed test per config through a throw-away core instance, reporting live Mbps.</summary>
public static class LiveSpeed
{
    public static readonly string[] Urls = ["https://speed.cloudflare.com/__down?bytes=50000000", "https://cachefly.cachefly.net/100mb.test"];
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);

    /// <param name="onSample">(Mbps, progress 0..1)</param>
    /// <returns>Average Mbps over the measured window, or -1 on failure.</returns>
    public static async Task<double> RunAsync(ProfileItemModel p, Action<double, double> onSample, CancellationToken ct)
    {
        var prof = await AppManager.Instance.GetProfileItem(p.IndexId);
        if (prof == null) return -1;
        var item = new ServerTestItem { IndexId = p.IndexId, Address = p.Address, Port = p.Port, ConfigType = p.ConfigType, Profile = prof,
            CoreType = AppManager.Instance.GetCoreType(prof, p.ConfigType), AllowTest = true };
        var proc = await CoreManager.Instance.LoadCoreConfigSpeedtest(item);
        if (proc == null) return -1;
        try
        {
            await Task.Delay(900, ct);
            using var handler = new SocketsHttpHandler { Proxy = new WebProxy($"socks5://127.0.0.1:{item.Port}"), UseProxy = true };
            using var http = new HttpClient(handler) { Timeout = Duration + TimeSpan.FromSeconds(8) };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            HttpResponseMessage? res = null;
            foreach (var u in Urls)
            {
                try { res = await http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, cts.Token); if (res.IsSuccessStatusCode) break; res.Dispose(); res = null; }
                catch (HttpRequestException ex) { Logging.SaveLog("satra speed " + u, ex); }
            }
            if (res == null) return -1;
            using var _res = res;
            await using var s = await res.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[64 * 1024];
            long total = 0, lastBytes = 0;
            var sw = Stopwatch.StartNew();
            var lastT = 0.0;
            double ewma = 0;
            cts.CancelAfter(Duration);
            try
            {
                int n;
                while ((n = await s.ReadAsync(buf, cts.Token)) > 0)
                {
                    total += n;
                    var t = sw.Elapsed.TotalSeconds;
                    if (t - lastT >= 0.2)
                    {
                        var inst = (total - lastBytes) * 8 / 1e6 / (t - lastT);
                        ewma = ewma == 0 ? inst : ewma * 0.6 + inst * 0.4;
                        lastBytes = total; lastT = t;
                        onSample(ewma, Math.Min(1, t / Duration.TotalSeconds));
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* time window over */ }
            var secs = Math.Max(0.5, sw.Elapsed.TotalSeconds);
            var avg = total * 8 / 1e6 / secs;
            ProfileExManager.Instance.SetTestSpeed(p.IndexId, (decimal)Math.Round(avg / 8, 2)); // v2rayN stores MB/s
            return avg;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logging.SaveLog("satra speed", ex);
            return -1;
        }
        finally { await proc.StopAsync(); }
    }
}
