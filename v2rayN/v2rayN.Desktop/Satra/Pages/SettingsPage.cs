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
        var ib0 = Cfg.Inbound.First();
        var lanOn0 = ib0.AllowLANConn && ib0.NewPort4LAN;
        var sig = $"{SatraCore.I.Phase == ConnPhase.On}{st.Mode}{st.Routing}{st.SmartRouting}{st.Advanced}{L.Lang}{_modeChanged}{st.SmartConnect}{st.AutoUpdate}{lanOn0}{st.ViaVpn.Count}{st.Direct.Count}";
        if (!force && sig == _sig) return;
        _sig = sig;
        _body.Children.Clear();

        // Region for smart routing (toggle itself lives on Home — keep this findable in Settings too)
        _body.Children.Add(Ui.Section(L.S("routing")));
        var routes = new[] { "iran", "russia", "china" };
        _body.Children.Add(Ui.Card(Ui.V(10,
            Ui.Caption(L.S("routing_region_d"), P.Text3),
            Segmented([L.S("routing_iran"), L.S("routing_russia"), L.S("routing_china")], Math.Max(0, Array.IndexOf(routes, st.Routing)), i =>
            {
                st.Routing = routes[i];
                SatraState.Save();
                _ = LocalRules.ApplyDirectDnsAsync(routes[i]);
                if (st.SmartRouting) _ = SatraCore.I.Main.ApplyRegionalPreset(LocalRules.Preset(routes[i]));
                SatraCore.I.Notify(L.S("routing") + " · OK");
                Refresh();
            })), new Thickness(14)));
        _body.Children.Add(Ui.Group(
            Ui.ListRow(L.S("smart_routing"), st.SmartRouting ? L.S("smart_routing_d") : L.S("smart_routing_off_d"),
                Ui.Switch(st.SmartRouting, v =>
                {
                    st.SmartRouting = v;
                    SatraState.Save();
                    if (v) _ = SatraCore.I.Main.ApplyRegionalPreset(LocalRules.Preset(st.Routing));
                    _ = SatraCore.I.ApplyLocalLimitsAsync();
                    Refresh();
                })),
            Ui.ListRow(L.S("kill_switch"), L.S("kill_switch_d"), Ui.Switch(st.KillSwitch, v => { st.KillSwitch = v; SatraState.Save(); }))));

        // Share VPN with phones/PCs on the same Wi-Fi or hotspot
        var ib = Cfg.Inbound.First();
        var lanOn = ib.AllowLANConn && ib.NewPort4LAN;
        _body.Children.Add(Ui.Section(L.S("lan_share")));
        var lanRows = new List<Control> { Ui.ListRow(L.S("lan_share"), L.S("lan_share_d"), Ui.Switch(lanOn, v => _ = SetLanAsync(v))) };
        if (lanOn) lanRows.Add(Ui.ListRow(L.S("lan_details"), $"{LanIp()}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}", null, LanSheet, "⇄"));
        _body.Children.Add(Ui.Group(lanRows.ToArray()));

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
                cc.Content = new SatraShell(win);
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
            Ui.ListRow(L.S("updates"), L.S("updates_d"), Ui.T("›", 18, FontWeight.Bold, P.Text3), UpdatesSheet, "↓"),
            Ui.ListRow(L.S("show_advanced"), L.S("simple_mode_hint"), Ui.Switch(st.Advanced, v => { st.Advanced = v; SatraState.Save(); Refresh(); }), glyph: "⚙")));

        if (st.Advanced)
        {
            var m = SatraCore.I.Main;
            Control Open(string title, string glyph, ReactiveCommand<RxVoid, RxVoid> cmd) =>
                Ui.ListRow(title, null, Ui.T("›", 18, FontWeight.Bold, P.Text3), () => cmd.Execute().Subscribe(_ => { }), glyph);
            _body.Children.Add(Ui.Section(L.S("advanced")));
            _body.Children.Add(Ui.Enter(Ui.Group(
                Ui.ListRow(L.S("exceptions"), L.F("exceptions_d", st.ViaVpn.Count, st.Direct.Count), null, ExceptionsSheet, "⇄"),
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

    private void UpdatesSheet()
    {
        var kinds = new[] { AssetUpdates.Kind.Geo, AssetUpdates.Kind.Rules, AssetUpdates.Kind.Xray, AssetUpdates.Kind.SingBox };
        var names = new[] { L.S("asset_geo"), L.S("asset_rules"), "Xray", "sing-box" };
        var list = new StackPanel { Spacing = 0 };
        var appRow = new ContentControl();
        var busy = false;
        Action refreshAll = () => { };

        async Task<Control> RowAsync(int i, bool online)
        {
            var info = await AssetUpdates.InfoAsync(kinds[i], online);
            var status = Ui.Caption(Line(info, online), P.Text3);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, IsVisible = false };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            async Task Do(Func<Task<string?>> act, string doing)
            {
                if (busy) return;
                busy = true;
                status.Text = doing; status.Foreground = P.B(P.Accent); bar.IsVisible = true; bar.IsIndeterminate = true;
                var err = await act();
                busy = false;
                bar.IsVisible = false;
                SatraCore.I.Notify(err == null ? names[i] + " · " + L.S("done") : names[i] + " · " + err);
                refreshAll();
            }
            var prog = new Progress<(long got, long? total)>(p => Dispatcher.UIThread.Post(() =>
            {
                if (p.total is > 0) { bar.IsIndeterminate = false; bar.Value = 100.0 * p.got / p.total.Value; }
            }));
            if (!online || AssetUpdates.IsNewer(info))
                btns.Children.Add(Ui.Btn(online ? L.S("update") : L.S("check"), online ? Ui.Kind.Primary : Ui.Kind.Tinted,
                    () => _ = online ? Do(() => AssetUpdates.UpdateAsync(kinds[i], prog), L.S("updating")) : Task.Run(() => Dispatcher.UIThread.Post(() => refreshAll())), null, 12));
            if (info.CanRollback)
                btns.Children.Add(Ui.Btn(L.S("rollback"), Ui.Kind.Ghost, () => _ = Do(() => AssetUpdates.RollbackAsync(kinds[i]), L.S("rolling_back")), "↶", 12));
            return new Border { Padding = new Thickness(14, 10), Child = Ui.V(6, Ui.Row(Ui.V(2, Ui.T(names[i], 14, FontWeight.SemiBold), status), btns), bar) };
        }

        static string Line(AssetUpdates.Info info, bool online)
        {
            var cur = info.Current + (info.UpdatedAt is { } at ? " · " + at.ToString("yyyy-MM-dd HH:mm") : "");
            if (!online) return cur;
            if (info.Latest == null) return cur + " · " + L.S("asset_err_check");
            return AssetUpdates.IsNewer(info) ? cur + " → " + info.Latest : cur + " · " + L.S("up_to_date");
        }

        async Task FillAsync(bool online)
        {
            var appT = SatraAppUpdate.CheckAsync();
            var rows = await Task.WhenAll(Enumerable.Range(0, kinds.Length).Select(i => Dispatcher.UIThread.InvokeAsync(() => RowAsync(i, online))));
            var app = await appT;
            Dispatcher.UIThread.Post(() =>
            {
                list.Children.Clear();
                var first = true;
                foreach (var r in rows)
                {
                    if (!first) list.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
                    first = false;
                    list.Children.Add(r);
                }
                appRow.Content = Ui.Row(Ui.V(2, Ui.T("SatraN", 14, FontWeight.SemiBold),
                        Ui.Caption("v" + Identity.Version + " · " + (app.HasUpdate ? "→ " + app.Version : app.Version != null ? L.S("up_to_date") : L.S("asset_err_check"))
                                   + (SatraAppUpdate.IsDebInstall ? " · apt" : ""), P.Text3)),
                    app.HasUpdate ? Ui.Btn(L.S("update"), Ui.Kind.Primary, () => SatraShell.I.Go("home"), null, 12) : new Control());
            });
        }

        refreshAll = () => _ = FillAsync(true);
        list.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("checking"), P.Text3) });
        _ = FillAsync(true);
        var updAll = Ui.Btn(L.S("update_all"), Ui.Kind.Tinted, async () =>
        {
            if (busy) return;
            busy = true;
            var errs = new List<string>();
            foreach (var k in kinds)
            {
                var info = await AssetUpdates.InfoAsync(k, true);
                if (!AssetUpdates.IsNewer(info)) continue;
                SatraCore.I.Notify(L.S("updating") + " " + names[Array.IndexOf(kinds, k)]);
                if (await AssetUpdates.UpdateAsync(k, null) is { } e) errs.Add(names[Array.IndexOf(kinds, k)] + ": " + e);
            }
            busy = false;
            SatraCore.I.Notify(errs.Count == 0 ? L.S("update_all") + " · " + L.S("done") : string.Join(" · ", errs));
            refreshAll();
        }, "⟳", 13);
        SatraShell.I.Sheet(L.S("updates"), Ui.V(12,
            Ui.Card(appRow, new Thickness(14, 10)),
            Ui.Row(Ui.Section(L.S("asset_section")), updAll),
            new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = list },
            Ui.Caption(L.S("updates_hint"), P.Text3)), 520);
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

    public static string LanIp()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                if (ni.Name.Contains("tun", StringComparison.OrdinalIgnoreCase) || ni.Name.StartsWith("singbox")) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    var ip = a.Address.ToString();
                    if (a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))) return ip;
                }
            }
        }
        catch { }
        return "0.0.0.0";
    }

    private async Task SetLanAsync(bool on)
    {
        var ib = Cfg.Inbound.First();
        ib.AllowLANConn = on;
        ib.NewPort4LAN = on;
        if (on && (string.IsNullOrEmpty(ib.User) || string.IsNullOrEmpty(ib.Pass)))
        {
            ib.User = "satra";
            ib.Pass = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(9)).Replace('+', 'x').Replace('/', 'y');
        }
        await ConfigHandler.SaveConfig(Cfg);
        await SatraCore.I.Main.Reload();
        Refresh();
        if (on) LanSheet();
    }

    private void LanSheet()
    {
        var ib = Cfg.Inbound.First();
        var ip = LanIp();
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks3);
        var url = $"socks5://{ib.User}:{ib.Pass}@{ip}:{port}";
        Control Field(string label, string value) => Ui.Row(Ui.V(2, Ui.Section(label), Ui.T(Ui.Ltr(value), 14, FontWeight.SemiBold)), Ui.IconBtn("⧉", () => _ = SatraShell.I.Copy(value), L.S("copied"), P.Text2, 14));
        var kids = new List<Control>
        {
            Ui.Caption(L.S("lan_how")),
            Field(L.S("lan_address"), ip), Field(L.S("lan_port"), $"{port}  (SOCKS5 + HTTP)"), Field(L.S("lan_user"), ib.User), Field(L.S("lan_pass"), ib.Pass),
        };
        try
        {
            var png = QRCodeUtils.GenQRCode(url);
            if (png != null)
            {
                var img = new Image { Source = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png)), Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center };
                kids.Insert(1, new Border { Background = Brushes.White, CornerRadius = Ui.RCtl, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, Child = img });
            }
        }
        catch { }
        if (OperatingSystem.IsWindows())
            kids.Add(Ui.Btn(L.S("lan_hotspot"), Ui.Kind.Tinted, () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:network-mobilehotspot") { UseShellExecute = true }); } catch { } }, size: 13));
        kids.Add(Ui.Caption(L.S("lan_note"), P.Text3));
        SatraShell.I.Sheet(L.S("lan_share"), Ui.V(12, kids.ToArray()), 440);
    }
}
