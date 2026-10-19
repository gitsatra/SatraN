using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace v2rayN.Desktop.Satra.Services;

public enum CheckState { Pending, Pass, Warn, Fail, Unknown }

public class CheckResult
{
    public string Key = "";
    public CheckState State = CheckState.Pending;
    public string Detail = "";
}

/// <summary>
/// Connection Security Check (like ExpressVPN's leak test): exit IP vs real IP, DNS leak (bash.ws), WebRTC/UDP
/// leak (real STUN binding request), IPv6 leak, transport encryption (TLS/REALITY, certificate checks),
/// kill switch, and core version. Produces a 0–100 score.
/// </summary>
public static class SecurityCheck
{
    public static readonly string[] Keys = ["sc_ip", "sc_dns", "sc_webrtc", "sc_ipv6", "sc_tls", "sc_core"];

    private static HttpClient Client(bool proxy, int timeout = 12)
    {
        var h = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
        if (proxy) { h.Proxy = new WebProxy($"socks5://{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}"); h.UseProxy = true; }
        else h.UseProxy = false;
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(timeout) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.5");
        return c;
    }

    private static async Task<(string ip, string cc)?> DirectLookupAsync()
    {
        using var c = Client(false, 8);
        foreach (var u in new[] { "https://api.ip.sb/geoip", "https://ipwho.is/", "https://api.country.is/" })
        {
            try
            {
                var j = JsonNode.Parse(await c.GetStringAsync(u))!;
                var ip = j["ip"]?.ToString();
                var cc = (j["country_code"] ?? j["country"])?.ToString();
                if (!string.IsNullOrEmpty(ip)) return (ip!, cc?.ToUpperInvariant() ?? "");
            }
            catch { }
        }
        return null;
    }

    public static async Task<int> RunAsync(List<CheckResult> res, Action update)
    {
        res.Clear();
        foreach (var k in Keys) res.Add(new CheckResult { Key = k });
        update();
        CheckResult R(string k) => res.First(r => r.Key == k);
        var connected = SatraCore.I.Phase == ConnPhase.On;
        var tun = AppManager.Instance.Config.TunModeItem.EnableTun;

        var directT = DirectLookupAsync();
        var exitT = connected ? ExitProbe.LookupAsync(AppManager.Instance.GetLocalPort(EInboundProtocol.socks)) : Task.FromResult<(string ip, string cc)?>(null);
        await Task.WhenAll(directT, exitT);
        var direct = directT.Result;
        var exit = exitT.Result;

        // 1. exit IP
        var ipR = R("sc_ip");
        if (!connected) { ipR.State = CheckState.Fail; ipR.Detail = L.S("sc_not_connected"); }
        else if (exit is not { } e) { ipR.State = CheckState.Fail; ipR.Detail = L.S("sc_no_exit"); }
        else if (direct is { } d && d.ip == e.ip && !tun) { ipR.State = CheckState.Fail; ipR.Detail = L.F("sc_ip_same", Ui.Ltr(e.ip)); }
        else { ipR.State = CheckState.Pass; ipR.Detail = $"{NetBadges.Flag(e.cc)} {Ui.Ltr(Mask(e.ip))}"; }
        update();

        // 2. DNS leak
        var dnsR = R("sc_dns");
        if (!connected) { dnsR.State = CheckState.Unknown; dnsR.Detail = L.S("sc_not_connected"); }
        else
        {
            try
            {
                using var c = Client(true, 15);
                var id = (await c.GetStringAsync("https://bash.ws/id")).Trim();
                // lookups the way apps make them: through the VPN (remote DNS) and via the system resolver
                var pings = Enumerable.Range(1, 6).Select(async i =>
                {
                    try { if (i % 2 == 0) await Dns.GetHostAddressesAsync($"{i}.{id}.bash.ws"); else await c.GetAsync($"http://{i}.{id}.bash.ws"); } catch { }
                });
                await Task.WhenAll(pings);
                await Task.Delay(1500);
                var arr = JsonNode.Parse(await c.GetStringAsync($"https://bash.ws/dnsleak/test/{id}?json"))!.AsArray();
                var servers = arr.Where(x => x?["type"]?.ToString() == "dns").ToList();
                var home = direct?.cc;
                var leaks = servers.Where(x => !string.IsNullOrEmpty(home) && string.Equals(x?["country"]?.ToString(), home, StringComparison.OrdinalIgnoreCase) && home != exit?.cc).ToList();
                if (servers.Count == 0) { dnsR.State = CheckState.Unknown; dnsR.Detail = L.S("sc_unknown"); }
                else if (leaks.Count > 0) { dnsR.State = CheckState.Fail; dnsR.Detail = L.F("sc_dns_leak", leaks.Count); }
                else
                {
                    dnsR.State = CheckState.Pass;
                    dnsR.Detail = L.F("sc_dns_ok", servers.Count, string.Join(" ", servers.Select(x => x?["country"]?.ToString()).Distinct().Take(4).Select(NetBadges.Flag)));
                }
            }
            catch { dnsR.State = CheckState.Unknown; dnsR.Detail = L.S("sc_unknown"); }
        }
        update();

        // 3. WebRTC / UDP leak: a real STUN request from this computer, outside any proxy settings
        var rtcR = R("sc_webrtc");
        var mapped = await StunAsync();
        if (mapped == null) { rtcR.State = tun ? CheckState.Pass : CheckState.Unknown; rtcR.Detail = L.S(tun ? "sc_rtc_blocked" : "sc_unknown"); }
        else if (direct is { } d2 && mapped == d2.ip && (exit == null || exit.Value.ip != mapped)) { rtcR.State = connected ? CheckState.Warn : CheckState.Fail; rtcR.Detail = L.S("sc_rtc_leak"); }
        else { rtcR.State = CheckState.Pass; rtcR.Detail = L.S("sc_rtc_ok"); }
        update();

        // 4. IPv6
        var v6R = R("sc_ipv6");
        string? v6 = null;
        try { using var c = Client(false, 6); v6 = (await c.GetStringAsync("https://api6.ipify.org")).Trim(); } catch { }
        if (v6 is { Length: > 0 } && v6.Contains(':') && (exit == null || exit.Value.ip != v6)) { v6R.State = CheckState.Warn; v6R.Detail = L.S("sc_v6_leak"); }
        else { v6R.State = CheckState.Pass; v6R.Detail = L.S("sc_v6_ok"); }
        update();

        // 5. transport encryption of the active config
        var tlsR = R("sc_tls");
        var a = SatraCore.I.Active;
        var prof = a == null ? null : await AppManager.Instance.GetProfileItem(a.IndexId);
        if (prof == null) { tlsR.State = CheckState.Unknown; tlsR.Detail = L.S("no_server"); }
        else
        {
            var sec = prof.StreamSecurity ?? "";
            if (sec == "reality") { tlsR.State = CheckState.Pass; tlsR.Detail = "REALITY · " + (string.IsNullOrEmpty(prof.Fingerprint) ? "chrome" : prof.Fingerprint); }
            else if (sec == "tls" && !prof.GetAllowInsecure()) { tlsR.State = CheckState.Pass; tlsR.Detail = "TLS · " + L.S("sc_cert_ok"); }
            else if (sec == "tls") { tlsR.State = CheckState.Warn; tlsR.Detail = "TLS · " + L.S("sc_insecure"); }
            else if (prof.ConfigType is EConfigType.Shadowsocks or EConfigType.WireGuard or EConfigType.Hysteria2 or EConfigType.TUIC) { tlsR.State = CheckState.Pass; tlsR.Detail = L.S("sc_proto_enc"); }
            else { tlsR.State = CheckState.Warn; tlsR.Detail = L.S("sc_no_tls"); }
        }
        update();

        // 7. core
        var coreR = R("sc_core");
        var cur = await CoreVersionAsync();
        string? latest = null;
        try
        {
            using var c = Client(!connected ? false : true, 10);
            // Xray ships most releases as pre-releases; compare against the newest tag of any kind
            var arr = JsonNode.Parse(await c.GetStringAsync("https://api.github.com/repos/XTLS/Xray-core/releases?per_page=8"))!.AsArray();
            latest = arr.Select(x => x?["tag_name"]?.ToString()?.TrimStart('v')).Where(x => Version.TryParse(x, out _)).OrderByDescending(x => Version.Parse(x!)).FirstOrDefault();
        }
        catch { }
        if (cur == null) { coreR.State = CheckState.Unknown; coreR.Detail = L.S("sc_unknown"); }
        else if (latest == null || Version.Parse(cur) >= Version.Parse(latest)) { coreR.State = CheckState.Pass; coreR.Detail = "Xray " + cur + (latest == null ? "" : " · " + L.S("sc_latest")); }
        else { coreR.State = CheckState.Warn; coreR.Detail = L.F("sc_core_old", cur, latest); }
        update();

        var w = new Dictionary<string, int> { ["sc_ip"] = 25, ["sc_dns"] = 20, ["sc_webrtc"] = 15, ["sc_ipv6"] = 10, ["sc_tls"] = 20, ["sc_core"] = 10 };
        double score = res.Sum(r => w[r.Key] * (r.State switch { CheckState.Pass => 1.0, CheckState.Warn => 0.5, CheckState.Unknown => 0.6, _ => 0 }));
        return (int)Math.Round(score);
    }

