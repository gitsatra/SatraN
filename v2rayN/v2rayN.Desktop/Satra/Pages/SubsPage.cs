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
    private Button _allBtn = null!;
    private readonly HashSet<string> _updating = [], _hidden = [];
    private readonly Border _allBar = new() { Padding = new Thickness(16, 12), CornerRadius = new CornerRadius(14) };

    private static Control Spinner(Color c, double size = 14)
    {
        var rt = new RotateTransform();
        var tb = new TextBlock { Text = "⟳", FontSize = size, Foreground = P.B(c), RenderTransform = rt, RenderTransformOrigin = RelativePoint.Center,
            VerticalAlignment = VerticalAlignment.Center };
        IDisposable? t = null;
        tb.AttachedToVisualTree += (_, _) => t = DispatcherTimer.Run(() => { rt.Angle = (rt.Angle + 12) % 360; return tb.IsVisible; }, TimeSpan.FromMilliseconds(33));
        tb.DetachedFromVisualTree += (_, _) => t?.Dispose();
        return tb;
    }

    private static string DisplayName(SubItem s) =>
        Ui.CleanName(SatraState.I.Subs.TryGetValue(s.Id, out var m) && m.Title is { Length: > 0 } t ? t : s.Remarks);

    private async Task UpdateOneAsync(SubItem s, bool quiet = false)
    {
        if (!_updating.Add(s.Id)) return;
        _ = BuildAsync(true);
        try { await SatraCore.I.UpdateSubsAsync(s.Id); }
        catch (Exception ex) { Logging.SaveLog("satra sub update", ex); }
        finally { _updating.Remove(s.Id); }
        await BuildAsync(true);
        if (quiet) return;
        SatraState.I.Subs.TryGetValue(s.Id, out var m);
        var n = SatraCore.I.Profiles.ProfileItems.Count(p => p.Subid == s.Id);
        SatraShell.I.Toast(m?.Error is { Length: > 0 } e ? $"✕ {e}" : $"✓ {L.F("sub_updated_n", n)}");
    }

    private async Task UpdateAllAsync()
    {
        var subs = (await AppManager.Instance.SubItems() ?? []).Where(x => x.Enabled && !_hidden.Contains(x.Id)).ToList();
        if (subs.Count == 0 || _updating.Count > 0) return;
        _allBtn.IsEnabled = false;
        _allBar.Background = P.B(P.Surface);
        _allBar.IsVisible = true;
        var fails = 0;
        for (var i = 0; i < subs.Count; i++)
        {
            _allBar.Child = Ui.V(8, Ui.Row(Ui.H(8, Spinner(P.Accent), Ui.T(L.F("updating_n", i + 1, subs.Count), 13.5, FontWeight.SemiBold)), Ui.Caption(DisplayName(subs[i]), P.Text3)),
                Ui.Bar((double)i / subs.Count, P.Accent, 6));
            await UpdateOneAsync(subs[i], quiet: true);
            if (SatraState.I.Subs.TryGetValue(subs[i].Id, out var m) && m.Error != null) fails++;
        }
        _allBar.Child = Ui.V(8, Ui.T(fails == 0 ? "✓ " + L.S("all_updated") : L.F("updated_with_errors", subs.Count - fails, fails), 13.5, FontWeight.SemiBold, fails == 0 ? P.Success : P.Warning), Ui.Bar(1, fails == 0 ? P.Success : P.Warning, 6));
        _allBtn.IsEnabled = true;
        DispatcherTimer.RunOnce(() => _allBar.IsVisible = false, TimeSpan.FromSeconds(3));
    }

    private void EditSheet(SubItem s)
    {
        var name = Ui.Input(L.S("name"), s.Remarks ?? "");
        var url = Ui.Input("https://…", s.Url ?? "");
        var ua = Ui.Input(L.S("ua_default"), s.UserAgent ?? "");
        var hrs = Ui.Input("0", s.AutoUpdateInterval > 0 ? (s.AutoUpdateInterval / 60.0).ToString("0.#") : "0");
        var en = s.Enabled;
        Action close = () => { };
        var save = Ui.Btn(L.S("save"), Ui.Kind.Primary, async () =>
        {
            if (string.IsNullOrWhiteSpace(url.Text) || !Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out _)) { SatraShell.I.Toast(L.S("sub_err_url")); return; }
            s.Remarks = string.IsNullOrWhiteSpace(name.Text) ? s.Remarks : name.Text.Trim();
            s.Url = url.Text.Trim();
            s.UserAgent = ua.Text?.Trim() ?? "";
            s.AutoUpdateInterval = double.TryParse(hrs.Text, out var h) && h > 0 ? (int)Math.Round(h * 60) : 0;
            s.Enabled = en;
            await ConfigHandler.AddSubItem(AppManager.Instance.Config, s);
            close();
            SatraShell.I.Toast(L.S("saved"));
            await BuildAsync(true);
        });
        Control F(string label, Control c) => Ui.V(6, Ui.Caption(label, P.Text2), c);
        close = SatraShell.I.Sheet(L.S("edit_sub"), Ui.V(14,
            F(L.S("name"), name), F(L.S("sub_url"), url),
            F(L.S("update_every_h"), hrs), F(L.S("user_agent"), ua),
            Ui.Row(Ui.T(L.S("enabled"), 14, FontWeight.Medium), Ui.Switch(en, v => en = v)),
            Ui.H(10, save, Ui.Btn(L.S("cancel"), Ui.Kind.Ghost, () => close()))), 480);
    }

    private void DeleteSheet(SubItem s)
    {
        Action close = () => { };
        var n = SatraCore.I.Profiles.ProfileItems.Count(p => p.Subid == s.Id);
        close = SatraShell.I.Sheet(L.S("delete_sub"), Ui.V(16,
            Ui.T(L.F("delete_sub_q", DisplayName(s), n), 14, FontWeight.Normal, P.Text2, TextWrapping.Wrap),
            Ui.H(10, Ui.Btn(L.S("delete"), Ui.Kind.Danger, () => { close(); SoftDelete(s); }), Ui.Btn(L.S("cancel"), Ui.Kind.Ghost, () => close()))), 420);
    }

    /// <summary>Hide now, really delete after 6 s unless the user taps Undo.</summary>
    private void SoftDelete(SubItem s)
    {
        _hidden.Add(s.Id);
        _ = BuildAsync(true);
        var undone = false;
        SatraShell.I.Toast(L.F("sub_deleted", DisplayName(s)), L.S("undo"), () => { undone = true; _hidden.Remove(s.Id); _ = BuildAsync(true); }, 6);
        DispatcherTimer.RunOnce(async () =>
        {
            if (undone) return;
            await ConfigHandler.DeleteSubItem(AppManager.Instance.Config, s.Id);
            SatraState.I.Subs.Remove(s.Id);
            SatraState.Save();
            _hidden.Remove(s.Id);
            SatraCore.I.Profiles.RefreshServersRequested.Publish();
            await BuildAsync(true);
        }, TimeSpan.FromSeconds(6.3));
    }

    public SubsPage()
    {
        _root.Children.Add(Ui.Row(Ui.Title(L.S("subs")), Ui.H(8,
            _allBtn = Ui.Btn(L.S("update_all"), Ui.Kind.Ghost, () => _ = UpdateAllAsync(), "⟳", 13),
            Ui.Btn(L.S("add"), Ui.Kind.Tinted, () => _ = AddAsync(), "+", 13))));
        _allBar.IsVisible = false;
        _list.Children.Add(_allBar);
        var sv = new ScrollViewer { Content = _list };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
    }

    private async Task AddAsync()
    {
        await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());
        _ = BuildAsync(true);
    }

    public void Refresh() => _ = BuildAsync();

    private string _sig = "";
    private bool _building, _built;

    private async Task BuildAsync(bool force = false)
    {
        if (_building) return;
        _building = true;
        try { await BuildCoreAsync(force); } finally { _building = false; }
    }

    private async Task BuildCoreAsync(bool force)
    {
        var subs = (await AppManager.Instance.SubItems() ?? []).Where(x => !_hidden.Contains(x.Id)).ToList();
        var st = SatraState.I;
        var sig = string.Join("|", subs.Select(s => $"{s.Id}{s.Remarks}{s.AutoUpdateInterval}{(st.Subs.TryGetValue(s.Id, out var e) ? $"{e.Fetched}{e.Error}" : "")}{s.Enabled}{s.Url}{st.Hub.ContainsKey(s.Id)}{(st.Subs.TryGetValue(s.Id, out var m) ? $"{m.Up + m.Down}{m.Total}{m.Expire}{m.Title}{m.Announce}{m.RenewUrl}{m.SupportUrl}" : "")}"));
        if (!force && sig == _sig) return;
        _sig = sig;
        _list.Children.Clear();
        _list.Children.Add(_allBar);
        if (subs.Count == 0)
        {
            _list.Children.Add(Ui.Card(Ui.V(10, Ui.T(L.S("no_server"), 16, FontWeight.SemiBold), Ui.Caption(L.S("add_hint")),
                Ui.Btn(L.S("add_clip"), Ui.Kind.Primary, () => _ = AddAsync())), new Thickness(20)));
            return;
        }
        var i = 0;
        var animate = force || !_built;
        _built = true;
        foreach (var s in subs) _list.Children.Add(animate ? Ui.Enter(Card(s), i++ * 40) : Card(s));
    }

    private Control Card(SubItem s)
    {
        SatraState.I.Subs.TryGetValue(s.Id, out var m);
        var title = Ui.CleanName(m?.Title is { Length: > 0 } t ? t : s.Remarks);
        var kids = new List<Control>();
        var enabled = Ui.Switch(s.Enabled, async v => { s.Enabled = v; await ConfigHandler.AddSubItem(AppManager.Instance.Config, s); SatraCore.I.Notify(L.S("reconnect_note")); _ = BuildAsync(true); });

        var cfgCount = SatraCore.I.Profiles.ProfileItems.Count(p => p.Subid == s.Id);
        var updated = m is { Fetched: > 0 }
            ? L.F("updated_ago", RelTime(m.Fetched))
            : L.S("never_updated");
        var subMeta = SatraState.I.Hub.ContainsKey(s.Id)
            ? $"Satra Hub · {L.F("configs_count", cfgCount)} · {updated}"
            : $"{L.F("configs_count", cfgCount)} · {updated}";

        var tile = new Border
        {
            Width = 44, Height = 44, CornerRadius = Ui.RCtl,
            Background = P.B(s.Enabled ? P.AccentSoft : P.Fill),
            Child = new TextBlock
            {
                Text = "▣", FontSize = 20, Foreground = P.B(s.Enabled ? P.Accent : P.Text3),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            },
        };
        kids.Add(Ui.Row(Ui.H(12, tile, Ui.V(3, Ui.T(title, 17, FontWeight.Bold), Ui.Caption(subMeta, P.Text3))), enabled));
        if (_updating.Contains(s.Id))
            kids.Add(new Border { Background = P.B(P.AccentSoft), CornerRadius = Ui.RCtl, Padding = new Thickness(12, 8),
                Child = Ui.H(8, Spinner(P.Accent), Ui.T(L.S("updating"), 13, FontWeight.SemiBold, P.Accent)) });
        else if (m?.Error is { Length: > 0 } err)
            kids.Add(new Border { Background = P.B(P.DangerSoft), CornerRadius = Ui.RCtl, Padding = new Thickness(12, 8),
                Child = Ui.H(8, Ui.T("⚠", 14, FontWeight.Normal, P.Danger), Ui.V(2, Ui.T(L.S("update_failed"), 13, FontWeight.SemiBold, P.Danger),
                    Ui.T($"{err} · {RelTime(m.ErrorAt)}", 12.5, FontWeight.Normal, P.Text2, TextWrapping.Wrap))) });

        if (m != null && (m.Total > 0 || m.Expire > 0 || m.Up + m.Down > 0))
        {
            var used = Math.Max(0, m.Up + m.Down);
            var (frac, data, time) = SubInfo.Describe(m);
            var col = P.Level(frac);
            if (m.Total > 0)
            {
                var left = Math.Max(0, m.Total - used);
                var leftPct = (int)Math.Round(100 * (1 - frac));
                kids.Add(Ui.Bar(frac, col, 10));
                kids.Add(Ui.Row(
                    Ui.V(4,
                        Ui.T(L.F("quota_left", Ui.Bytes(left), Ui.Bytes(m.Total)), 16, FontWeight.Bold, col, TextWrapping.Wrap),
                        Ui.Caption($"{Ui.Bytes(used)} {L.S("used")} · {L.F("quota_pct", Math.Clamp(leftPct, 0, 100))}", P.Text3)),
                    ExpirePill(m)));
            }
            else
            {
                kids.Add(Ui.Row(Ui.T(data, 14, FontWeight.SemiBold, P.Text2, TextWrapping.Wrap), ExpirePill(m)));
            }

            var badges = new List<Control>();
            if (s.AutoUpdateInterval > 0) badges.Add(Ui.Pill(L.S("auto_update_on"), P.Info, P.InfoSoft, 10.5));
            if (!s.Enabled) badges.Add(Ui.Pill(L.S("sub_off"), P.Text3, P.Fill, 10.5));
            if (badges.Count > 0) kids.Add(new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { Ui.H(6, badges.ToArray()) } });

            if (!string.IsNullOrWhiteSpace(m.Announce))
                kids.Add(new Border { Background = P.B(P.InfoSoft), CornerRadius = Ui.RCtl, Padding = new Thickness(12, 8), Child = Ui.H(10, Icons.Make("📣", 15, P.Info), Ui.T(m.Announce, 13, FontWeight.Normal, P.Text, TextWrapping.Wrap)) });
        }
        else
        {
            kids.Add(Ui.Caption(HostOf(s.Url), P.Text3));
        }

        var upd = Ui.Btn(L.S("update"), Ui.Kind.Ghost, () => _ = UpdateOneAsync(s), "⟳", 13);
        upd.IsEnabled = !_updating.Contains(s.Id);
        var actions = new List<Control> { upd,
            Ui.Btn(L.S("edit"), Ui.Kind.Ghost, () => EditSheet(s), "✎", 13),
            Ui.Btn(L.S("delete"), Ui.Kind.Ghost, () => DeleteSheet(s), "🗑", 13) };
        if (m?.RenewUrl is { Length: > 0 } ru) actions.Add(Ui.Btn(L.S("renew"), Ui.Kind.Primary, () => ProcUtils.ProcessStart(ru), size: 13));
        if (m?.SupportUrl is { Length: > 0 } su) actions.Add(Ui.Btn(L.S("support"), Ui.Kind.Tinted, () => ProcUtils.ProcessStart(su), size: 13));
        if (SatraState.I.Hub.ContainsKey(s.Id))
        {
            actions.Add(Ui.Btn(L.S("devices"), Ui.Kind.Ghost, () => _ = DevicesSheet(s.Id), "▢", 13));
            actions.Add(Ui.Btn(L.S("limits"), Ui.Kind.Ghost, () => _ = LimitsSheet(s.Id), "◔", 13));
        }
        else if (m?.HubBase is { Length: > 0 })
        {
            actions.Add(Ui.Btn(L.S("hub_register"), Ui.Kind.Tinted, async () => { await HubSync.EnsureRegisteredAsync(s); _ = BuildAsync(true); }, size: 13));
        }
        kids.Add(new WrapPanel { Children = { Ui.H(8, actions.ToArray()) } });
        return Ui.Card(Ui.V(14, kids.ToArray()), new Thickness(20, 16), s.Enabled ? null : P.Lerp(P.Surface, P.Bg, 0.5));
    }

    private static Border ExpirePill(SubMeta m)
    {
        if (m.Expire <= 0) return Ui.Pill(L.S("no_expiry"), P.Text3, P.Fill, 10.5);
        var exp = SubInfo.ExpireUnixSeconds(m.Expire);
        var left = exp - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var col = left <= 0 ? P.Danger : left < 3 * 86400 ? P.Warning : P.Text2;
        var soft = left <= 0 ? P.DangerSoft : left < 3 * 86400 ? P.WarningSoft : P.Fill;
        var text = left <= 0 ? L.S("expired") : left < 86400 ? L.S("expires_today") : L.F("days_left", Math.Ceiling(left / 86400.0));
        return Ui.Pill(text, col, soft, 10.5);
    }

    private static string RelTime(long unixSec)
    {
        var ago = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSec);
        if (ago.TotalMinutes < 1) return "now";
        if (ago.TotalHours < 1) return $"{(int)ago.TotalMinutes}m";
        if (ago.TotalDays < 1) return $"{(int)ago.TotalHours}h";
        return $"{(int)ago.TotalDays}d";
    }

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "";

    // owner PIN is kept in memory only for this session
    private static readonly Dictionary<string, string> Pins = [];

    private static HubClient? Client(string subId)
    {
        var c = HubSync.ClientFor(subId);
        if (c != null && Pins.TryGetValue(subId, out var p)) c.Pin = p;
        return c;
    }

    private static Task<HubClient?> Owner(string subId) => Task.FromResult(Client(subId));

    /// <summary>Runs a Hub call; if the server asks for the owner PIN, prompts once and retries.</summary>
    private static async Task<T?> Try<T>(HubClient c, Func<Task<T>> f, string? subId = null)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await f(); }
            catch (HubException e) when (e.Code.Contains("pin", StringComparison.OrdinalIgnoreCase))
            {
                if (attempt > 0) SatraCore.I.Notify(L.S("wrong_pin"));
                var pin = await SatraShell.I.Prompt(L.S("pick_pin"), "PIN", secret: true);
                if (string.IsNullOrEmpty(pin)) return default;
                c.Pin = pin;
                var sid = subId ?? SatraState.I.Hub.FirstOrDefault(kv => kv.Value.Token == c.Token).Key;
                if (sid != null) Pins[sid] = pin;
            }
            catch (HubException e) { SatraCore.I.Notify(e.Message); return default; }
            catch (Exception e) { SatraCore.I.Notify(e.Message); return default; }
        }
        return default;
    }

    private async Task DevicesSheet(string subId)
    {
        var c = Client(subId);
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
        var c = Client(subId);
        if (c == null) return;
        var old = await SatraShell.I.Prompt(L.S("pick_pin") + " (old / empty)", "PIN", true);
        var nw = await SatraShell.I.Prompt(L.S("set_pin"), "PIN", true);
        if (string.IsNullOrEmpty(nw) || nw.Length < 4) return;
        if (await Try(c, async () => { await c.SetPin(old, nw); return true; })) { c.Pin = nw; SatraCore.I.Notify("PIN · OK"); }
    }

    private async Task LimitsSheet(string subId, string? deviceId = null, string? deviceName = null)
    {
        var c = Client(subId);
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
                    Ui.Caption($"{l["match"]?["type"]} · {valsTxt} · {PeriodText(l["period"]?.ToString())}" + (l["scope"]?.ToString() == "device" ? " · " + (l["deviceId"]?.ToString() == SatraState.I.Hub[subId].DeviceId ? L.S("this_device") : L.S("devices") + " " + l["deviceId"]) : ""), P.Text3)),
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
        var form = Ui.Card(Ui.V(10, Ui.Section(L.S("add_limit")), Ui.H(8, type, per, mb, Mb()), vals, add), new Thickness(14), P.Raised);
        var title = L.S("server_limits") + (deviceName != null ? " · " + deviceName : "");
        close = SatraShell.I.Sheet(title, Ui.V(14, Ui.Group(rows.ToArray()), form), 620);
    }

    public static string PeriodText(string? p) => p switch { "weekly" => L.S("per_week"), "monthly" => L.S("per_month"), "total" => L.S("total"), _ => L.S("per_day") };

    private static TextBlock Mb()
    {
        var t = Ui.T("MB", 13, FontWeight.Medium, P.Text2);
        t.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        return t;
    }

    /// Mixed-direction line built from isolated runs so numbers never reorder inside Persian text.
    private static Control DataLine(SubMeta m)
    {
        Control Run(string t, bool num = false) => Ui.T(num ? Ui.Ltr(t) : t, 13, FontWeight.Medium, P.Text2);
        var used = m.Up + m.Down;
        return m.Total > 0
            ? Ui.H(5, Run(Ui.Bytes(Math.Max(0, m.Total - used)), true), Run(L.S("remaining")), Run("·"), Run($"{Ui.Bytes(used).Trim('\u200E')} / {Ui.Bytes(m.Total).Trim('\u200E')}", true))
            : Ui.H(5, Run(Ui.Bytes(used), true), Run("·"), Run(L.S("unlimited")));
    }
}
