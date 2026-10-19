using Avalonia.Layout;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Settings: simple by default (mode, routing, language, device); "Advanced" reveals the full engine settings.</summary>
public class SettingsPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 10, Margin = new Thickness(0, 18, 0, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    public Control View => _root;
    private bool _modeChanged;
    private static Config Cfg => AppManager.Instance.Config;

    public SettingsPage()
    {
        _root.Children.Add(Ui.Title(L.S("settings")));
        var sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
    }

    public static Control Segmented(string[] labels, int selected, Action<int> pick)
    {
        var row = new UniformGrid { Rows = 1 };
        var host = new Border { Background = P.B(P.Fill), CornerRadius = Ui.RCtl, Padding = new Thickness(3), Child = row };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var on = i == selected;
            var b = Ui.Pressable(new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 8), Cursor = new Cursor(StandardCursorType.Hand),
                Background = on ? P.B(P.Raised) : Brushes.Transparent,
                Child = new TextBlock { Text = labels[i], FontSize = 13, FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium, Foreground = P.B(on ? P.Text : P.Text2), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
            });
            b.PointerReleased += (_, _) => pick(idx);
            row.Children.Add(b);
        }
        return host;
    }

    private string _sig = "";

    public void Refresh() => Build(false);

    private void Build(bool force)
    {
        var st = SatraState.I;
        var sig = $"{SatraCore.I.Phase == ConnPhase.On}{st.Mode}{st.Routing}{st.Advanced}{L.Lang}{_modeChanged}{st.SmartConnect}{st.AutoUpdate}";
        if (!force && sig == _sig) return;
        _sig = sig;
        _body.Children.Clear();
        var connected = SatraCore.I.Phase == ConnPhase.On;

        // routing (Iran / Russia only)
        _body.Children.Add(Ui.Section(L.S("routing")));
        var routes = new[] { "iran", "russia" };
        _body.Children.Add(Ui.Card(Segmented([L.S("routing_iran"), L.S("routing_russia")], Math.Max(0, Array.IndexOf(routes, st.Routing)), i =>
        {
            st.Routing = routes[i];
            SatraState.Save();
            _ = SatraCore.I.Main.ApplyRegionalPreset(i == 0 ? EPresetType.Iran : EPresetType.Russia);
            SatraCore.I.Notify(L.S("routing") + " · OK");
            Refresh();
        }), new Thickness(14)));
        _body.Children.Add(Ui.Group(
            Ui.ListRow(L.S("smart_routing"), L.S("smart_routing_d"), Ui.Switch(st.SmartRouting, v => { st.SmartRouting = v; SatraState.Save(); _ = SatraCore.I.ApplyLocalLimitsAsync(); })),
            Ui.ListRow(L.S("exceptions"), L.F("exceptions_d", st.ViaVpn.Count, st.Direct.Count), null, ExceptionsSheet, "⇄"),
            Ui.ListRow(L.S("kill_switch"), L.S("kill_switch_d"), Ui.Switch(st.KillSwitch, v => { st.KillSwitch = v; SatraState.Save(); }))));

        // general
        _body.Children.Add(Ui.Section(L.S("settings")));
        var langs = new[] { "en", "fa", "ru" };
        var lang = Segmented(["English", "فارسی", "Русский"], Math.Max(0, Array.IndexOf(langs, L.Lang)), i =>
        {
            if (Cfg.UiItem.CurrentLanguage == langs[i]) return;
            Cfg.UiItem.CurrentLanguage = langs[i];
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(langs[i]);
            _ = ConfigHandler.SaveConfig(Cfg);
            if (SatraShell.I.Parent is ContentControl cc)
            {
                var win = SatraShell.I.Window;
                cc.Content = null;
                cc.Content = new SatraShell(win); // rebuild UI in the new language / direction
                SatraShell.I.Go("settings");
            }
        });
        lang.Width = 300;
        var devName = Ui.Input(L.S("this_device"), st.DeviceName);
        devName.Width = 220;
        devName.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(devName.Text) || devName.Text == st.DeviceName) return;
            st.DeviceName = devName.Text.Trim();
            SatraState.Save();
            Identity.Apply();
        };
        _body.Children.Add(Ui.Group(
            Ui.ListRow(L.S("language"), null, lang, glyph: "文"),
            Ui.ListRow(L.S("this_device"), "Satra Hub", devName, glyph: "▢"),
            Ui.ListRow(L.S("smart"), L.S("smart_desc"), Ui.Switch(st.SmartConnect, v => { st.SmartConnect = v; SatraState.Save(); }), glyph: "⚡", glyphColor: P.Accent),
            Ui.ListRow(L.S("auto_update"), "15 min", Ui.Switch(st.AutoUpdate, v => { st.AutoUpdate = v; SatraState.Save(); }), glyph: "⟳"),
            Ui.ListRow(L.S("autostart"), null, Ui.Switch(Cfg.GuiItem.AutoRun, async v => { Cfg.GuiItem.AutoRun = v; await AutoStartupHandler.UpdateTask(Cfg); await ConfigHandler.SaveConfig(Cfg); }), glyph: "⏻"),
            Ui.ListRow(L.S("show_advanced"), L.S("simple_mode_hint"), Ui.Switch(st.Advanced, v => { st.Advanced = v; SatraState.Save(); Refresh(); }), glyph: "⚙")));

        if (st.Advanced)
        {
            var m = SatraCore.I.Main;
            Control Open(string title, string glyph, ReactiveCommand<RxVoid, RxVoid> cmd) =>
                Ui.ListRow(title, null, Ui.T("›", 18, FontWeight.Bold, P.Text3), () => cmd.Execute().Subscribe(_ => { }), glyph);
            _body.Children.Add(Ui.Section(L.S("advanced")));
            _body.Children.Add(Ui.Enter(Ui.Group(
                Open(L.S("core_settings"), "⚙", m.OptionSettingCmd),
                Open(L.S("dns"), "◎", m.DNSSettingCmd),
                Open(L.S("routing_rules"), "⇄", m.RoutingSettingCmd),
                Open(L.S("sub_settings"), "▣", m.SubSettingCmd),
                Open(L.S("logs"), "▤", m.OpenTheFileLocationCmd))));
        }

        _body.Children.Add(Ui.Section(L.S("about")));
        _body.Children.Add(Ui.Group(
            Ui.ListRow("SatraN", $"v{Identity.Version} · Xray · v2rayN {Utils.GetVersionInfo()} · {Identity.Os}", Ui.Btn("Exit", Ui.Kind.Danger, () => _ = AppManager.Instance.AppExitAsync(true), size: 13), glyph: "S", glyphColor: P.Accent)));
    }

    private void ExceptionsSheet()
    {
        var st = SatraState.I;
        var via = Ui.Input("youtube.com, x.com");
        via.Text = string.Join("\n", st.ViaVpn); via.AcceptsReturn = true; via.Height = 110;
        var dir = Ui.Input("bank.ir, digikala.com");
        dir.Text = string.Join("\n", st.Direct); dir.AcceptsReturn = true; dir.Height = 110;
        Action close = () => { };
        static List<string> Parse(string? t) => (t ?? "").Split(['\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct().ToList();
        var save = Ui.Btn(L.S("save"), Ui.Kind.Primary, () =>
        {
            st.ViaVpn = Parse(via.Text); st.Direct = Parse(dir.Text);
            SatraState.Save();
            _ = SatraCore.I.ApplyLocalLimitsAsync();
            close();
            Refresh();
        });
        close = SatraShell.I.Sheet(L.S("exceptions"), Ui.V(10, Ui.Section(L.S("always_vpn")), via, Ui.Section(L.S("always_direct")), dir, Ui.Caption(L.S("exceptions_hint"), P.Text3), save), 480);
    }
}
