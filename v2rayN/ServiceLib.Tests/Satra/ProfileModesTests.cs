using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class ProfileModesTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }

    private const string Xray = """
    {"inbounds":[{"tag":"socks","protocol":"socks","sniffing":{"enabled":true,"destOverride":["http","tls"]}}],
     "outbounds":[{"tag":"proxy","protocol":"vless","settings":{"vnext":[{"users":[{"flow":""}]}]},
       "streamSettings":{"network":"ws","security":"tls","tlsSettings":{"serverName":"a.com","allowInsecure":true}},
       "mux":{"enabled":true}},{"tag":"direct","protocol":"freedom"}],
     "dns":{"servers":["223.5.5.5",{"address":"8.8.8.8","domains":["geosite:ir"]}]},
     "routing":{"domainStrategy":"IPIfNonMatch","rules":[{"type":"field","outboundTag":"direct","ip":["geoip:private"]}]}}
    """;

    private const string Sbox = """
    {"inbounds":[{"type":"tun","tag":"tun-in","stack":"gvisor","mtu":9000,"address":["172.18.0.1/30","fdfe::1/126"],"strict_route":false}],
     "outbounds":[{"type":"vless","tag":"proxy","tls":{"enabled":true,"insecure":true}},{"type":"direct","tag":"direct"}],
     "dns":{"servers":[]},
     "route":{"rules":[{"action":"sniff"},{"protocol":"dns","action":"hijack-dns"},{"ip_is_private":true,"outbound":"direct"}]}}
    """;

    [Test]
    public void Normal_and_unknown_leave_config_untouched()
    {
        Ok(ProfileModes.Apply(Xray, false, "normal", false) == Xray);
        Ok(ProfileModes.Apply(Xray, false, null, false) == Xray);
        Ok(ProfileModes.Apply(Xray, false, "nope", true) == Xray);
        Ok(ProfileModes.Apply("not json", false, "game", false) == "not json");
    }

    [Test]
    public void Fortress_only_with_tun()
    {
        Ok(!ProfileModes.Available("fortress", false));
        Ok(ProfileModes.Available("fortress", true));
        Ok(ProfileModes.Apply(Sbox, true, "fortress", false) == Sbox, "fortress must not apply in proxy modes");
        var j = JsonNode.Parse(ProfileModes.Apply(Sbox, true, "fortress", true))!;
        Ok((bool)j["inbounds"]![0]!["strict_route"]! == true);
    }

    [Test]
    public void Game_turns_mux_off_and_light_routing_in_xray()
    {
        var j = JsonNode.Parse(ProfileModes.Apply(Xray, false, "game", false))!;
        Ok((bool)j["outbounds"]![0]!["mux"]!["enabled"]! == false);
        Ok((bool)j["inbounds"]![0]!["sniffing"]!["routeOnly"]! == true);
        Ok((string)j["routing"]!["domainStrategy"]! == "AsIs");
    }

    [Test]
    public void Secure_blocks_quic_dns_over_proxy_utls_fragment_in_xray()
    {
        var j = JsonNode.Parse(ProfileModes.Apply(Xray, false, "secure", false))!;
        var r0 = j["routing"]!["rules"]![0]!;
        Ok((string)r0["network"]! == "udp" && (string)r0["port"]! == "443" && (string)r0["outboundTag"]! == "block");
        Ok(j["outbounds"]!.AsArray().Any(o => (string?)o!["tag"] == "block"));
        var tls = j["outbounds"]![0]!["streamSettings"]!["tlsSettings"]!;
        Ok((string)tls["fingerprint"]! == "chrome" && (bool)tls["allowInsecure"]! == false);
        Ok((string)j["outbounds"]![0]!["streamSettings"]!["sockopt"]!["dialerProxy"]! == "satra-frag");
        var dns = j["dns"]!["servers"]!.AsArray();
        Ok(!dns.Any(s => s is JsonValue v && (string?)v == "223.5.5.5"), "bare direct resolver must go");
        Ok(dns.Any(s => s is JsonObject), "domain-scoped resolver stays");
        Ok((string)j["dns"]!["queryStrategy"]! == "UseIPv4");
    }

    [Test]
    public void Game_and_stream_tune_tun_in_singbox()
    {
        var g = JsonNode.Parse(ProfileModes.Apply(Sbox, true, "game", true))!;
        Ok((string)g["inbounds"]![0]!["stack"]! == "system" && (int)g["inbounds"]![0]!["mtu"]! == 1400);
        Ok((bool)g["outbounds"]![0]!["multiplex"]!["enabled"]! == false);
        var s = JsonNode.Parse(ProfileModes.Apply(Sbox, true, "stream", true))!;
        Ok((string)s["inbounds"]![0]!["stack"]! == "mixed");
    }

    [Test]
    public void Secure_singbox_keeps_dns_hijack_first_and_drops_v6()
    {
        var j = JsonNode.Parse(ProfileModes.Apply(Sbox, true, "secure", true))!;
        var rules = j["route"]!["rules"]!.AsArray();
        Ok((string)rules[1]!["action"]! == "hijack-dns");
        Ok((string)rules[2]!["action"]! == "reject" && (int)rules[2]!["port"]! == 443);
        Ok(j["inbounds"]![0]!["address"]!.AsArray().All(a => !((string)a!).Contains(':')));
        Ok((bool)j["outbounds"]![0]!["tls"]!["insecure"]! == false);
    }

    [Test]
    public void Eco_adds_ads_block_and_changes_list_never_empty()
    {
        var j = JsonNode.Parse(ProfileModes.Apply(Xray, false, "eco", false))!;
        Ok(j["routing"]!["rules"]!.ToJsonString().Contains("geosite:category-ads-all"));
        foreach (var m in ProfileModes.All) Ok(ProfileModes.Changes(m, true).Count > 0);
        Ok(ProfileModes.Changes("game", false).All(c => c.key != "ch_mtu"), "no TUN items outside TUN");
    }
}
