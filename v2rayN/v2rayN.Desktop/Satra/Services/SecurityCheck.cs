using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ServiceLib.Satra;

namespace v2rayN.Desktop.Satra.Services;

public enum CheckState { Pending, Running, Pass, Warn, Fail, Unknown }

public class CheckResult
{
    public string Key = "";
    public string Cat = "";
    public int Max;
    public CheckState State = CheckState.Pending;
    public string Detail = "";
    public bool Scored => Max > 0;
    public int Points => State switch { CheckState.Pass => Max, CheckState.Warn => Max / 2, _ => 0 };
    public bool Done => State is CheckState.Pass or CheckState.Warn or CheckState.Fail or CheckState.Unknown;
}

/// <summary>
/// Connection Security Check, run only on demand, on the selected (or running) config. Works without connecting:
/// when SatraN is off a throw-away core serves just this scan (<see cref="TempProxy"/>).
/// Four sections of real checks; kill switch and the Tor section are informational (not scored).
/// </summary>
public static class SecurityCheck
{
    public static readonly string[] Cats = ["leak", "tls", "sys", "tor"];

    /// (key, section, max points) — the original six keep their weights.
    public static readonly (string Key, string Cat, int Max)[] Plan =
    [
        ("sc_ip", "leak", 25), ("sc_dns", "leak", 20), ("sc_webrtc", "leak", 15), ("sc_ipv6", "leak", 10),
        ("sc_exit2", "leak", 4), ("sc_dnsloc", "leak", 3), ("sc_headers", "leak", 4), ("sc_poison", "leak", 3), ("sc_lan", "leak", 4), ("sc_geo", "leak", 2),
        ("sc_tls", "tls", 20), ("sc_cert", "tls", 5), ("sc_fp", "tls", 4), ("sc_sni", "tls", 3), ("sc_cipher", "tls", 5),
        ("sc_flow", "tls", 2), ("sc_handshake", "tls", 4), ("sc_port", "tls", 2), ("sc_alpn", "tls", 1), ("sc_transport", "tls", 3),
        ("sc_core", "sys", 10), ("sc_singbox", "sys", 3), ("sc_app", "sys", 4), ("sc_geodata", "sys", 3), ("sc_api", "sys", 3),
        ("sc_perm", "sys", 3), ("sc_root", "sys", 2), ("sc_autoupd", "sys", 2), ("sc_kill", "sys", 0), ("sc_cover", "sys", 0),
        ("sc_tor_exit", "tor", 0), ("sc_tor_relay", "tor", 0), ("sc_tor_api", "tor", 0), ("sc_tor_reach", "tor", 0), ("sc_tor_local", "tor", 0), ("sc_tor_list", "tor", 0),
    ];

    public static List<CheckResult> Fresh() => Plan.Select(p => new CheckResult { Key = p.Key, Cat = p.Cat, Max = p.Max }).ToList();

    public static int Score(IEnumerable<CheckResult> res) =>
        ConfigAudit.Score(res.Select(r => (r.Max, r.Done ? (AuditState?)ToAudit(r.State) : null)));

    private static AuditState ToAudit(CheckState s) => s switch { CheckState.Pass => AuditState.Pass, CheckState.Warn => AuditState.Warn, CheckState.Fail => AuditState.Fail, _ => AuditState.Unknown };
    private static CheckState FromAudit(AuditState s) => s switch { AuditState.Pass => CheckState.Pass, AuditState.Warn => CheckState.Warn, AuditState.Fail => CheckState.Fail, _ => CheckState.Unknown };

    private static HttpClient Client(int? port, int timeout = 12)
    {
        var h = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
        if (port is int p) { h.Proxy = new WebProxy($"socks5://{Global.Loopback}:{p}"); h.UseProxy = true; }
        else h.UseProxy = false;
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(timeout) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.5");
        return c;
    }