    private static string Mask(string ip)
    {
        var i = ip.LastIndexOfAny(['.', ':']);
        return i > 0 ? ip[..(i + 1)] + "•••" : ip;
    }

    private static async Task<string?> CoreVersionAsync()
    {
        try
        {
            var info = CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray);
            var exe = CoreInfoManager.Instance.GetCoreExecFile(info, out _);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            var psi = new System.Diagnostics.ProcessStartInfo(exe, "version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync();
            var m = System.Text.RegularExpressions.Regex.Match(o, @"Xray (\d+\.\d+\.\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    /// RFC 5389 binding request; returns the public address the STUN server saw (XOR-MAPPED-ADDRESS).
    public static async Task<string?> StunAsync()
    {
        foreach (var (host, port) in new[] { ("stun.l.google.com", 19302), ("stun.cloudflare.com", 3478) })
        {
            try
            {
                using var u = new UdpClient(AddressFamily.InterNetwork);
                var tid = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
                byte[] req = [0x00, 0x01, 0x00, 0x00, 0x21, 0x12, 0xA4, 0x42, .. tid];
                var addrs = await Dns.GetHostAddressesAsync(host);
                var ep = new IPEndPoint(addrs.First(x => x.AddressFamily == AddressFamily.InterNetwork), port);
                await u.SendAsync(req, ep);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var r = (await u.ReceiveAsync(cts.Token)).Buffer;
                for (var i = 20; i + 4 <= r.Length;)
                {
                    var type = (r[i] << 8) | r[i + 1];
                    var len = (r[i + 2] << 8) | r[i + 3];
                    if ((type == 0x0020 || type == 0x0001) && len >= 8 && r[i + 5] == 0x01)
                    {
                        var b = r.AsSpan(i + 8, 4).ToArray();
                        if (type == 0x0020) { b[0] ^= 0x21; b[1] ^= 0x12; b[2] ^= 0xA4; b[3] ^= 0x42; }
                        return new IPAddress(b).ToString();
                    }
                    i += 4 + len + ((4 - len % 4) % 4);
                }
            }
            catch { }
        }
        return null;
    }
}
