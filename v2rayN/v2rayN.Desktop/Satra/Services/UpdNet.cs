using System.Net;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Smart downloader shared by app, Xray, sing-box and geo updates (see <see cref="Routes"/>).</summary>
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

    /// <summary>Route used by the last successful request (QA/log): direct, proxy, proxy-temp, hub-ip.</summary>
    public static string LastRoute { get; private set; } = "";

    /// <summary>Test hook: SATRA_TEST_BLOCK_DIRECT=1 makes the direct route fail for this process only.</summary>
    private static readonly bool BlockDirect = Environment.GetEnvironmentVariable("SATRA_TEST_BLOCK_DIRECT") == "1";

    private static HttpClient ViaProxy(int port)
    {
        if (_viaProxy == null || _proxyPort != port) { _viaProxy = Make(new WebProxy($"socks5://127.0.0.1:{port}")); _proxyPort = port; }
        return _viaProxy;
    }

    /// <summary>
    /// The smart route list, tried in order and invisible to the user:
    /// 1) direct, 2) through a working config's local port (the running one, or a throw-away core started just for this),
    /// 3) the Hub's origin IP (only for Hub URLs). GitHub mirrors are the callers' last fallback.
    /// </summary>
    private static async IAsyncEnumerable<(string route, HttpClient c)> Routes(string url, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!BlockDirect) yield return ("direct", Direct);
        if (SatraCore.I.Phase == ConnPhase.On)
            yield return ("proxy", ViaProxy(AppManager.Instance.GetLocalPort(EInboundProtocol.socks)));
        else
        {
            TempProxy? t = null;
            try { t = await TempProxy.AcquireAsync(null, tryOthers: true, ct); } catch (OperationCanceledException) { throw; } catch { }
            if (t != null)
            {
                try { yield return ("proxy-temp", Make(new WebProxy($"socks5://127.0.0.1:{t.Port}"))); }
                finally { await t.DisposeAsync(); }
            }
        }
        if (url.StartsWith(HubDl, StringComparison.Ordinal) || url.Contains("p.iivt.ir", StringComparison.Ordinal)) yield return ("hub-ip", Origin);
    }

    /// <summary>Hub copy of a GitHub release file (same bytes, same checksum), tried before GitHub itself.</summary>
    public static string? HubMirror(string url)
    {
        const string gh = "https://github.com/";
        if (!url.StartsWith(gh, StringComparison.Ordinal) || !url.Contains("/releases/", StringComparison.Ordinal)) return null;
        return HubDl + "mirror/" + url[gh.Length..];
    }

    /// <summary>A 404 is an answer, not a blocked route: no point retrying it over other routes.</summary>
    private static bool Missing(Exception ex) => ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound };

    private static string[] Candidates(string url) => HubMirror(url) is { } m ? [m, url] : [url];

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var u in Candidates(url))
        {
            try { return await GetStringOnceAsync(u, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { last = ex; }
        }
        throw last ?? new HttpRequestException("unreachable");
    }

    private static async Task<string> GetStringOnceAsync(string url, CancellationToken ct)
    {
        Exception? last = null;
        await foreach (var (route, c) in Routes(url, ct))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(route == "direct" ? 12 : 20));
                var r = await c.GetStringAsync(url, cts.Token);
                LastRoute = route;
                Logging.SaveLog($"updnet {route} ok {url}");
                return r;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && !Missing(ex)) { last = ex; Logging.SaveLog($"updnet {route} failed {url}: {ex.Message}"); }
        }
        throw last ?? new HttpRequestException("unreachable");
    }

    /// <summary>Downloads to <paramref name="dest"/> (via .part), verifying SHA-256 when given.</summary>
    public static async Task DownloadAsync(string url, string dest, string? sha256, IProgress<(long got, long? total)>? progress, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var u in Candidates(url))
        {
            try { await DownloadOnceAsync(u, dest, sha256, progress, ct); return; }
            catch (Exception ex) when (!ct.IsCancellationRequested) { last = ex; }
        }
        throw last ?? new HttpRequestException("unreachable");
    }

    private static async Task DownloadOnceAsync(string url, string dest, string? sha256, IProgress<(long got, long? total)>? progress, CancellationToken ct)
    {
        Exception? last = null;
        await foreach (var (route, c) in Routes(url, ct))
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
                LastRoute = route;
                Logging.SaveLog($"updnet {route} ok {url}");
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && !Missing(ex))
            {
                last = ex;
                Logging.SaveLog($"updnet {route} failed {url}: {ex.Message}");
                try { File.Delete(part); } catch { }
            }
        }
        throw last ?? new HttpRequestException("unreachable");
    }
}
