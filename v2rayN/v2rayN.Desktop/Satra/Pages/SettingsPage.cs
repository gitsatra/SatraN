using Avalonia.Layout;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Settings: simple by default (mode, routing, language, device); "Advanced" reveals the full engine settings.</summary>
public class SettingsPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 10, Margin = new Thickness(0, 18, 0, 28), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    public Control View => _root;
    private static Config Cfg => AppManager.Instance.Config;

    public SettingsPage()
    {
        _root.Children.Add(Ui.Title(L.S("settings")));
        var sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
    }

    private static Control Segmented(string[] labels, int selected, Action<int> pick)
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

    public void Refresh()
    {
        var st = SatraState.I;
        _body.Children.Clear();
        var connected = SatraCore.I.Phase == ConnPhase.On;

        // connection mode
        var modes = new[] { "proxy", "tun", "socks" };
        var modeIdx = Math.Max(0, Array.IndexOf(modes, st.Mode));
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var modeKids = new List<Control>
        {
            Segmented([L.S("mode_proxy"), L.S("mode_tun"), L.S("mode_proxy_only")], modeIdx, i =>
            {
                if (st.Mode == modes[i]) return;
                st.Mode = modes[i];
                SatraState.Save();
                if (SatraCore.I.Phase == ConnPhase.On) SatraCore.I.Notify(L.S("reconnect_note"));
                Refresh();
            }),
        };
        if (st.Mode == "socks")
            modeKids.Add(new Border { Background = P.B(P.InfoSoft), CornerRadius = Ui.RCtl, Padding = new Thickness(12, 8), Child = Ui.T(L.F("proxy_addr", $"{Global.Loopback}:{port}", $"{Global.Loopback}:{port}"), 13, FontWeight.Medium, P.Info, TextWrapping.Wrap) });
        if (st.Mode == "tun" && !Utils.IsWindows())
            modeKids.Add(Ui.Caption("TUN needs administrator rights (you may be asked for your password).", P.Text3));
        if (connected) modeKids.Add(Ui.Caption(L.S("reconnect_note"), P.Warning));
        _body.Children.Add(Ui.Section(L.S("mode")));
        _body.Children.Add(Ui.Card(Ui.V(10, modeKids.ToArray()), new Thickness(14)));

        // routing (Iran / Russia only)
        _body.Children.Add(Ui.Section(L.S("routing")));
        var routes = new[] { "iran", "russia" };
        _body.Children.Add(Ui.Card(Segmented([L.S("routing_iran"), L.S("routing_russia")], Math.Max(0, Array.IndexOf(routes, st.Routing)), i =>
        {
            st.Routing = routes[i];
            SatraState.Save();
            _ = SatraCore.I.Main.ApplyRegionalPreset(i == 0 ? EPresetType.Iran : EPresetType.Russia);
            SatraCore.I.Notify(L.S("routing") + " ✓");
            Refresh();
        }), new Thickness(14)));

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
            Ui.ListRow("SatraN", $"v{Utils.GetVersionInfo()} · Xray · {Identity.Os}", Ui.Btn("Exit", Ui.Kind.Danger, () => _ = AppManager.Instance.AppExitAsync(true), size: 13), glyph: "S", glyphColor: P.Accent)));
    }
}
