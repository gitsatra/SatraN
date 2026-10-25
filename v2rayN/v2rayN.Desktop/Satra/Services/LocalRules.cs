using System.Text.Json;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Writes block rules for exhausted local limits into the active routing (tagged "satra:"), then reloads the core.</summary>
public static class LocalRules
{
    /// <summary>Domestic resolvers for traffic that goes direct (local sites must resolve to local CDNs).</summary>
    public static string DirectDns(string? region) => region switch
    {
        "russia" => "77.88.8.8,77.88.8.1",
        "china" => "223.5.5.5,119.29.29.29",
        _ => "217.218.155.155,217.218.127.127", // Iran (TCI public resolvers)
    };

    /// <summary>Apply region DNS + persist (call after the region changes).</summary>
    public static async Task ApplyDirectDnsAsync(string? region)
    {
        var cfg = AppManager.Instance.Config;
        cfg.SimpleDNSItem ??= ConfigHandler.InitBuiltinSimpleDNS();
        cfg.SimpleDNSItem.DirectDNS = DirectDns(region);
        await ConfigHandler.SaveConfig(cfg);
    }

    public static EPresetType Preset(string r) => r switch { "russia" => EPresetType.Russia, "china" => EPresetType.Default, _ => EPresetType.Iran };

    /// <summary>Direct-routed domains and IPs per region. Only geosite codes present in the stock v2fly/Chocolate4U dat files.</summary>
    public static (List<string> Domains, List<string> Ips) Region(string r) => r switch
    {
        "russia" => (["regexp:\\.ru$", "regexp:\\.su$", "regexp:\\.xn--p1ai$", "geosite:category-ru"], ["geoip:ru", "geoip:private"]),
        "china" => (["regexp:\\.cn$", "geosite:cn"], ["geoip:cn", "geoip:private"]),
        _ => (["regexp:\\.ir$", "geosite:category-ir", "domain:shaparak.ir", "domain:shetab.ir", "domain:digikala.com", "domain:snapp.taxi", "domain:divar.ir", "domain:aparat.com", "domain:bale.ai", "domain:eitaa.com", "domain:rubika.ir", "domain:filimo.com", "domain:torob.com"], ["geoip:ir", "geoip:private"]),
    };

    public static async Task SyncAsync(List<LocalLimit> blocked)
    {
        var cfg = AppManager.Instance.Config;
        var routing = await ConfigHandler.GetDefaultRouting(cfg);
        if (routing == null) return;
        var rules = JsonUtils.Deserialize<List<RulesItem>>(routing.RuleSet) ?? [];
        var before = JsonSerializer.Serialize(rules);
        rules.RemoveAll(r => r.Remarks?.StartsWith("satra:") == true);
        var add = new List<RulesItem>();
        foreach (var l in blocked)
        {
            var r = new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.BlockTag, Enabled = true, Remarks = "satra:" + l.Id };
            if (l.Type == "domain") r.Domain = l.Values.Select(v => v.Contains(':') ? v : "domain:" + v.Trim().ToLowerInvariant()).ToList();
            else if (l.Type == "process") r.Process = l.Values.ToList();
            else continue;
            add.Add(r);
        }
        var st = SatraState.I;
        static List<string> D(IEnumerable<string> v) => v.Select(x => x.Contains(':') ? x : "domain:" + x).ToList();
        if (st.ViaVpn.Count > 0) add.Add(new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.ProxyTag, Enabled = true, Remarks = "satra:via", Domain = D(st.ViaVpn) });
        if (st.Direct.Count > 0) add.Add(new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.DirectTag, Enabled = true, Remarks = "satra:direct", Domain = D(st.Direct) });
        if (st.SmartRouting)
        {
            // Own smart-routing rules (independent of the upstream template): local sites/IPs direct, rest via VPN.
            var (dom, ip) = Region(st.Routing);
            add.Add(new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.DirectTag, Enabled = true, Remarks = "satra:region-d", Domain = dom });
            add.Add(new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.DirectTag, Enabled = true, Remarks = "satra:region-ip", Ip = ip });
        }
        if (!st.SmartRouting) add.Add(new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.ProxyTag, Enabled = true, Remarks = "satra:all", Port = "0-65535" });
        rules.InsertRange(0, add);
        if (JsonSerializer.Serialize(rules) == before) return;
        routing.RuleSet = JsonUtils.Serialize(rules, false);
        routing.RuleNum = rules.Count;
        await ConfigHandler.SaveRoutingItem(cfg, routing);
        await SatraCore.I.Main.Reload();
    }
}
