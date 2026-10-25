using System.Net;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>HTTP for updates: try direct first, then through our own local proxy when connected (helps where the host is filtered).</summary>
public static class UpdNet
{
    public const string HubDl = "https://p.iivt.ir:2097/satra/dl/";
    /// <summary>Origin of the Hub, used when the CDN front is blocked on the user's network (same TLS cert/SNI).</summary>
    public const string HubOrigin = "107.189.24.195";
    private static readonly HttpClient Direct = Make(null);
    private static readonly HttpClient Origin = Make(null, true);
    private static HttpClient? _viaProxy;
    private static int _proxyPort;

    private static HttpClient Make(IWebProxy? proxy, bool origin = false)
    {
        var h = new SocketsHttpHandler { Proxy = proxy, UseProxy = proxy != null, AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(7) };
        if (origin)
            h.ConnectCallback = async (ctx, ct) =>
            {
                var host = ctx.DnsEndPoint.Host == "p.iivt.ir" ? HubOrigin : ctx.DnsEndPoint.Host;
                var s = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
                try { await s.ConnectAsync(host, ctx.DnsEndPoint.Port, ct); return new System.Net.Sockets.NetworkStream(s, true); }
                catch { s.Dispose(); throw; }
            };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Identity.App);
        return c;
    }

    private static IEnumerable<HttpClient> Clients()
    {
        if (SatraCore.I.Phase == ConnPhase.On)
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (_viaProxy == null || _proxyPort != port)
            {
                _viaProxy = Make(new WebProxy($"socks5://127.0.0.1:{port}"));
                _proxyPort = port;
            }
            yield return _viaProxy;
        }
        yield return Direct;
        yield return Origin;
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var c in Clients())
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                return await c.GetStringAsync(url, cts.Token);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { last = ex; }
        }
        throw last ?? new HttpRequestException("unreachable");
    }

    /// <summary>Downloads to <paramref name="dest"/> (via .part), verifying SHA-256 when given.</summary>
    public static async Task DownloadAsync(string url, string dest, string? sha256, IProgress<(long got, long? total)>? progress, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var c in Clients())
        {
            var part = dest + ".part";
            try
            {
                // stall watchdog: a filtered route can keep a socket open with no data forever; give up after 25 s idle
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromSeconds(25));
                using (var resp = await c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token))
                {
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength;
                    await using var input = await resp.Content.ReadAsStreamAsync(stall.Token);
                    await using var output = File.Create(part);
                    var buf = new byte[1 << 16];
                    long got = 0;
                    int n;
                    while ((n = await input.ReadAsync(buf, stall.Token)) > 0)
                    {
                        stall.CancelAfter(TimeSpan.FromSeconds(25));
                        await output.WriteAsync(buf.AsMemory(0, n), ct);
                        got += n;
                        progress?.Report((got, total));
                    }
                }
                if (!string.IsNullOrEmpty(sha256))
                {
                    await using var f = File.OpenRead(part);
                    var h = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(f, ct));
                    if (!h.Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("checksum mismatch");
                }
                File.Move(part, dest, true);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                try { File.Delete(part); } catch { }
            }
        }
        throw last ?? new HttpRequestException("unreachable");
    }
}
