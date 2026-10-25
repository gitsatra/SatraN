using System.Text.Json.Nodes;
using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class TunHelperTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }
    private const string Good = """{"log":{"level":"warn","output":"/etc/passwd"},"inbounds":[{"type":"tun","interface_name":"singbox_tun","route_exclude_address":["127.0.0.1/32"]}],"outbounds":[{"type":"socks","tag":"proxy","server":"127.0.0.1","server_port":28888},{"type":"direct","tag":"direct"}],"experimental":{"cache_file":{"enabled":true,"path":"/home/x/cache.db"},"clash_api":{"external_controller":"0.0.0.0:9090","external_ui":"/"},"v2ray_api":{}}}""";

    [Test]
    public void Sanitize_strips_dangerous_fields_and_adds_lan_excludes()
    {
        var o = JsonNode.Parse(TunHelper.Sanitize(Good, "/run/satran"))!;
        Ok(null == o["log"]!["output"]);
        Ok("/run/satran/cache.db" == (string?)o["experimental"]!["cache_file"]!["path"]);
        Ok(null == o["experimental"]!["clash_api"]!["external_controller"]);
        Ok(null == o["experimental"]!["clash_api"]!["external_ui"]);
        Ok(null == o["experimental"]!["v2ray_api"]);
        var ex = o["inbounds"]![0]!["route_exclude_address"]!.AsArray().Select(x => (string?)x).ToList();
        Ok(ex.Contains("192.168.0.0/16"));
        Ok(ex.Contains("10.0.0.0/8"));
    }

    [Test]
    [Arguments("""{"inbounds":[{"type":"tun"},{"type":"mixed","listen":"0.0.0.0"}],"outbounds":[]}""")]
    [Arguments("""{"inbounds":[],"outbounds":[]}""")]
    [Arguments("""{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"socks","server":"8.8.8.8"}]}""")]
    [Arguments("""{"inbounds":[{"type":"tun"}],"outbounds":[{"type":"vless","server":"1.1.1.1"}]}""")]
    [Arguments("""{"inbounds":[{"type":"tun"}],"outbounds":[],"endpoints":[{"type":"wireguard"}]}""")]
    [Arguments("""{"inbounds":[{"type":"tun","interface_name":"a;rm -rf /"}],"outbounds":[]}""")]
    [Arguments("""{"inbounds":[{"type":"tun"}],"outbounds":[],"route":{"rule_set":[{"type":"local","path":"/etc/shadow"}]}}""")]
    public void Sanitize_rejects(string cfg)
    {
        try { TunHelper.Sanitize(cfg, "/run/satran"); } catch (InvalidDataException) { return; }
        throw new Exception("accepted: " + cfg);
    }

    [Test]
    public void Acl_by_allow_file_and_group()
    {
        var d = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(d, "passwd"), "root:x:0:0::/root:/bin/sh\nliver:x:1000:1000::/home/liver:/bin/bash\nbob:x:1001:1001::/home/bob:/bin/bash\neve:x:1002:1002::/home/eve:/bin/bash\n");
        File.WriteAllText(Path.Combine(d, "allow"), "# users\nliver\n");
        File.WriteAllText(Path.Combine(d, "group"), "satran:x:990:bob\n");
        bool A(int u) => TunHelper.UidAllowed(u, Path.Combine(d, "allow"), Path.Combine(d, "passwd"), Path.Combine(d, "group"));
        Ok(A(0));
        Ok(A(1000));
        Ok(A(1001));
        Ok(!A(1002));
        Ok(!A(-1));
    }

    [Test]
    public void Sanitize_keeps_upstream_server_exclude_so_tun_does_not_loop()
    {
        var cfg = Good.Replace("\"127.0.0.1/32\"", "\"127.0.0.1/32\",\"107.189.24.195/32\"");
        var ex = JsonNode.Parse(TunHelper.Sanitize(cfg, "/run/satran"))!["inbounds"]![0]!["route_exclude_address"]!.AsArray().Select(x => (string?)x).ToList();
        Ok(ex.Contains("107.189.24.195/32"));
    }
}
