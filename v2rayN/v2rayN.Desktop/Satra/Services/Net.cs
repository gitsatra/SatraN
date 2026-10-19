using System.Net;
using System.Net.Http;

namespace v2rayN.Desktop.Satra.Services;

public static class Net
{
    private static HttpClient Make(bool viaProxy)
    {
        var h = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(8) };
        if (viaProxy)
        {
            h.Proxy = new WebProxy($"socks5://{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
            h.UseProxy = true;
        }
        else
        {
            h.UseProxy = false;
        }
        return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static readonly HttpClient Direct = Make(false);
    private static HttpClient? _proxy;
    private static HttpClient Proxy => _proxy ??= Make(true);

    /// <summary>Direct first, then through the local core (mirrors v2rayN's subscription download).</summary>
    public static async Task<HttpResponseMessage?> SendAsync(Func<HttpRequestMessage> make)
    {
        foreach (var c in new[] { Direct, Proxy })
        {
            try
            {
                var r = await c.SendAsync(make());
                return r;
            }
            catch (Exception ex)
            {
                Logging.SaveLog("satra net", ex);
            }
        }
        return null;
    }
}
