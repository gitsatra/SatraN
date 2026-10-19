using System.Text.Json.Nodes;
using Avalonia.Layout;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Subscriptions with quota/expiry colours, branding links, and Satra Hub owner screens (devices, limits, PIN).</summary>
public class SubsPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _list = new() { Spacing = 12, Margin = new Thickness(0, 18, 0, 28) };
    public Control View => _root;

    public SubsPage()
    {
        _root.Children.Add(Ui.Row(Ui.Title(L.S("subs")), Ui.H(8,
            Ui.Btn(L.S("update_all"), Ui.Kind.Ghost, () => _ = SatraCore.I.UpdateSubsAsync(), "⟳", 13),
            Ui.Btn(L.S("add"), Ui.Kind.Tinted, () => _ = AddAsync(), "+", 13))));
        var sv = new ScrollViewer { Content = _list };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
    }

    private async Task AddAsync()
    {
        await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());
        Refresh();
    }

    public void Refresh() => _ = BuildAsync();

    private async Task BuildAsync()
    {
        var subs = await AppManager.Instance.SubItems() ?? [];
        _list.Children.Clear();
        if (subs.Count == 0)
        {
            _list.Children.Add(Ui.Card(Ui.V(10, Ui.T(L.S("no_server"), 16, FontWeight.SemiBold), Ui.Caption(L.S("add_hint")),
                Ui.Btn(L.S("add_clip"), Ui.Kind.Primary, () => _ = AddAsync())), new Thickness(20)));
            return;
        }
        var i = 0;
        foreach (var s in subs) _list.Children.Add(Ui.Enter(Card(s), i++ * 40));
    }

    private Control Card(SubItem s)
    {
        SatraState.I.Subs.TryGetValue(s.Id, out var m);
        var title = m?.Title is { Length: > 0 } t ? t : s.Remarks;
        var kids = new List<Control>();
        var enabled = Ui.Switch(s.Enabled, async v => { s.Enabled = v; await ConfigHandler.AddSubItem(AppManager.Instance.Config, s); SatraCore.I.Notify(L.S("reconnect_note")); });
        kids.Add(Ui.Row(Ui.V(2, Ui.T(title, 18, FontWeight.Bold), Ui.Caption(SatraState.I.Hub.ContainsKey(s.Id) ? "Satra Hub · " + L.S("hub_registered") : HostOf(s.Url), P.Text3)), enabled));

        if (m != null)
        {
            var (frac, data, time) = SubInfo.Describe(m);
            var col = P.Level(frac);
            kids.Add(Ui.Bar(frac, col, 8));
            kids.Add(Ui.Row(Ui.T(data, 13, FontWeight.Medium, P.Text2), Ui.Pill(time, col, P.Lerp(P.Surface, col, 0.16))));
            if (!string.IsNullOrWhiteSpace(m.Announce))
                kids.Add(new Border { Background = P.B(P.InfoSoft), CornerRadius = Ui.RCtl, Padding = new Thickness(12, 8), Child = Ui.T("📣  " + m.Announce, 13, FontWeight.Normal, P.Text, TextWrapping.Wrap) });
        }

        var actions = new List<Control> { Ui.Btn(L.S("update"), Ui.Kind.Ghost, () => _ = SatraCore.I.UpdateSubsAsync(s.Id), "⟳", 13) };
        if (m?.RenewUrl is { Length: > 0 } ru) actions.Add(Ui.Btn(L.S("renew"), Ui.Kind.Primary, () => ProcUtils.ProcessStart(ru), size: 13));
        if (m?.SupportUrl is { Length: > 0 } su) actions.Add(Ui.Btn(L.S("support"), Ui.Kind.Tinted, () => ProcUtils.ProcessStart(su), size: 13));
        if (SatraState.I.Hub.ContainsKey(s.Id))
        {
            actions.Add(Ui.Btn(L.S("devices"), Ui.Kind.Ghost, () => _ = DevicesSheet(s.Id), "▢", 13));
            actions.Add(Ui.Btn(L.S("limits"), Ui.Kind.Ghost, () => _ = LimitsSheet(s.Id), "◔", 13));
        }
        else if (m?.HubBase is { Length: > 0 })
        {
            actions.Add(Ui.Btn(L.S("hub_register"), Ui.Kind.Tinted, async () => { await HubSync.EnsureRegisteredAsync(s); Refresh(); }, size: 13));
        }
        kids.Add(new WrapPanel { Children = { Ui.H(8, actions.ToArray()) } });
        return Ui.Card(Ui.V(12, kids.ToArray()), new Thickness(20, 16), s.Enabled ? null : P.Lerp(P.Surface, P.Bg, 0.5));
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

    private static async Task<HubClient?> Owner(string subId)
    {
        var c = HubSync.ClientFor(subId);
        if (c == null) return null;
        if (c.Pin == null)
        {
            var pin = await SatraShell.I.Prompt(L.S("pick_pin"), "PIN", secret: true);
            if (string.IsNullOrEmpty(pin)) return null;
            c.Pin = pin;
        }
        return c;
    }

    private static async Task<T?> Try<T>(HubClient c, Func<Task<T>> f)
    {
        try { return await f(); }
        catch (HubException e)
        {
            if (e.Code.Contains("pin", StringComparison.OrdinalIgnoreCase)) { c.Pin = null; SatraCore.I.Notify(L.S("wrong_pin")); }
            else SatraCore.I.Notify(e.Message);
        }
        catch (Exception e) { SatraCore.I.Notify(e.Message); }
        return default;
    }

    private async Task DevicesSheet(string subId)
    {
        var c = HubSync.ClientFor(subId);
        if (c == null) return;
        var list = await Try(c, () => c.Devices());
        if (list == null) return;
        var me = SatraState.I.Hub[subId].DeviceId;
        var rows = new List<Control>();
        Action close = () => { };
        foreach (var d in list.OfType<JsonObject>())
        {
            var id = d["id"]?.ToString() ?? "";
            var online = d["online"]?.GetValue<bool>() == true;
            var u = d["usage"];
            long L_(string k) => long.TryParse(u?[k]?.ToString(), out var v) ? v : 0;
            var sub = $"{d["os"]} · {Ui.Bytes(L_("today"))} {L.S("today")} · {Ui.Bytes(L_("up") + L_("down"))} {L.S("total")}";
            var dot = new Border { Width = 9, Height = 9, CornerRadius = Ui.RPill, Background = P.B(online ? P.Success : P.Text3) };
            var name = (d["name"]?.ToString() ?? id) + (id == me ? $"  ({L.S("this_device")})" : "");
            var del = Ui.IconBtn("🗑", async () =>
            {
                var oc = await Owner(subId);
                if (oc != null && await Try(oc, async () => { await oc.DeleteDevice(id); return true; })) { close(); await DevicesSheet(subId); }
            }, L.S("delete"), P.Danger, 14);
            var lim = Ui.IconBtn("◔", () => { close(); _ = LimitsSheet(subId, id, d["name"]?.ToString()); }, L.S("limits"), P.Text2, 14);
            rows.Add(Ui.ListRow(name, sub, Ui.H(4, dot, lim, del)));
        }
        close = SatraShell.I.Sheet(L.S("devices"), Ui.V(14, Ui.Group(rows.ToArray()),
            Ui.Btn(L.S("owner_pin"), Ui.Kind.Ghost, () => _ = SetPin(subId), "🔒", 13)));
    }

    private static async Task SetPin(string subId)
    {
        var c = HubSync.ClientFor(subId);
        if (c == null) return;
        var old = await SatraShell.I.Prompt(L.S("pick_pin") + " (old / empty)", "PIN", true);
        var nw = await SatraShell.I.Prompt(L.S("set_pin"), "PIN", true);
        if (string.IsNullOrEmpty(nw) || nw.Length < 4) return;
        if (await Try(c, async () => { await c.SetPin(old, nw); return true; })) { c.Pin = nw; SatraCore.I.Notify("✓"); }
    }

    private async Task LimitsSheet(string subId, string? deviceId = null, string? deviceName = null)
    {
        var c = HubSync.ClientFor(subId);
        if (c == null) return;
        var limits = await Try(c, () => c.Limits());
        if (limits == null) return;
        Action close = () => { };
        var rows = new List<Control>();
        foreach (var l in limits.OfType<JsonObject>())
        {
            if (deviceId != null && l["deviceId"]?.ToString() != deviceId) continue;
            var id = l["id"]?.ToString() ?? "";
            long.TryParse(l["quotaBytes"]?.ToString(), out var q);
            long.TryParse(l["state"]?["usedBytes"]?.ToString(), out var used);
            var ex = l["state"]?["exhausted"]?.GetValue<bool>() == true;
            var frac = q > 0 ? Math.Min(1, (double)used / q) : 1;
            var valsTxt = string.Join(", ", l["match"]?["values"]?.AsArray().Select(x => x?.ToString()) ?? []);
            var head = Ui.Row(Ui.V(2, Ui.T(l["name"]?.ToString() is { Length: > 0 } n ? n : valsTxt, 14.5, FontWeight.SemiBold),
                    Ui.Caption($"{l["match"]?["type"]} · {valsTxt} · {PeriodText(l["period"]?.ToString())}" + (l["scope"]?.ToString() == "device" ? $" · {L.S("this_device")}" : ""), P.Text3)),
                Ui.H(4, ex ? Ui.Pill(L.S("blocked"), P.Danger, P.DangerSoft) : Ui.Pill($"{Ui.Bytes(used)} / {Ui.Bytes(q)}", P.Text2, P.Fill),
                    Ui.IconBtn("+", async () =>
                    {
                        var oc = await Owner(subId);
                        if (oc != null && await Try(oc, async () => { await oc.Topup(id, 500L << 20); return true; })) { close(); await LimitsSheet(subId, deviceId, deviceName); }
                    }, L.S("topup") + " 500 MB", P.Success, 15),
                    Ui.IconBtn("🗑", async () =>
                    {
                        var oc = await Owner(subId);
                        if (oc != null && await Try(oc, async () => { await oc.DeleteLimit(id); return true; })) { close(); await LimitsSheet(subId, deviceId, deviceName); }
                    }, L.S("delete"), P.Danger, 14)));
            rows.Add(new Border { Padding = new Thickness(14, 12), Child = Ui.V(8, head, Ui.Bar(frac, P.Level(frac))) });
        }
        if (rows.Count == 0) rows.Add(new Border { Padding = new Thickness(14), Child = Ui.Caption("—") });

        // add form
        var type = new ComboBox { ItemsSource = new[] { L.S("site"), L.S("category"), L.S("all_traffic") }, SelectedIndex = 0, MinWidth = 130 };
        var vals = Ui.Input("instagram.com, youtube.com");
        var mb = Ui.Input("MB", "500");
        mb.Width = 90;
        var per = new ComboBox { ItemsSource = new[] { L.S("per_day"), L.S("per_week"), L.S("per_month") }, SelectedIndex = 0 };
        var add = Ui.Btn(L.S("add_limit"), Ui.Kind.Primary, async () =>
        {
            var oc = await Owner(subId);
            if (oc == null || !long.TryParse(mb.Text, out var m)) return;
            var t = type.SelectedIndex switch { 1 => "category", 2 => "all", _ => "domain" };
            var values = (vals.Text ?? "").Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
            var body = new Dictionary<string, object?>
            {
                ["id"] = "l" + Guid.NewGuid().ToString("N")[..8], ["v"] = 1, ["name"] = values.FirstOrDefault() ?? L.S("all_traffic"),
                ["scope"] = deviceId == null ? "sub" : "device", ["deviceId"] = deviceId,
                ["match"] = new { type = t, values }, ["quotaBytes"] = m << 20,
                ["period"] = per.SelectedIndex switch { 1 => "weekly", 2 => "monthly", _ => "daily" }, ["enabled"] = true,
            };
            if (await Try(oc, async () => { await oc.PutLimit(body); return true; })) { close(); await LimitsSheet(subId, deviceId, deviceName); }
        }, "+", 13);
        var form = Ui.Card(Ui.V(10, Ui.Section(L.S("add_limit")), Ui.H(8, type, per, mb), vals, add), new Thickness(14), P.Raised);
        var title = L.S("server_limits") + (deviceName != null ? " · " + deviceName : "");
        close = SatraShell.I.Sheet(title, Ui.V(14, Ui.Group(rows.ToArray()), form), 620);
    }

    public static string PeriodText(string? p) => p switch { "weekly" => L.S("per_week"), "monthly" => L.S("per_month"), "total" => L.S("total"), _ => L.S("per_day") };
}
