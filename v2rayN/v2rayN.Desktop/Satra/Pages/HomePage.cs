using Avalonia.Layout;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Home: connect orb, mode selector, smart-routing toggle, active server, live speed.</summary>
public class HomePage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 28), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
    private readonly ContentControl _modeHost = new() { MaxWidth = 640, Margin = new Thickness(0, 0, 0, 10) };
    private readonly ContentControl _routeHost = new() { MaxWidth = 640, Margin = new Thickness(0, 0, 0, 14) };
    private string _modeSig = "";
    private string _routeSig = "";
    private readonly ConnectOrb _orb = new() { Width = 168, Height = 168, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = Ui.T("", 26, FontWeight.Bold);
    private readonly TextBlock _hint = Ui.T("", 13.5, FontWeight.Normal, P.Text2);
    private readonly TextBlock _timer = Ui.T("", 13.5, FontWeight.Medium, P.Text2);
    private readonly ContentControl _server = new();
    private readonly TextBlock _down = Ui.Num(Ui.T("0 B/s", 22, FontWeight.Bold)), _up = Ui.Num(Ui.T("0 B/s", 22, FontWeight.Bold));
    private readonly TextBlock _downTot = Ui.Caption(""), _upTot = Ui.Caption("");
    private readonly TextBlock _today = Ui.Num(Ui.T("", 15, FontWeight.SemiBold));
    private readonly Sparkline _spark = new() { Height = 54 };
    private readonly Border _modeChip = new();
    private string _lastServerKey = "";

    public Control View => _root;

    public HomePage()
    {
        var head = Ui.Row(Ui.Title(L.S("home")), Ui.H(8,
            Ui.IconBtn("⟳", () => _ = SatraCore.I.UpdateSubsAsync(), L.S("update_all")),
            Ui.Btn(L.S("add"), Ui.Kind.Tinted, () => _ = AddAsync(), "+")));
        _root.Children.Add(head);

        _status.HorizontalAlignment = _hint.HorizontalAlignment = _timer.HorizontalAlignment = HorizontalAlignment.Center;
        _orb.Clicked += () => _ = SatraCore.I.ToggleAsync();
        var center = Ui.V(10, _orb, _status, _timer, _hint);
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(center, 1);
        _root.Children.Add(center);

        Grid.SetRow(_modeHost, 2);
        _root.Children.Add(_modeHost);
        Grid.SetRow(_routeHost, 3);
        _root.Children.Add(_routeHost);
        _server.Margin = new Thickness(0, 0, 0, 14);
        _server.MaxWidth = 640;
        Grid.SetRow(_server, 4);
        _root.Children.Add(_server);

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        Control Stat(string label, TextBlock big, TextBlock small, Color dot)
        {
            var d = new Border { Width = 8, Height = 8, CornerRadius = Ui.RPill, Background = P.B(dot), VerticalAlignment = VerticalAlignment.Center };
            return Ui.V(4, Ui.H(6, d, Ui.Section(label)), big, small);
        }
        var c1 = Stat(L.S("download"), _down, _downTot, P.Success);
        var c2 = Stat(L.S("upload"), _up, _upTot, P.Accent);
        var c3 = Ui.V(4, Ui.Section(L.S("today")), _today, _modeChip);
        Grid.SetColumn(c2, 1);
        Grid.SetColumn(c3, 2);
        stats.Children.Add(c1);
        stats.Children.Add(c2);
        stats.Children.Add(c3);
        var statCard = Ui.Card(Ui.V(12, stats, _spark), new Thickness(20, 16));
        statCard.MaxWidth = 640;
        Grid.SetRow(statCard, 5);
        _root.Children.Add(statCard);

        SatraCore.I.Speed += (_, _) => Dispatcher.UIThread.Post(UpdateSpeed);
        SatraCore.I.Ticked += () => Dispatcher.UIThread.Post(() => { var c = SatraCore.I; _timer.Text = c.Since is { } s ? (DateTime.Now - s).ToString(@"hh\:mm\:ss") : ""; });
    }

    private async Task AddAsync() => await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());

    private void UpdateSpeed()
    {
        var c = SatraCore.I;
        _down.Text = Ui.Speed(c.DownRate);
        _up.Text = Ui.Speed(c.UpRate);
        _downTot.Text = $"{L.S("session")} {Ui.Bytes(c.SessionDown)}";
        _upTot.Text = $"{L.S("session")} {Ui.Bytes(c.SessionUp)}";
        var d = SatraState.I.Today();
        _today.Text = Ui.Ltr($"↓ {Ui.Bytes(d.Down)}  ↑ {Ui.Bytes(d.Up)}  Σ {Ui.Bytes(d.Down + d.Up)}");
        _spark.LineColor = c.Phase == ConnPhase.On ? P.Success : P.Accent;
        _spark.SetData(c.History);
    }

    public void Refresh()
    {
        var c = SatraCore.I;
        _orb.Phase = c.Phase;
        (_status.Text, _hint.Text, var col) = c.Phase switch
        {
            ConnPhase.On => (L.S("connected"), L.S("tap_disconnect"), P.Success),
            ConnPhase.Connecting => (L.S("connecting"), SatraState.I.SmartConnect ? L.S("smart_desc") : "", P.Accent),
            ConnPhase.Disconnecting => (L.S("disconnecting"), "", P.Text2),
            ConnPhase.Failed => (L.S("failed"), L.S("retry"), P.Danger),
            _ => (L.S("disconnected"), L.S("tap_connect"), P.Text),
        };
        _status.Foreground = P.B(col);
        _timer.Text = c.Since is { } s ? (DateTime.Now - s).ToString(@"hh\:mm\:ss") : "";
        _timer.IsVisible = c.Phase == ConnPhase.On;

        var mode = SatraState.I.Mode switch { "tun" => L.S("mode_vpn"), "socks" => L.S("mode_proxy_only"), _ => L.S("mode_proxy") };
        _modeChip.Child = Ui.T(mode, 10.5, FontWeight.SemiBold, P.Text2);
        _modeChip.Background = P.B(P.Fill);
        _modeChip.CornerRadius = Ui.RPill;
        _modeChip.Padding = new Thickness(8, 2);
        _modeChip.HorizontalAlignment = HorizontalAlignment.Left;

        var msig = $"{SatraState.I.Mode}{c.Phase == ConnPhase.On}{L.Lang}";
        if (msig != _modeSig) { _modeSig = msig; _modeHost.Content = ModeSelector(); }

        var rsig = $"{SatraState.I.SmartRouting}{SatraState.I.Routing}{L.Lang}";
        if (rsig != _routeSig) { _routeSig = rsig; _routeHost.Content = SmartRoutingCard(); }

        var a = c.Active;
        var key = $"{a?.IndexId}|{a?.Delay}|{(a != null ? NetBadges.ExitCc(a) : null)}|{SatraState.I.SmartConnect}";
        if (key != _lastServerKey)
        {
            _lastServerKey = key;
            _server.Content = ServerCard(a);
        }
        UpdateSpeed();
    }

    private static readonly string[] Modes = ["tun", "proxy", "socks"];

    /// VPN (whole device) · System proxy · Proxy only. Changing it while connected reconnects automatically.
    private Control ModeSelector()
    {
        var st = SatraState.I;
        var seg = SettingsPage.Segmented([L.S("mode_vpn"), L.S("mode_proxy"), L.S("mode_proxy_only")], Math.Max(0, Array.IndexOf(Modes, st.Mode)), i =>
        {
            if (st.Mode == Modes[i]) return;
            st.Mode = Modes[i];
            SatraState.Save();
            _modeSig = "";
            Refresh();
            if (SatraCore.I.Phase == ConnPhase.On) _ = SatraCore.I.ReconnectAsync();
        });
        var hint = Ui.Caption(L.S("mode_hint"), P.Text3);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.MaxWidth = 620;
        hint.Margin = new Thickness(2, 0, 2, 0);
        if (st.Mode != "socks") return Ui.V(6, seg, hint);
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var http = port + 1; // mixed HTTP is LocalPort+1 (same as Android pairing)
        var addr = $"{Global.Loopback}:{port}";
        var chip = Ui.Btn(L.F("proxy_addr", addr, $"{Global.Loopback}:{http}"), Ui.Kind.Ghost, () => _ = SatraShell.I.Copy(addr), size: 12.5);
        chip.HorizontalAlignment = HorizontalAlignment.Center;
        return Ui.V(6, seg, chip, hint);
    }

    /// Prominent smart-routing toggle: local sites direct while VPN stays on; off = full tunnel.
    private Control SmartRoutingCard()
    {
        var st = SatraState.I;
        var region = st.Routing == "russia" ? L.S("routing_russia") : L.S("routing_iran");
        var title = $"{L.S("smart_routing")} ({region})";
        var note = st.SmartRouting ? L.S("smart_routing_d") : L.S("smart_routing_off_d");
        var sw = Ui.Switch(st.SmartRouting, v =>
        {
            st.SmartRouting = v;
            SatraState.Save();
            if (v)
            {
                _ = SatraCore.I.Main.ApplyRegionalPreset(st.Routing == "russia" ? EPresetType.Russia : EPresetType.Iran);
            }
            _ = SatraCore.I.ApplyLocalLimitsAsync();
            _routeSig = "";
            Refresh();
        });
        var row = Ui.ListRow(title, note, sw, glyph: "⇄", glyphColor: st.SmartRouting ? P.Accent : P.Text3);
        return Ui.Enter(Ui.Group(row));
    }

    private Control ServerCard(ProfileItemModel? a)
    {
        if (a == null)
        {
            return Ui.Card(Ui.Row(Ui.V(4, Ui.T(L.S("no_server"), 16, FontWeight.SemiBold), Ui.Caption(L.S("add_hint"))),
                Ui.Btn(L.S("add_clip"), Ui.Kind.Primary, () => _ = AddAsync())), new Thickness(18, 14));
        }
        var cc = NetBadges.ExitCc(a);
        var flag = NetBadges.FlagView(cc, 26);
        var sub = ServersPage.SubName(a.Subid);
        var meta = new List<Control>();
        if (!string.IsNullOrEmpty(sub)) meta.Add(Ui.Caption(sub, P.Text3));
        if (SatraState.I.SmartConnect) meta.Add(Ui.Pill(L.S("smart"), P.Accent, P.AccentSoft, 10.5));
        var left = Ui.H(12, flag, Ui.V(3, Ui.T(Ui.CleanName(a.Remarks), 15.5, FontWeight.SemiBold), Ui.H(8, meta.ToArray())));
        var ip = SatraState.I.Geo.TryGetValue(NetBadges.GeoKey(a), out var g) ? g.Ip : "";
        if (SatraCore.I.Phase == ConnPhase.On && ip.Length > 0)
            meta.Add(Ui.Btn(Ui.Ltr(ip) + "  ⧉", Ui.Kind.Ghost, () => _ = SatraShell.I.Copy(ip), size: 11));
        var q = QuotaPill(a.Subid);
        if (q != null) meta.Add(q);
        var right = Ui.H(10, ServersPage.PingPill(ServersPage.Delay(a)), Ui.Btn(L.S("servers"), Ui.Kind.Ghost, () => SatraShell.I.Go("servers"), size: 13));
        return Ui.Enter(Ui.Card(Ui.Row(left, right), new Thickness(18, 12)));
    }

    private static Control? QuotaPill(string? subId)
    {
        if (subId == null || !SatraState.I.Subs.TryGetValue(subId, out var m) || (m.Total <= 0 && m.Expire <= 0)) return null;
        var (frac, data, time) = SubInfo.Describe(m);
        var col = P.Level(frac);
        return Ui.Pill(string.Join(" · ", new[] { data, time }.Where(x => !string.IsNullOrEmpty(x))), col, P.Lerp(P.Surface, col, 0.15), 10.5);
    }
}