    private static async Task<(string ip, string cc)?> LookupAsync(int? port, params string[] urls)
    {
        using var c = Client(port, 9);
        foreach (var u in urls)
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

    public sealed record Ctx(ProfileItemModel? Config, bool Temporary, bool Tun);

    /// <summary>Runs every check, updating <paramref name="res"/> in place (rows never rebuild). Returns the score.</summary>
    public static async Task<(int score, Ctx ctx)> RunAsync(List<CheckResult> res, Action update, ProfileItemModel? want, CancellationToken ct = default)
    {
        foreach (var r in res) { r.State = CheckState.Pending; r.Detail = ""; }
        update();
        CheckResult R(string k) => res.First(r => r.Key == k);
        async Task Step(string k, Func<CheckResult, Task> body)
        {
            var r = R(k);
            r.State = CheckState.Running; update();
            var t0 = Environment.TickCount64;
            try { await body(r); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { r.State = CheckState.Unknown; r.Detail = L.S("sc_unknown"); Logging.SaveLog("security " + k, ex); }
            if (r.State == CheckState.Running) r.State = CheckState.Unknown;
            // a calm, readable rhythm: each item ticks off on its own beat
            var left = 260 - (Environment.TickCount64 - t0);
            if (left > 0) await Task.Delay((int)left, ct);
            update();
        }
        void Set(CheckResult r, CheckState s, string d) { r.State = s; r.Detail = d; }

        var tun = AppManager.Instance.Config.TunModeItem.EnableTun && SatraCore.I.Phase == ConnPhase.On;
        await using var proxy = await TempProxy.AcquireAsync(want, false, ct);
        var port = proxy?.Port;
        var ctx = new Ctx(proxy?.Profile ?? want ?? SatraCore.I.Active, proxy?.Temporary ?? false, tun);
        var prof = ctx.Config == null ? null : await AppManager.Instance.GetProfileItem(ctx.Config.IndexId);

        var directT = LookupAsync(null, "https://api.ip.sb/geoip", "https://ipwho.is/", "https://api.country.is/");
        var exitT = port is int pp ? ExitProbe.LookupAsync(pp, ct) : Task.FromResult<(string ip, string cc)?>(null);
        (string ip, string cc)? direct = null, exit = null;

        // ───────── Leaks & identity ─────────
        await Step("sc_ip", async r =>
        {
            await Task.WhenAll(directT, exitT);
            direct = directT.Result; exit = exitT.Result;
            if (port == null) Set(r, CheckState.Fail, L.S("sc_no_exit"));
            else if (exit is not { } e) Set(r, CheckState.Fail, L.S("sc_no_exit"));
            else if (direct is { } d && d.ip == e.ip) Set(r, CheckState.Fail, L.F("sc_ip_same", Ui.Ltr(e.ip)));
            else Set(r, CheckState.Pass, $"{NetBadges.Flag(e.cc)} {Ui.Ltr(Mask(e.ip))}");
        });

        await Step("sc_dns", async r =>
        {
            if (port == null) { Set(r, CheckState.Unknown, L.S("sc_no_exit")); return; }
            using var c = Client(port, 12);
            var id = (await c.GetStringAsync("https://bash.ws/id", ct)).Trim();
            var pings = Enumerable.Range(1, 6).Select(async i =>
            {
                // via the proxy the hostname is resolved remotely (what apps using the port get); in TUN also via the system resolver
                try { if (tun && i % 2 == 0) await Dns.GetHostAddressesAsync($"{i}.{id}.bash.ws", ct); else await c.GetAsync($"http://{i}.{id}.bash.ws", ct); } catch { }
            });
            await Task.WhenAll(pings);
            await Task.Delay(450, ct);
            var arr = JsonNode.Parse(await c.GetStringAsync($"https://bash.ws/dnsleak/test/{id}?json", ct))!.AsArray();
            var servers = arr.Where(x => x?["type"]?.ToString() == "dns").ToList();
            _dnsCountries = servers.Select(x => x?["country"]?.ToString() ?? "").Where(x => x.Length > 0).Distinct().ToList();
            var home = direct?.cc;
            var leaks = servers.Where(x => !string.IsNullOrEmpty(home) && string.Equals(x?["country"]?.ToString(), home, StringComparison.OrdinalIgnoreCase) && home != exit?.cc).ToList();
            if (servers.Count == 0) Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (leaks.Count > 0) Set(r, CheckState.Fail, L.F("sc_dns_leak", leaks.Count));
            else Set(r, CheckState.Pass, L.F("sc_dns_ok", servers.Count, string.Join(" ", _dnsCountries.Take(4).Select(NetBadges.Flag))));
        });

        await Step("sc_webrtc", async r =>
        {
            var mapped = await StunAsync();
            if (mapped == null) Set(r, tun ? CheckState.Pass : CheckState.Unknown, L.S(tun ? "sc_rtc_blocked" : "sc_unknown"));
            else if (direct is { } d2 && mapped == d2.ip && (exit == null || exit.Value.ip != mapped)) Set(r, tun ? CheckState.Fail : CheckState.Warn, L.S(tun ? "sc_rtc_leak" : "sc_rtc_ports"));
            else Set(r, CheckState.Pass, L.S("sc_rtc_ok"));
        });

        await Step("sc_ipv6", async r =>
        {
            string? v6 = null;
            try { using var c = Client(null, 6); v6 = (await c.GetStringAsync("https://api6.ipify.org", ct)).Trim(); } catch { }
            if (v6 is { Length: > 0 } && v6.Contains(':') && (exit == null || exit.Value.ip != v6)) Set(r, tun ? CheckState.Fail : CheckState.Warn, L.S("sc_v6_leak"));
            else Set(r, CheckState.Pass, L.S("sc_v6_ok"));
        });

        await Step("sc_exit2", async r =>
        {
            if (port == null || exit == null) { Set(r, CheckState.Unknown, L.S("sc_no_exit")); return; }
            var b = await LookupAsync(port, "https://ipwho.is/", "https://api.country.is/");
            if (b == null) Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (b.Value.ip == exit.Value.ip) Set(r, CheckState.Pass, L.S("sc_exit2_ok"));
            else Set(r, CheckState.Warn, L.F("sc_exit2_diff", Ui.Ltr(Mask(b.Value.ip))));
        });

        await Step("sc_dnsloc", r =>
        {
            if (_dnsCountries.Count == 0 || exit == null) Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (direct?.cc is { Length: > 0 } h && _dnsCountries.Contains(h, StringComparer.OrdinalIgnoreCase) && h != exit.Value.cc) Set(r, CheckState.Fail, L.F("sc_dnsloc_home", NetBadges.Flag(h)));
            else Set(r, CheckState.Pass, string.Join(" ", _dnsCountries.Take(4).Select(NetBadges.Flag)));
            return Task.CompletedTask;
        });

        await Step("sc_headers", async r =>
        {
            if (port == null) { Set(r, CheckState.Unknown, L.S("sc_no_exit")); return; }
            using var c = Client(port, 10);
            var j = JsonNode.Parse(await c.GetStringAsync("https://httpbin.org/headers", ct))!["headers"]!.AsObject();
            var bad = j.Where(kv => kv.Key is "X-Forwarded-For" or "Via" or "X-Real-Ip" or "Forwarded" or "X-Client-Ip").ToList();
            var leaks = bad.Any(kv => direct != null && (kv.Value?.ToString() ?? "").Contains(direct.Value.ip));
            if (leaks) Set(r, CheckState.Fail, L.S("sc_headers_leak"));
            else if (bad.Count > 0) Set(r, CheckState.Warn, string.Join(", ", bad.Select(kv => kv.Key)));
            else Set(r, CheckState.Pass, L.S("sc_headers_ok"));
        });

        await Step("sc_poison", async r =>
        {
            var hosts = new[] { "www.youtube.com", "twitter.com", "www.instagram.com" };
            var bad = new List<string>();
            var ok = 0;
            foreach (var h in hosts)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(4000);
                    var a = await Dns.GetHostAddressesAsync(h, AddressFamily.InterNetwork, cts.Token);
                    ok++;
                    if (a.Any(ConfigAudit.IsPoisoned)) bad.Add(h);
                }
                catch { }
            }
            if (ok == 0) Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (bad.Count > 0) Set(r, tun ? CheckState.Fail : CheckState.Warn, L.F("sc_poison_bad", bad.Count));
            else Set(r, CheckState.Pass, L.S("sc_poison_ok"));
        });

        await Step("sc_lan", r =>
        {
            var ib = AppManager.Instance.Config.Inbound.FirstOrDefault();
            if (ib == null) Set(r, CheckState.Unknown, "");
            else if (!ib.AllowLANConn) Set(r, CheckState.Pass, L.S("sc_lan_local"));
            else if (!string.IsNullOrEmpty(ib.User) && !string.IsNullOrEmpty(ib.Pass)) Set(r, CheckState.Pass, L.S("sc_lan_auth"));
            else Set(r, CheckState.Warn, L.S("sc_lan_open"));
            return Task.CompletedTask;
        });

        await Step("sc_geo", r =>
        {
            if (exit == null || direct == null) Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (exit.Value.cc == direct.Value.cc) Set(r, CheckState.Warn, L.F("sc_geo_same", NetBadges.Flag(exit.Value.cc)));
            else Set(r, CheckState.Pass, $"{NetBadges.Flag(direct.Value.cc)} → {NetBadges.Flag(exit.Value.cc)}");
            return Task.CompletedTask;
        });

        // ───────── Encryption ─────────
        var audit = prof == null ? [] : ConfigAudit.Run(prof);
        await Step("sc_tls", r =>
        {
            if (prof == null) { Set(r, CheckState.Unknown, L.S("no_server")); return Task.CompletedTask; }
            var sec = prof.StreamSecurity ?? "";
            if (sec == "reality") Set(r, CheckState.Pass, "REALITY · " + (string.IsNullOrEmpty(prof.Fingerprint) ? "chrome" : prof.Fingerprint));
            else if (sec == "tls" && !prof.GetAllowInsecure()) Set(r, CheckState.Pass, "TLS · " + L.S("sc_cert_ok"));
            else if (sec == "tls") Set(r, CheckState.Warn, "TLS · " + L.S("sc_insecure"));
            else if (prof.ConfigType is EConfigType.Shadowsocks or EConfigType.WireGuard or EConfigType.Hysteria2 or EConfigType.TUIC) Set(r, CheckState.Pass, L.S("sc_proto_enc"));
            else Set(r, CheckState.Warn, L.S("sc_no_tls"));
            return Task.CompletedTask;
        });
        foreach (var k in new[] { "sc_cert", "sc_fp", "sc_sni", "sc_cipher", "sc_flow" })
            await Step(k, r => { if (audit.FirstOrDefault(a => a.Key == k) is { } a) Set(r, FromAudit(a.State), a.Detail); else Set(r, CheckState.Unknown, L.S("no_server")); return Task.CompletedTask; });
        await Step("sc_handshake", async r =>
        {
            if (prof == null || !ConfigAudit.IsTls(prof)) { Set(r, CheckState.Pass, "n/a"); return; }
            var (st, d) = await HandshakeAsync(prof, port, ct);
            Set(r, st, d);
        });
        foreach (var k in new[] { "sc_port", "sc_alpn", "sc_transport" })
            await Step(k, r => { if (audit.FirstOrDefault(a => a.Key == k) is { } a) Set(r, FromAudit(a.State), a.Detail); else Set(r, CheckState.Unknown, L.S("no_server")); return Task.CompletedTask; });

        // ───────── App & device ─────────
        await Step("sc_core", async r =>
        {
            var cur = await AssetUpdates.CoreVersionAsync(AssetUpdates.Kind.Xray);
            var latest = await LatestTag("XTLS/Xray-core", port, true);
            if (cur == "?") Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (latest == null || !SatraAppUpdate.IsNewer(latest, cur)) Set(r, CheckState.Pass, "Xray " + cur + (latest == null ? "" : " · " + L.S("sc_latest")));
            else Set(r, CheckState.Warn, L.F("sc_core_old", cur, latest));
        });
        await Step("sc_singbox", async r =>
        {
            var cur = await AssetUpdates.CoreVersionAsync(AssetUpdates.Kind.SingBox);
            var latest = await LatestTag("SagerNet/sing-box", port, false);
            if (cur == "?") Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else if (latest == null || !SatraAppUpdate.IsNewer(latest, cur)) Set(r, CheckState.Pass, "sing-box " + cur + (latest == null ? "" : " · " + L.S("sc_latest")));
            else Set(r, CheckState.Warn, L.F("sc_core_old", cur, latest));
        });
        await Step("sc_app", async r =>
        {
            var c = await SatraAppUpdate.CheckAsync(ct);
            if (c.LatestTag == null) Set(r, CheckState.Unknown, "SatraN " + Identity.Version);
            else if (c.HasUpdate) Set(r, CheckState.Warn, L.F("sc_core_old", Identity.Version, c.Version ?? c.LatestTag));
            else Set(r, CheckState.Pass, "SatraN " + Identity.Version + " · " + L.S("sc_latest"));
        });
        await Step("sc_geodata", r =>
        {
            var f = Utils.GetBinPath("geosite.dat");
            if (!File.Exists(f)) { Set(r, CheckState.Warn, L.S("sc_geodata_missing")); return Task.CompletedTask; }
            var age = (DateTime.Now - File.GetLastWriteTime(f)).TotalDays;
            Set(r, age <= 30 ? CheckState.Pass : age <= 90 ? CheckState.Warn : CheckState.Fail, L.F("sc_days", (int)age));
            return Task.CompletedTask;
        });
        await Step("sc_api", async r =>
        {
            var open = new List<int>();
            foreach (var proto in new[] { EInboundProtocol.api, EInboundProtocol.api2 })
            {
                var p = AppManager.Instance.GetLocalPort(proto);
                foreach (var ip in LanAddresses())
                {
                    try
                    {
                        using var s = new TcpClient();
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(700);
                        await s.ConnectAsync(ip, p, cts.Token);
                        open.Add(p);
                    }
                    catch { }
                }
            }
            Set(r, open.Count == 0 ? CheckState.Pass : CheckState.Fail, open.Count == 0 ? L.S("sc_api_ok") : L.F("sc_api_open", string.Join(",", open.Distinct())));
        });
        await Step("sc_perm", r =>
        {
            if (OperatingSystem.IsWindows()) { Set(r, CheckState.Pass, L.S("sc_perm_ok")); return Task.CompletedTask; }
            var f = Utils.GetConfigPath("guiNDB.db");
            if (!File.Exists(f)) f = Utils.GetConfigPath("guiNConfig.json");
            if (!File.Exists(f)) { Set(r, CheckState.Unknown, ""); return Task.CompletedTask; }
            var m = File.GetUnixFileMode(f);
            var world = (m & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0;
            Set(r, world ? CheckState.Warn : CheckState.Pass, world ? L.S("sc_perm_world") : L.S("sc_perm_ok"));
            return Task.CompletedTask;
        });
        await Step("sc_root", r =>
        {
            var root = OperatingSystem.IsWindows() ? Utils.IsAdministrator() : Environment.UserName == "root";
            Set(r, root ? CheckState.Warn : CheckState.Pass, L.S(root ? "sc_root_yes" : "sc_root_no"));
            return Task.CompletedTask;
        });
        await Step("sc_autoupd", r =>
        {
            var on = SatraState.I.AutoUpdate && SatraState.I.AppUpdateMode > 0;
            Set(r, on ? CheckState.Pass : CheckState.Warn, L.S(on ? "sc_autoupd_on" : "sc_autoupd_off"));
            return Task.CompletedTask;
        });
        await Step("sc_kill", r =>
        {
            var on = SatraState.I.KillSwitch;
            Set(r, on ? CheckState.Pass : CheckState.Unknown, L.S(on ? "sc_kill_on" : "sc_kill_off"));
            return Task.CompletedTask;
        });
        await Step("sc_cover", r =>
        {
            Set(r, tun ? CheckState.Pass : CheckState.Unknown, L.S(tun ? "sc_cover_tun" : ctx.Temporary ? "sc_cover_temp" : "sc_cover_ports"));
            return Task.CompletedTask;
        });

        // ───────── Tor ─────────
        var tor = await TorExits.GetAsync(port, ct);
        var isExit = exit != null && tor.Set.Contains(exit.Value.ip);
        await Step("sc_tor_exit", r =>
        {
            if (exit == null || tor.Set.Count == 0) Set(r, CheckState.Unknown, L.S("sc_unknown"));
            else Set(r, isExit ? CheckState.Warn : CheckState.Pass, L.S(isExit ? "sc_tor_exit_yes" : "sc_tor_exit_no"));
            return Task.CompletedTask;
        });
        await Step("sc_tor_relay", async r =>
        {
            if (exit == null) { Set(r, CheckState.Unknown, L.S("sc_unknown")); return; }
            using var c = Client(port, 12);
            var j = JsonNode.Parse(await c.GetStringAsync($"https://onionoo.torproject.org/details?search={exit.Value.ip}&fields=nickname,flags,running", ct))!;
            var relays = j["relays"]?.AsArray() ?? [];
            if (relays.Count == 0) Set(r, CheckState.Pass, L.S("sc_tor_relay_no"));
            else
            {
                var n = relays[0]!;
                var flags = string.Join(" ", (n["flags"]?.AsArray() ?? []).Select(f => f?.ToString()).Take(5));
                Set(r, CheckState.Warn, $"{n["nickname"]} · {flags}");
            }
        });
        await Step("sc_tor_api", async r =>
        {
            if (port == null) { Set(r, CheckState.Unknown, L.S("sc_no_exit")); return; }
            using var c = Client(port, 12);
            var j = JsonNode.Parse(await c.GetStringAsync("https://check.torproject.org/api/ip", ct))!;
            var isTor = j["IsTor"]?.GetValue<bool>() == true;
            Set(r, isTor ? CheckState.Warn : CheckState.Pass, L.S(isTor ? "sc_tor_api_yes" : "sc_tor_api_no"));
        });
        await Step("sc_tor_reach", async r =>
        {
            if (port == null) { Set(r, CheckState.Unknown, L.S("sc_no_exit")); return; }
            using var c = Client(port, 10);
            using var resp = await c.GetAsync("https://www.torproject.org/", HttpCompletionOption.ResponseHeadersRead, ct);
            Set(r, resp.IsSuccessStatusCode ? CheckState.Pass : CheckState.Unknown, resp.IsSuccessStatusCode ? L.S("sc_tor_reach_ok") : ((int)resp.StatusCode).ToString());
        });
        await Step("sc_tor_local", async r =>
        {
            var found = new List<int>();
            foreach (var p in new[] { 9050, 9150 })
            {
                try { using var s = new TcpClient(); using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(500); await s.ConnectAsync(IPAddress.Loopback, p, cts.Token); found.Add(p); } catch { }
            }
            Set(r, found.Count > 0 ? CheckState.Pass : CheckState.Unknown, found.Count > 0 ? L.F("sc_tor_local_yes", string.Join(",", found)) : L.S("sc_tor_local_no"));
        });
        await Step("sc_tor_list", r =>
        {
            Set(r, tor.Set.Count > 0 ? CheckState.Pass : CheckState.Unknown, tor.Set.Count > 0 ? L.F("sc_tor_list_fmt", tor.Set.Count, Math.Max(0, (int)(DateTime.UtcNow - tor.At).TotalHours)) : L.S("sc_unknown"));
            return Task.CompletedTask;
        });

        return (Score(res), ctx);
    }

    private static List<string> _dnsCountries = [];

    private static IEnumerable<IPAddress> LanAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                list.AddRange(ni.GetIPProperties().UnicastAddresses.Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork));
            }
        }
        catch { }
        return list.Take(3);
    }

    private static async Task<string?> LatestTag(string repo, int? port, bool anyKind)
    {
        try
        {
            using var c = Client(port, 10);
            var arr = JsonNode.Parse(await c.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=8"))!.AsArray();
            return arr.Where(x => anyKind || x?["prerelease"]?.GetValue<bool>() != true)
                .Select(x => x?["tag_name"]?.ToString()?.TrimStart('v')).Where(x => Version.TryParse(x, out _)).OrderByDescending(x => Version.Parse(x!)).FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Real TLS handshake to the server (through the scan's proxy when the server is filtered directly).</summary>
    private static async Task<(CheckState, string)> HandshakeAsync(ProfileItem p, int? port, CancellationToken ct)
    {
        var sni = string.IsNullOrWhiteSpace(p.Sni) ? p.Address : p.Sni;
        var reality = p.StreamSecurity == "reality";
        foreach (var direct in new[] { true, false })
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(6000);
                Stream raw;
                var tcp = new TcpClient();
                if (direct) { await tcp.ConnectAsync(p.Address, p.Port, cts.Token); raw = tcp.GetStream(); }
                else
                {
                    if (port == null) break;
                    await tcp.ConnectAsync(IPAddress.Loopback, port.Value, cts.Token);
                    raw = tcp.GetStream();
                    await Socks5Connect(raw, p.Address, p.Port, cts.Token);
                }
                await using var ssl = new SslStream(raw, false, (_, _, _, e) => reality || e == SslPolicyErrors.None);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = sni, ApplicationProtocols = [SslApplicationProtocol.Http11] }, cts.Token);
                tcp.Dispose();
                return (CheckState.Pass, $"{ssl.SslProtocol} · {(reality ? "REALITY" : L.S("sc_cert_ok"))}");
            }
            catch (System.Security.Authentication.AuthenticationException) when (!reality) { return (CheckState.Fail, L.S("sc_handshake_cert")); }
            catch { }
        }
        return (CheckState.Unknown, L.S("sc_handshake_na"));
    }

    private static async Task Socks5Connect(Stream s, string host, int port, CancellationToken ct)
    {
        await s.WriteAsync(new byte[] { 5, 1, 0 }, ct);
        var b = new byte[2]; await s.ReadExactlyAsync(b, ct);
        var hb = System.Text.Encoding.ASCII.GetBytes(host);
        var req = new byte[7 + hb.Length];
        req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3; req[4] = (byte)hb.Length;
        hb.CopyTo(req, 5); req[^2] = (byte)(port >> 8); req[^1] = (byte)port;
        await s.WriteAsync(req, ct);
        var head = new byte[4]; await s.ReadExactlyAsync(head, ct);
        if (head[1] != 0) throw new IOException("socks " + head[1]);
        var skip = head[3] switch { 1 => 4, 4 => 16, 3 => -1, _ => 4 };
        if (skip == -1) { var l = new byte[1]; await s.ReadExactlyAsync(l, ct); skip = l[0]; }
        await s.ReadExactlyAsync(new byte[skip + 2], ct);
    }

    private static string Mask(string ip)
    {
        var i = ip.LastIndexOfAny(['.', ':']);
        return i > 0 ? ip[..(i + 1)] + "•••" : ip;
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

/// <summary>Tor bulk exit list, cached on disk for 6 h and fetched through the scan's proxy (torproject.org is filtered in some places).</summary>
public static class TorExits
{
    public sealed record List(HashSet<string> Set, DateTime At);
    private static List? _mem;
    private static string File_ => Utils.GetConfigPath("tor-exits.txt");

    /// <summary>No network: answers from the in-memory or on-disk list only (used by Home next to the exit IP).</summary>
    public static bool IsKnownExit(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        try
        {
            if (_mem == null && File.Exists(File_)) _mem = new List(ConfigAudit.ParseTorList(File.ReadAllText(File_)), File.GetLastWriteTimeUtc(File_));
        }
        catch { }
        return _mem?.Set.Contains(ip) == true;
    }

    public static async Task<List> GetAsync(int? port, CancellationToken ct)
    {
        if (_mem != null && DateTime.UtcNow - _mem.At < TimeSpan.FromHours(6)) return _mem;
        if (File.Exists(File_) && DateTime.UtcNow - File.GetLastWriteTimeUtc(File_) < TimeSpan.FromHours(6))
            return _mem = new List(ConfigAudit.ParseTorList(await File.ReadAllTextAsync(File_, ct)), File.GetLastWriteTimeUtc(File_));
        foreach (var p in port is int pp ? new int?[] { pp, null } : [null])
        {
            try
            {
                var h = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8), UseProxy = p != null, Proxy = p != null ? new WebProxy($"socks5://127.0.0.1:{p}") : null };
                using var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(20) };
                var text = await c.GetStringAsync("https://check.torproject.org/torbulkexitlist", ct);
                var set = ConfigAudit.ParseTorList(text);
                if (set.Count < 100) continue;
                await File.WriteAllTextAsync(File_, text, ct);
                return _mem = new List(set, DateTime.UtcNow);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }
        // stale cache beats nothing
        if (File.Exists(File_)) return _mem = new List(ConfigAudit.ParseTorList(await File.ReadAllTextAsync(File_, ct)), File.GetLastWriteTimeUtc(File_));
        return new List([], DateTime.MinValue);
    }
}
