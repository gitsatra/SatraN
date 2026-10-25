namespace ServiceLib.Satra;

/// <summary>
/// Satra connection modes (see /workspace/satra/MODES-SPEC.md, shared with Android). A mode is an overlay applied to
/// the generated core JSON only: the user's stored settings are never touched, so "normal" is exactly the old output.
/// </summary>
public static class ProfileModes
{
    public const string Normal = "normal", Game = "game", Secure = "secure", Fortress = "fortress", Stream = "stream", Eco = "eco";
    public static readonly string[] All = [Normal, Game, Secure, Fortress, Stream, Eco];
    public static bool IsValid(string? m) => m != null && All.Contains(m);
    public static string Normalize(string? m) => IsValid(m) ? m! : Normal;

    /// <summary>Desktop: fortress needs the whole system (TUN); every other mode works in every connection type.</summary>
    public static bool Available(string mode, bool tun) => mode != Fortress || tun;

    /// <summary>What a mode changes, as string keys (+ optional arg) for the activation animation. Same order as applied.</summary>
    public static List<(string key, string? arg)> Changes(string mode, bool tun)
    {
        var l = new List<(string, string?)>();
        switch (mode)
        {
            case Game:
                l.Add(("ch_mux_off", null)); l.Add(("ch_sniff_light", null)); l.Add(("ch_dns_fast", null)); l.Add(("ch_quic_allow", null));
                if (tun) { l.Add(("ch_stack", "system")); l.Add(("ch_mtu", "1400")); }
                break;
            case Secure:
            case Fortress:
                l.Add(("ch_dns_proxy", null)); l.Add(("ch_quic_block", null)); l.Add(("ch_utls", null)); l.Add(("ch_fragment", null)); l.Add(("ch_ipv6_off", null));
                if (tun && mode == Fortress) l.Add(("ch_strict", null));
                break;
            case Stream:
                l.Add(("ch_mux_off", null)); l.Add(("ch_quic_allow", null));
                if (tun) { l.Add(("ch_stack", "mixed")); l.Add(("ch_mtu", "1500")); }
                break;
            case Eco:
                l.Add(("ch_ads", null)); l.Add(("ch_mux_on", null)); l.Add(("ch_quic_block", null));
                break;
        }
        if (l.Count == 0) l.Add(("ch_none", null));
        return l;
    }

    /// <summary>Colour identity (dark-theme friendly, Apple system palette) and emoji per mode (MODES-SPEC.md).</summary>
    public static string Color(string mode) => Normalize(mode) switch
    {
        Game => "#BF5AF2", Secure => "#0A84FF", Fortress => "#FF453A", Stream => "#FF375F", Eco => "#30D158", _ => "#8E9BB0",
    };
    public static string Emoji(string mode) => Normalize(mode) switch
    {
        Game => "🎮", Secure => "🛡️", Fortress => "🏰", Stream => "🎬", Eco => "🌿", _ => "🌐",
    };

    public enum Act { On, Off, Set }
    /// <summary>One real setting the overlay changes: label key, state, exact value written to the core config, reason key.</summary>
    public sealed record Detail(string Key, Act State, string Value, string Why);

    /// <summary>Exactly what <see cref="Apply"/> writes, for the "!" info panel. Keep in sync with Apply.</summary>
    public static List<Detail> Details(string mode, bool tun)
    {
        mode = Normalize(mode);
        var l = new List<Detail>();
        switch (mode)
        {
            case Game:
                l.Add(new("md_mux", Act.Off, "mux.enabled=false", "why_game_mux"));
                l.Add(new("md_sniff", Act.Set, "sniffing.routeOnly=true", "why_game_sniff"));
                l.Add(new("md_dstrat", Act.Set, "domainStrategy=AsIs", "why_game_dstrat"));
                l.Add(new("md_dns_fb", Act.Off, "dns.disableFallback=true · cache on", "why_game_dns"));
                l.Add(new("md_quic", Act.On, "UDP/443 allowed", "why_game_quic"));
                if (tun) { l.Add(new("md_stack", Act.Set, "stack=system", "why_game_stack")); l.Add(new("md_mtu", Act.Set, "mtu=1400", "why_game_mtu")); }
                break;
            case Secure:
            case Fortress:
                l.Add(new("md_dns_doh", Act.Set, "https://1.1.1.1/dns-query, https://8.8.8.8/dns-query", "why_sec_dns"));
                l.Add(new("md_ipv4", Act.Set, "queryStrategy=UseIPv4 / ipv4_only", "why_sec_ipv6"));
                l.Add(new("md_quic", Act.Off, "udp/443 → block", "why_sec_quic"));
                l.Add(new("md_utls", Act.On, "fingerprint=chrome (if empty)", "why_sec_utls"));
                l.Add(new("md_frag", Act.On, "fragment tlshello 100-200 / 10-20 ms (TLS, not Reality)", "why_sec_frag"));
                l.Add(new("md_insecure", Act.Off, "allowInsecure=false", "why_sec_insecure"));
                if (tun) l.Add(new("md_tun6", Act.Off, "tun address: IPv6 removed", "why_sec_ipv6"));
                if (tun && mode == Fortress) l.Add(new("md_strict", Act.On, "strict_route=true", "why_fort_strict"));
                break;
            case Stream:
                l.Add(new("md_mux", Act.Off, "mux.enabled=false", "why_stream_mux"));
                l.Add(new("md_quic", Act.On, "UDP/443 allowed", "why_stream_quic"));
                if (tun) { l.Add(new("md_stack", Act.Set, "stack=mixed", "why_stream_stack")); l.Add(new("md_mtu", Act.Set, "mtu=1500", "why_stream_mtu")); }
                break;
            case Eco:
                l.Add(new("md_ads", Act.On, "geosite:category-ads-all → block", "why_eco_ads"));
                l.Add(new("md_mux", Act.On, "mux concurrency=8 (not Vision/Reality)", "why_eco_mux"));
                l.Add(new("md_quic", Act.Off, "udp/443 → block", "why_eco_quic"));
                break;
        }
        return l;
    }

