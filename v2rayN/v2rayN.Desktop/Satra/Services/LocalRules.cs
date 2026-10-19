using System.Text.Json;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Writes block rules for exhausted local limits into the active routing (tagged "satra:"), then reloads the core.</summary>
public static class LocalRules
{
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
        if (!st.SmartRouting) add.Add(new RulesItem { Id = Utils.GetGuid(false), OutboundTag = Global.ProxyTag, Enabled = true, Remarks = "satra:all", Port = "0-65535" });
        rules.InsertRange(0, add);
        if (JsonSerializer.Serialize(rules) == before) return;
        routing.RuleSet = JsonUtils.Serialize(rules, false);
        routing.RuleNum = rules.Count;
        await ConfigHandler.SaveRoutingItem(cfg, routing);
        await SatraCore.I.Main.Reload();
    }
}