    /// <summary>Apply the overlay to a generated Xray or sing-box client config. Returns the input unchanged for normal / invalid JSON.</summary>
    public static string Apply(string json, bool singbox, string? mode, bool tun)
    {
        mode = Normalize(mode);
        if (mode == Normal || !Available(mode, tun)) return json;
        JsonNode? root;
        try { root = JsonNode.Parse(json); } catch { return json; }
        if (root is not JsonObject o) return json;
        if (singbox) ApplySingbox(o, mode, tun); else ApplyXray(o, mode);
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static IEnumerable<JsonObject> Objs(JsonNode? arr) => arr is JsonArray a ? a.OfType<JsonObject>() : [];
    private static readonly string[] ProxyProtocols = ["vless", "vmess", "trojan", "shadowsocks", "socks", "http", "hysteria2", "hysteria", "tuic", "wireguard", "anytls"];
    private static bool IsProxy(JsonObject ob) => ProxyProtocols.Contains((string?)(ob["protocol"] ?? ob["type"]) ?? "");

    // ---------------- Xray ----------------
    private static void ApplyXray(JsonObject o, string mode)
    {
        var outs = Objs(o["outbounds"]).ToList();
        var proxies = outs.Where(IsProxy).ToList();
        var secure = mode is Secure or Fortress;
        var muxable = proxies.Where(p => (string?)p["protocol"] is "vmess" or "vless" or "trojan" or "shadowsocks").ToList();
        if (mode is Game or Stream) foreach (var p in muxable) p["mux"] = new JsonObject { ["enabled"] = false, ["concurrency"] = -1 };
        if (mode == Eco)
            foreach (var p in muxable)
            {
                var vision = p["settings"]?["vnext"] is JsonArray vn && vn.ToJsonString().Contains("\"flow\":\"xtls");
                var reality = (string?)p["streamSettings"]?["security"] == "reality";
                if (!vision && !reality) p["mux"] = new JsonObject { ["enabled"] = true, ["concurrency"] = 8 };
            }
        if (mode == Game)
        {
            foreach (var i in Objs(o["inbounds"])) if (i["sniffing"] is JsonObject s) s["routeOnly"] = true;
            if (o["routing"] is JsonObject r) r["domainStrategy"] = "AsIs";
            if (o["dns"] is JsonObject d) { d["disableFallback"] = true; d["disableCache"] = false; }
        }
        if (secure)
        {
            foreach (var p in proxies)
            {
                if (p["streamSettings"] is not JsonObject ss) continue;
                foreach (var k in new[] { "tlsSettings", "realitySettings" })
                    if (ss[k] is JsonObject t)
                    {
                        if (string.IsNullOrEmpty((string?)t["fingerprint"])) t["fingerprint"] = "chrome";
                        if (k == "tlsSettings") t["allowInsecure"] = false;
                    }
                // TLS ClientHello fragmentation through a local freedom hop (Xray 1.8+); never for reality (would break it)
                if ((string?)ss["security"] == "tls" && (string?)p["tag"] == "proxy")
                {
                    var sock = ss["sockopt"] as JsonObject ?? new JsonObject();
                    if (sock["dialerProxy"] == null) { sock["dialerProxy"] = "satra-frag"; ss["sockopt"] = sock; }
                }
            }
            if (outs.All(x => (string?)x["tag"] != "satra-frag") && o["outbounds"] is JsonArray oa)
                oa.Add(new JsonObject
                {
                    ["tag"] = "satra-frag", ["protocol"] = "freedom",
                    ["settings"] = new JsonObject { ["fragment"] = new JsonObject { ["packets"] = "tlshello", ["length"] = "100-200", ["interval"] = "10-20" } },
                });
            if (o["dns"] is JsonObject d)
            {
                var keep = new JsonArray();
                keep.Add("https://1.1.1.1/dns-query"); keep.Add("https://8.8.8.8/dns-query");
                // domain-scoped entries (e.g. local sites resolved locally) stay; bare system/direct resolvers go
                foreach (var s in Objs(d["servers"]).Where(s => s["domains"] is JsonArray { Count: > 0 }).ToList()) keep.Add(s.DeepClone());
                d["servers"] = keep;
                d["queryStrategy"] = "UseIPv4";
            }
        }
        if (secure || mode == Eco)
        {
            EnsureXrayBlock(o);
            var rules = XrayRules(o);
            var pre = new List<JsonObject> { new() { ["type"] = "field", ["network"] = "udp", ["port"] = "443", ["outboundTag"] = "block" } };
            if (mode == Eco) pre.Add(new JsonObject { ["type"] = "field", ["domain"] = new JsonArray("geosite:category-ads-all"), ["outboundTag"] = "block" });
            for (var i = pre.Count - 1; i >= 0; i--) rules?.Insert(0, pre[i]);
        }
    }

    private static JsonArray? XrayRules(JsonObject o)
    {
        if (o["routing"] is not JsonObject r) { r = new JsonObject(); o["routing"] = r; }
        if (r["rules"] is not JsonArray a) { a = new JsonArray(); r["rules"] = a; }
        return a;
    }

    private static void EnsureXrayBlock(JsonObject o)
    {
        if (o["outbounds"] is not JsonArray a) return;
        if (a.OfType<JsonObject>().Any(x => (string?)x["tag"] == "block")) return;
        a.Add(new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" });
    }

    // ---------------- sing-box ----------------
    private static void ApplySingbox(JsonObject o, string mode, bool tun)
    {
        var proxies = Objs(o["outbounds"]).Where(IsProxy).ToList();
        var secure = mode is Secure or Fortress;
        static bool MuxCapable(JsonObject p) => (string?)p["type"] is "vmess" or "vless" or "trojan" or "shadowsocks";
        if (mode is Game or Stream) foreach (var p in proxies.Where(MuxCapable)) p["multiplex"] = new JsonObject { ["enabled"] = false };
        if (mode == Eco)
            foreach (var p in proxies)
                if (string.IsNullOrEmpty((string?)p["flow"]) && p["tls"]?["reality"] == null && MuxCapable(p))
                    p["multiplex"] = new JsonObject { ["enabled"] = true, ["protocol"] = "h2mux", ["max_connections"] = 1, ["max_streams"] = 8 };
        if (secure)
            foreach (var p in proxies)
                if (p["tls"] is JsonObject t)
                {
                    t["insecure"] = false;
                    if (t["utls"] is not JsonObject) t["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = "chrome" };
                    if (t["reality"] == null && (string?)p["type"] is "vless" or "vmess" or "trojan") t["fragment"] = true; // sing-box 1.12 tls fragment
                }
        if (secure && o["dns"] is JsonObject d) d["strategy"] = "ipv4_only";
        if (tun)
            foreach (var i in Objs(o["inbounds"]).Where(i => (string?)i["type"] == "tun"))
            {
                if (mode == Game) { i["stack"] = "system"; i["mtu"] = 1400; }
                if (mode == Stream) { i["stack"] = "mixed"; i["mtu"] = 1500; }
                if (mode == Fortress) i["strict_route"] = true;
                if (secure && i["address"] is JsonArray addr)
                    foreach (var x in addr.ToList()) if (((string?)x ?? "").Contains(':')) addr.Remove(x);
            }
        if (secure || mode == Eco)
        {
            if (o["route"] is not JsonObject r) { r = new JsonObject(); o["route"] = r; }
            if (r["rules"] is not JsonArray rules) { rules = new JsonArray(); r["rules"] = rules; }
            // after the sniff/hijack-dns actions sing-box puts first, so DNS keeps working
            var at = 0;
            for (var k = 0; k < rules.Count; k++) if (rules[k] is JsonObject ro && (string?)ro["action"] is "sniff" or "hijack-dns" or "resolve") at = k + 1;
            rules.Insert(at, new JsonObject { ["network"] = "udp", ["port"] = 443, ["action"] = "reject" });
        }
    }
}
