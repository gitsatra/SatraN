using Avalonia.Layout;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Home (v1.8): one power button (core on/off), a 3-way "how apps use it" choice (ports / system proxy /
/// whole device), the active server with the fastest alternatives one click away, live speed.</summary>
public class HomePage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 28), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
    private readonly ContentControl _modeHost = new() { MaxWidth = 640, Margin = new Thickness(0, 0, 0, 18) };
    private readonly ContentControl _routeHost = new() { MaxWidth = 640 };
    private string _modeSig = "";
    private string _routeSig = "";
    private readonly ConnectOrb _orb = new() { Width = 136, Height = 136, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
    private readonly PowerSwitch _power = new();
    private readonly ContentControl _powerPing = new() { VerticalAlignment = VerticalAlignment.Center, Width = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _modesStrip = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
    private string _stripSig = "";
    private ConnPhase _lastPhase = ConnPhase.Off;
    private Border? _secBtn;
    private readonly TextBlock _status = Ui.T("", 24, FontWeight.Bold);
    private readonly ContentControl _ports = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private string _portsSig = "";
    private readonly TextBlock _hint = Ui.T("", 13.5, FontWeight.Normal, P.Text2);
    private readonly TextBlock _timer = Ui.T("", 13.5, FontWeight.Medium, P.Text2);
    private readonly ContentControl _server = new();
    private ContentControl _homePing = new();
    private readonly ContentControl _speedHost = new();
    private readonly TextBlock _down = Ui.Num(Ui.T("0 B/s", 22, FontWeight.Bold)), _up = Ui.Num(Ui.T("0 B/s", 22, FontWeight.Bold));
    private readonly TextBlock _downTot = Ui.Caption(""), _upTot = Ui.Caption("");
    private readonly TextBlock _today = Ui.Num(Ui.T("", 15, FontWeight.SemiBold));
    private readonly Sparkline _spark = new() { Height = 54 };
    private readonly Border _modeChip = new();
    private string _lastServerKey = "";
    private string? _pingTargetId;
    private readonly Border _updateBtnHost = new() { IsVisible = false };
    private StackPanel _center = new();
    private readonly WorldMap _map = new() { Margin = new Thickness(-36, -28), ClipToBounds = true };

    private Panel? _host;
    private Size _fitFor;
    private int _density;
    private readonly List<Control> _tileDescs = new();
    private Control? _smartCaption;
    private Border? _statCard;
    private readonly TextBlock _miniSpeed = Ui.Num(Ui.T("", 12.5, FontWeight.Medium, P.Text2));
    /// Home never scrolls: when the window is short it steps down to denser layouts until everything fits.
    public Control View
    {
        get
        {
            if (_host != null) return _host;
            _host = new Panel { ClipToBounds = true };
            _host.Children.Add(_root);
            _host.SizeChanged += (_, e) => Fit(e.NewSize, force: true);
            return _host;
        }
    }

    public int Density => _density;

    private void Fit(Size avail, bool force = false)
    {
        if (avail.Width <= 0 || avail.Height <= 0) return;
        if (!force && avail == _fitFor) return;
        _fitFor = avail;
        for (var d = 0; d <= 4; d++)
        {
            ApplyDensity(d);
            _root.Measure(new Size(avail.Width, double.PositiveInfinity));
            if (_root.DesiredSize.Height <= avail.Height + 0.5) break;
        }
    }

    private void ApplyDensity(int d)
    {
        _density = d;
        var orb = d switch { 0 => 136, 1 => 112, 2 or 3 => 96, _ => 84 };
        _orb.Width = _orb.Height = orb;
        if (d <= 1) _power.SetSize(268, 68); else if (d <= 3) _power.SetSize(248, 60); else _power.SetSize(232, 54);
        _modesStrip.Spacing = d >= 3 ? 4 : 6;
        _root.Margin = d >= 2 ? new Thickness(32, 18, 32, 14) : new Thickness(36, 28, 36, 24);
        _center.Margin = d >= 2 ? new Thickness(0, 4, 0, 10) : new Thickness(0, 12, 0, 18);
        _spark.IsVisible = d == 0;
        foreach (var t in _tileDescs) t.IsVisible = d < 1;
        if (_smartCaption != null) _smartCaption.IsVisible = d < 2;
        if (_statCard != null) _statCard.IsVisible = d < 3 && _speedHost.Content == null;
        _miniSpeed.IsVisible = d >= 3; _miniSpeed.Opacity = SatraCore.I.Phase == ConnPhase.On ? 1 : 0;
        _hint.MaxLines = d >= 4 ? 1 : 2; _hint.MinHeight = d >= 4 ? 18 : 36;
        _modeHost.Margin = new Thickness(0, 0, 0, d >= 2 ? 10 : 18);
        _server.Margin = new Thickness(0, 0, 0, d >= 2 ? 8 : 14);
    }

    /// Re-fit after content changes (new server card, mode text, update banner…).
    private void Refit() { if (_host != null) Dispatcher.UIThread.Post(() => Fit(_host.Bounds.Size, force: true), DispatcherPriority.Background); }

    public static HomePage? Last;
    /// QA: layout facts for the "no scroll / no jump" checks.
    public string DevFacts() => $"density={_density} root={_root.DesiredSize.Height:0}/{_host?.Bounds.Height:0} rebuilds={PickRebuilds} rows=[{string.Join(" | ", _slots.Select(x => $"{x.Item?.Remarks}@{x.View.Bounds.Y:0}"))}]";
    public HomePage()
    {
        Last = this;
        var head = Ui.Row(Ui.Title(L.S("home")), Ui.H(8,
            Ui.Btn(L.S("speed_home"), Ui.Kind.Ghost, () => ServersPage.SpeedInline(_speedHost, [SatraCore.I.Active], compact: true), "↯", 13),
            Ui.IconBtn("⟳", () => _ = SatraCore.I.UpdateSubsAsync(), L.S("update_all")),
            Ui.Btn(L.S("add"), Ui.Kind.Tinted, () => _ = AddAsync(), "+")));
        Grid.SetRowSpan(_map, 6);
        _root.Children.Add(_map); // first child = drawn behind everything
        _root.Children.Add(Ui.V(10, head, _updateBtnHost));
        AutoUpdater.Changed += PaintUpdate;
        PaintUpdate();

        _status.HorizontalAlignment = _hint.HorizontalAlignment = _timer.HorizontalAlignment = HorizontalAlignment.Center;
        _orb.Clicked += () => _ = SatraCore.I.ToggleAsync();
        _power.Clicked += () => _ = SatraCore.I.ToggleAsync();
        _status.IsVisible = false; // the switch carries the state label now (kept for QA facts)
        _hint.TextWrapping = TextWrapping.Wrap; _hint.TextAlignment = TextAlignment.Center; _hint.MaxWidth = 460;
        _hint.TextTrimming = TextTrimming.CharacterEllipsis;
        _miniSpeed.HorizontalAlignment = HorizontalAlignment.Center;
        _ports.MinHeight = 30; _hint.MinHeight = 36;
        _secBtn = SecurityButton();
        var powerRow = Ui.H(12, _power, _powerPing, _secBtn);
        powerRow.HorizontalAlignment = HorizontalAlignment.Center;
        _modesStrip.Margin = new Thickness(0, 6, 0, 0);
        var center = _center = Ui.V(8, _orb, powerRow, _status, _timer, _miniSpeed, _hint, _ports, _modesStrip);
        center.Margin = new Thickness(0, 12, 0, 18);
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(center, 1);
        _map.Avoid = powerRow; _map.Band = center;
        center.LayoutUpdated += (_, _) => _map.InvalidateVisual();
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
        _statCard = statCard;
        Grid.SetRow(statCard, 5);
        _root.Children.Add(statCard);
        _speedHost.MaxWidth = 640;
        Grid.SetRow(_speedHost, 5);
        _root.Children.Add(_speedHost);
        _speedHost.PropertyChanged += (_, e) => { if (e.Property == ContentControl.ContentProperty) { statCard.IsVisible = _density < 3 && _speedHost.Content == null; Refit(); } };

        // no UI work while the window is hidden/minimized or Home is not on screen
        bool Shown() => _root.IsEffectivelyVisible && _root.IsAttachedToVisualTree() && SatraShell.I.Window is { IsVisible: true, WindowState: not WindowState.Minimized };
        SatraCore.I.Speed += (_, _) => Dispatcher.UIThread.Post(() => { if (Shown()) UpdateSpeed(); });
        _root.AttachedToVisualTree += (_, _) => UpdateSpeed();
        SatraCore.I.Ticked += () => Dispatcher.UIThread.Post(() => { if (!Shown()) return;  { var c = SatraCore.I; _timer.Text = c.Since is { } s ? (DateTime.Now - s).ToString(@"hh\:mm\:ss") : ""; } });
        // pings only touch the numbers in place: no card rebuild, no reorder while a ping run is in flight
        PingEngine.I.Changed += () => Dispatcher.UIThread.Post(() => { UpdateHomePing(); if (Shown()) UpdatePicks(); });
    }

    private async Task AddAsync() => await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());

    /// <summary>Update banner on Home, driven by <see cref="AutoUpdater"/> (checked on app open).</summary>
    private void PaintUpdate()
    {
        var u = AutoUpdater.Found;
        var st = AutoUpdater.State;
        if (u == null || st is AutoUpdater.St.UpToDate or AutoUpdater.St.Checking or AutoUpdater.St.Idle) { _updateBtnHost.IsVisible = false; return; }
        var ver = u.Version ?? "";
        var (title, sub, btnText) = st switch
        {
            AutoUpdater.St.Downloading => (L.F("app_update_available", ver), L.S("app_update_downloading") + $" {AutoUpdater.Progress * 100:0}%", (string?)null),
            AutoUpdater.St.Ready => (L.F("upd_ready", ver), L.S(SatraAppUpdate.IsDebInstall ? "upd_ready_deb" : "upd_ready_d"), L.S("upd_restart")),
            AutoUpdater.St.Installing => (L.S("app_update_installing"), "", null),
            AutoUpdater.St.Error => (L.F("app_update_available", ver), AutoUpdater.Error ?? "", L.S("try_again")),
            _ => (L.F("app_update_available", ver), L.S("upd_avail_d"), L.S("upd_install")),
        };
        var kids = new List<Control> { Ui.Row(Ui.H(12, Ui.T("↑", 20, FontWeight.Bold, P.Accent), Ui.V(2, Ui.T(title, 14, FontWeight.SemiBold), Ui.Caption(sub, P.Text2))),
            btnText == null ? new Control() : Ui.Btn(btnText, Ui.Kind.Primary, () => _ = AutoUpdater.InstallAsync(), null, 13)) };
        if (st == AutoUpdater.St.Downloading) kids.Add(Ui.Bar(AutoUpdater.Progress, P.Accent, 4));
        _updateBtnHost.Child = Ui.Card(Ui.V(8, kids.ToArray()), new Thickness(16, 10), P.AccentSoft);
        _updateBtnHost.MaxWidth = 640;
        _updateBtnHost.IsVisible = true;
        Refit();
    }

    private void UpdateSpeed()
    {
        var c = SatraCore.I;
        var on = c.Phase == ConnPhase.On;
        _down.Text = Ui.Speed(on ? c.DownRate : 0);
        _up.Text = Ui.Speed(on ? c.UpRate : 0);
        _downTot.Text = $"{L.S("session")} {Ui.Bytes(c.SessionDown)}";
        _upTot.Text = $"{L.S("session")} {Ui.Bytes(c.SessionUp)}";
        var d = SatraState.I.Today();
        _today.Text = Ui.Ltr($"↓ {Ui.Bytes(d.Down)}  ↑ {Ui.Bytes(d.Up)}  Σ {Ui.Bytes(d.Down + d.Up)}");
        _spark.LineColor = c.Phase == ConnPhase.On ? P.Success : P.Accent;
        _spark.SetData(c.History);
        _miniSpeed.Text = Ui.Ltr($"↓ {Ui.Speed(on ? c.DownRate : 0)}   ↑ {Ui.Speed(on ? c.UpRate : 0)}");
    }

    public void Refresh()
    {
        var c = SatraCore.I;
        _orb.Phase = c.Phase;
        _orb.Kick();
        var m = SatraState.I.Mode;
        _power.Phase = c.Phase;
        var prof = ServiceLib.Satra.ProfileModes.Normalize(SatraState.I.Profile);
        _power.SetText(c.Phase switch
        {
            ConnPhase.On => L.S("pw_connected"), ConnPhase.Connecting => L.S("pw_connecting"), ConnPhase.Disconnecting => L.S("pw_stopping"),
            ConnPhase.Failed => L.S("pw_retry"), _ => L.S("pw_connect"),
        }, prof == "normal" ? L.S(ConnModes.Label(m)) : c.Phase == ConnPhase.On ? ServiceLib.Satra.ProfileModes.Emoji(prof) + "  " + L.F("mode_on", L.S("mode_" + prof)) : $"{L.S(ConnModes.Label(m))} · {L.S("mode_" + prof)}");
        // once connected: measure the active config right away (auto ping, not user-started: no TUN notice)
        if (c.Phase == ConnPhase.On && _lastPhase != ConnPhase.On && c.Active is { } act) _ = PingEngine.I.PingOneAsync(act);
        _lastPhase = c.Phase;
        _powerPing.Content = c.Phase == ConnPhase.On && c.Active is { } ap ? ServersPage.PingPill(ServersPage.Delay(ap), PingEngine.I.Get(ap.IndexId).Status == PingStatus.Pending) : null;
        PaintStrip();
        (_status.Text, _hint.Text, var col) = c.Phase switch
        {
            ConnPhase.On when c.ServerSilent => (L.S("st_on_" + Short(m)), L.S("srv_no_answer"), P.Warning),
            ConnPhase.On => (L.S("st_on_" + Short(m)), L.S("d_on_" + Short(m)), P.Success),
            ConnPhase.Connecting => (L.S("connecting"), L.S("d_connecting"), P.Accent),
            ConnPhase.Disconnecting => (L.S("disconnecting"), "", P.Text2),
            ConnPhase.Failed => (L.S("failed"), c.LastError ?? L.S("retry"), P.Danger),
            _ => (L.S("st_off"), c.ModeAwaitingConnect ? L.S("await_connect") : L.S("d_off"), P.Text),
        };
        var psig = $"{c.Phase == ConnPhase.On}{m}{L.Lang}";
        if (psig != _portsSig) { _portsSig = psig; _ports.Content = c.Phase == ConnPhase.On && m != ConnModes.Tun ? PortChips() : null; }
        _status.Foreground = P.B(col);
        _timer.Text = c.Since is { } s ? (DateTime.Now - s).ToString(@"hh\:mm\:ss") : "";
        // keep the line's space so the switch never moves when the state changes
        _timer.Opacity = c.Phase == ConnPhase.On ? 1 : 0;

        var mode = SatraState.I.Mode switch { "tun" => L.S("mode_vpn"), "socks" => L.S("mode_proxy_only"), _ => L.S("mode_proxy") };
        _modeChip.Child = Ui.T(mode, 10.5, FontWeight.SemiBold, P.Text2);
        _modeChip.Background = P.B(P.Fill);
        _modeChip.CornerRadius = Ui.RPill;
        _modeChip.Padding = new Thickness(8, 2);
        _modeChip.HorizontalAlignment = HorizontalAlignment.Left;

        var msig = $"{SatraState.I.Mode}{c.Phase}{L.Lang}";
        var rsig = $"{SatraState.I.SmartRouting}{SatraState.I.Routing}{L.Lang}";
        if (rsig != _routeSig) { _routeSig = rsig; _routeHost.Content = null; _modeSig = ""; }
        if (msig != _modeSig) { _modeSig = msig; _modeHost.Content = ModeSelector(); ApplyDensity(_density); Refit(); }

        var a = c.Active;
        _map.Show(c.Phase == ConnPhase.On && a != null ? NetBadges.ExitCc(a) : null);
        // Rebuild card when server / connect state / smart changes — not on every ping tick
        var exitIp = a != null && SatraState.I.Geo.TryGetValue(NetBadges.GeoKey(a), out var eg) ? eg.Ip : "";
        var key = $"{a?.IndexId}|{c.Phase}|{(a != null ? NetBadges.ExitCc(a) : null)}|{exitIp}|{TorExits.IsKnownExit(exitIp)}|{SatraState.I.SmartConnect}|{L.Lang}";
        if (key != _lastServerKey)
        {
            _lastServerKey = key;
            _server.Content = ServerCard(a);
            Refit();
        }
        else { UpdateHomePing(); UpdatePicks(); }
        _miniSpeed.Opacity = c.Phase == ConnPhase.On ? 1 : 0;
        UpdateSpeed();
    }

    private void UpdateHomePing()
    {
        var a = SatraCore.I.Active;
        if (a != null && SatraCore.I.Phase == ConnPhase.On) _powerPing.Content = ServersPage.PingPill(ServersPage.Delay(a), PingEngine.I.Get(a.IndexId).Status == PingStatus.Pending);
        if (a == null || a.IndexId != _pingTargetId) return;
        _homePing.Content = ServersPage.PingPill(ServersPage.Delay(a), PingEngine.I.Get(a.IndexId).Status == PingStatus.Pending);
    }

    private static string Short(string mode) => mode == ConnModes.Tun ? "tun" : mode == ConnModes.SystemProxy ? "proxy" : "ports";

    /// Copyable local endpoints, shown while the ports are open.
    private static Control PortChips()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        Control Chip(string label, string value)
        {
            var b = new Border
            {
                Background = P.B(P.Fill), CornerRadius = Ui.RPill, Padding = new Thickness(12, 5), Cursor = new Cursor(StandardCursorType.Hand),
                Child = Ui.H(8, Ui.T(label, 11, FontWeight.SemiBold, P.Text3), Ui.Num(Ui.T(Ui.Ltr(value), 12.5, FontWeight.SemiBold, P.Text)), Ui.T("⧉", 12, FontWeight.Normal, P.Text3)),
            };
            ToolTip.SetTip(b, L.S("copy"));
            b.PointerReleased += (_, _) => _ = SatraShell.I.Copy(value);
            return Ui.Pressable(b);
        }
        // SOCKS5 and HTTP share one mixed inbound port in v2rayN/Xray
        var chips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        chips.Children.Add(Chip("SOCKS5 · HTTP", $"{Global.Loopback}:{port}"));
        return chips;
    }

    /// Three clear tiles: Ports only · System proxy · Whole device. The selected one is what is active now.
    private Control ModeSelector()
    {
        var st = SatraState.I;
        var on = SatraCore.I.Phase == ConnPhase.On;
        var grid = new UniformGrid { Rows = 1 };
        _tileDescs.Clear();
        foreach (var (mode, glyph, sub) in new[] { (ConnModes.Ports, "⌁", "m_ports_d"), (ConnModes.SystemProxy, "◎", "m_proxy_d"), (ConnModes.Tun, "⛨", "m_tun_d") })
        {
            var sel = st.Mode == mode;
            var live = sel && on;
            var accent = live ? P.Success : P.Accent;
            var title = Ui.T(L.S(ConnModes.Label(mode)), 13.5, FontWeight.SemiBold, sel ? P.Text : P.Text2);
            var desc = Ui.T(L.S(sub), 11.5, FontWeight.Normal, P.Text3, TextWrapping.Wrap);
            _tileDescs.Add(desc);
            var dot = new Border { Width = 7, Height = 7, CornerRadius = Ui.RPill, Background = live ? P.B(P.Success) : sel ? P.B(P.Accent) : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            var tile = new Border
            {
                Margin = new Thickness(4, 0), Padding = new Thickness(14, 12), CornerRadius = Ui.RCtl, 
                Background = P.B(sel ? P.Lerp(P.Surface, accent, 0.10) : P.Surface),
                BorderBrush = P.B(sel ? P.Lerp(P.Surface, accent, 0.55) : P.Separator), BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = Ui.V(4, Ui.H(8, Ui.T(glyph, 15, FontWeight.Normal, sel ? accent : P.Text3), title, dot), desc),
            };
            if (tile.Child is StackPanel sp) { sp.VerticalAlignment = VerticalAlignment.Center; foreach (var c in ((StackPanel)sp.Children[0]).Children) c.VerticalAlignment = VerticalAlignment.Center; }
            ToolTip.SetTip(tile, L.S(sub));
            tile.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(220) } };
            Avalonia.Automation.AutomationProperties.SetName(tile, L.S(ConnModes.Label(mode)));
            tile.PointerReleased += (_, _) => { if (SatraCore.I.Phase is ConnPhase.Connecting or ConnPhase.Disconnecting) return; _ = SatraCore.I.SetModeAsync(mode); };
            grid.Children.Add(Ui.Pressable(tile));
        }
        grid.Margin = new Thickness(-4, 0);
        // smart routing lives with the mode: it decides which sites skip the server in every mode
        var region = st.Routing switch { "russia" => L.S("routing_russia"), "china" => L.S("routing_china"), _ => L.S("routing_iran") };
        var sw = Ui.Switch(st.SmartRouting, v =>
        {
            st.SmartRouting = v;
            SatraState.Save();
            if (v) _ = SatraCore.I.Main.ApplyRegionalPreset(LocalRules.Preset(st.Routing));
            _ = SatraCore.I.ApplyLocalLimitsAsync();
            _routeSig = "";
            Refresh();
        });
        var smart = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var g = Ui.T("⇄", 14, FontWeight.Normal, st.SmartRouting ? P.Accent : P.Text3);
        g.Margin = new Thickness(0, 1, 10, 0); g.VerticalAlignment = VerticalAlignment.Top;
        var cap = Ui.Caption(st.SmartRouting ? L.S("smart_routing_d") : L.S("smart_routing_off_d"), P.Text3);
        _smartCaption = cap;
        var txt = Ui.V(1, Ui.T($"{L.S("smart_routing")} · {region}", 13, FontWeight.Medium), cap);
        sw.VerticalAlignment = VerticalAlignment.Center; sw.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(txt, 1); Grid.SetColumn(sw, 2);
        smart.Children.Add(g); smart.Children.Add(txt); smart.Children.Add(sw);
        smart.Margin = new Thickness(2, 10, 2, 0);
        return Ui.Enter(Ui.V(8, Ui.Section(L.S("route_title")), grid, smart));
    }

    /// One fixed row per quick pick; rebinding swaps text/flag/pill inside the same controls (no rebuild, no layout jump).
    private sealed class Slot
    {
        public readonly Border Row = new() { Padding = new Thickness(8, 6), CornerRadius = Ui.RCtl, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Height = 34 };
        public readonly ContentControl Flag = new();
        public readonly TextBlock Name = Ui.T("", 13.5, FontWeight.Medium, P.Text);
        public readonly ContentControl Ping = new() { MinWidth = 64, HorizontalContentAlignment = HorizontalAlignment.Right };
        public ProfileItemModel? Item;
        public string Cc = "", Pill = "";
        public Control View = null!;
    }
    private readonly List<Slot> _slots = new();
    private Border? _pickSep;
    public static int PickRebuilds; // QA: counts card rebuilds (must not move while pinging)

    private void UpdatePicks()
    {
        if (_slots.Count == 0) return;
        var pending = SatraCore.I.Profiles.ProfileItems.Any(p => PingEngine.I.Get(p.IndexId).Status == PingStatus.Pending);
        // during a ping run the rows stay put and only their numbers change; the order settles once the run is done
        var shown = _slots.Where(s => s.Item != null).Select(s => s.Item!).ToList();
        var picks = pending && shown.Count > 0 ? shown : Picks();
        for (var i = 0; i < _slots.Count; i++) Bind(_slots[i], i < picks.Count ? picks[i] : null);
        if (_pickSep != null) _pickSep.IsVisible = picks.Count > 0;
    }

    private static void Bind(Slot s, ProfileItemModel? p)
    {
        s.Item = p;
        s.View.IsVisible = p != null;
        if (p == null) return;
        var cc = NetBadges.ExitCc(p) ?? "";
        if (cc != s.Cc || s.Flag.Content == null) { s.Cc = cc; s.Flag.Content = NetBadges.FlagView(NetBadges.ExitCc(p), 18); }
        var nm = Ui.CleanName(p.Remarks);
        if (s.Name.Text != nm) s.Name.Text = nm;
        var d = ServersPage.Delay(p); var pend = PingEngine.I.Get(p.IndexId).Status == PingStatus.Pending;
        var key = $"{d}|{pend}";
        if (key != s.Pill) { s.Pill = key; s.Ping.Content = ServersPage.PingPill(d, pend); }
    }
    private static List<ProfileItemModel> Picks()
    {
        var c = SatraCore.I;
        return ConnModes.QuickPicks(c.Profiles.ProfileItems.ToList(), p => p.IndexId, ServersPage.Delay, c.Active?.IndexId);
    }

    /// The active server, then the fastest alternatives (one click switches; the core reloads on it).
    private Control ServerCard(ProfileItemModel? a)
    {
        if (a == null) return NoServerCard();
        PickRebuilds++;
        var rows = new StackPanel { Spacing = 0 };
        rows.Children.Add(ActiveRow(a));
        _pickSep = new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(0, 8, 0, 4) };
        rows.Children.Add(_pickSep);
        _slots.Clear();
        for (var i = 0; i < 3; i++) { var s = NewSlot(); _slots.Add(s); rows.Children.Add(s.View); }
        UpdatePicks();
        var head = Ui.Row(Ui.Section(L.S("server")), Ui.H(4,
            Ui.Btn(L.S("ping_all_short"), Ui.Kind.Ghost, () => _ = PingGuard.All(SatraCore.I.Profiles.ProfileItems.ToList()), "◎", 12),
            Ui.Btn(L.S("all_servers") + "  ›", Ui.Kind.Ghost, () => SatraShell.I.Go("servers"), size: 12)));
        return Ui.Enter(Ui.V(6, head, Ui.Card(rows, new Thickness(16, 12))));
    }

    private Control NoServerCard() => Ui.Card(Ui.Row(Ui.V(4, Ui.T(L.S("no_server"), 16, FontWeight.SemiBold), Ui.Caption(L.S("add_hint"))),
        Ui.Btn(L.S("add_clip"), Ui.Kind.Primary, () => _ = AddAsync())), new Thickness(18, 14));

    private static Slot NewSlot()
    {
        var s = new Slot();
        s.Name.TextTrimming = TextTrimming.CharacterEllipsis;
        s.Name.VerticalAlignment = s.Flag.VerticalAlignment = s.Ping.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        s.Name.Margin = new Thickness(10, 0);
        Grid.SetColumn(s.Name, 1); Grid.SetColumn(s.Ping, 2);
        g.Children.Add(s.Flag); g.Children.Add(s.Name); g.Children.Add(s.Ping);
        s.Row.Child = g;
        var row = s.Row;
        row.PointerEntered += (_, _) => row.Background = P.B(P.Fill);
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, _) => { if (s.Item != null) _ = SatraCore.I.UseAsync(s.Item); };
        ToolTip.SetTip(row, L.S("cm_set_active"));
        s.View = Ui.Pressable(row);
        return s;
    }

    private Control ActiveRow(ProfileItemModel a)
    {
        _pingTargetId = a.IndexId;
        var cc = NetBadges.ExitCc(a);
        var flag = NetBadges.FlagView(cc, 26);
        var sub = ServersPage.SubName(a.Subid);
        var meta = new List<Control>();
        if (!string.IsNullOrEmpty(sub)) meta.Add(Ui.Caption(sub, P.Text3));
        if (SatraState.I.SmartConnect) meta.Add(Ui.Pill(L.S("smart"), P.Accent, P.AccentSoft, 10.5));
        var ip = SatraState.I.Geo.TryGetValue(NetBadges.GeoKey(a), out var g) ? g.Ip : "";
        if (SatraCore.I.Phase == ConnPhase.On && ip.Length > 0)
        {
            {
                var ipb = Ui.Pill(Ui.Ltr(ip) + "  ⧉", P.Text2, P.Fill, 10.5);
                ipb.Cursor = new Cursor(StandardCursorType.Hand); ToolTip.SetTip(ipb, L.S("sc_ip"));
                ipb.PointerReleased += (_, _) => _ = SatraShell.I.Copy(ip);
                meta.Add(Ui.Pressable(ipb));
            }
            if (TorExits.IsKnownExit(ip)) meta.Add(TorBadge());
        }
        var q = QuotaPill(a.Subid);
        if (q != null) meta.Add(q);
        var metaRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var m in meta) { m.VerticalAlignment = VerticalAlignment.Center; m.Margin = new Thickness(0, 1, 8, 1); metaRow.Children.Add(m); }
        var left = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        flag.VerticalAlignment = VerticalAlignment.Center;
        var txt = Ui.V(3, Ui.T(Ui.CleanName(a.Remarks), 15.5, FontWeight.SemiBold), metaRow);
        txt.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(txt, 1);
        left.Children.Add(flag); left.Children.Add(txt);
        // Fresh ContentControl each rebuild — reusing _homePing under a new parent crashes Avalonia.
        _homePing = new ContentControl { Content = ServersPage.PingPill(ServersPage.Delay(a), PingEngine.I.Get(a.IndexId).Status == PingStatus.Pending) };
        var pingBtn = Ui.IconBtn("◎", () => _ = PingGuard.One(a), L.S("ping"), P.Text2, 15);
        var right = Ui.H(8, _homePing, pingBtn);
        return Ui.Row(left, right);
    }

    /// Calm badge: the exit IP is on the official Tor exit list → opens Security at the Tor section.
    private static Control TorBadge()
    {
        var b = Ui.Pill("⧗  " + L.S("home_tor_exit"), P.Info, P.Fill, 10.5);
        b.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(b, L.S("home_tor_exit_tip"));
        Avalonia.Automation.AutomationProperties.SetName(b, L.S("home_tor_exit"));
        b.PointerReleased += (_, _) => { SecurityPage.FocusTor = true; SatraShell.I.Go("security"); };
        return Ui.Pressable(b);
    }

    private Border SecurityButton()
    {
        var b = new Border
        {
            Height = 44, Padding = new Thickness(16, 0), CornerRadius = Ui.RPill, Background = P.B(P.Surface), BorderBrush = P.B(P.Separator), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.H(8, Ui.T("⛨", 15, FontWeight.Normal, P.Info), Ui.T(L.S("security_short"), 13.5, FontWeight.SemiBold)),
        };
        if (b.Child is StackPanel sp) foreach (var k in sp.Children) k.VerticalAlignment = VerticalAlignment.Center;
        b.PointerEntered += (_, _) => b.Background = P.B(P.Raised);
        b.PointerExited += (_, _) => b.Background = P.B(P.Surface);
        b.PointerReleased += (_, _) => SatraShell.I.Go("security");
        Avalonia.Automation.AutomationProperties.SetName(b, L.S("security_short"));
        return Ui.Pressable(b);
    }

    private static readonly (string id, string glyph)[] ModeGlyphs =
        [("normal", "◉"), ("game", "▶"), ("secure", "⛨"), ("fortress", "▣"), ("stream", "≋"), ("eco", "❀")];

    /// Connection profiles (MODES-SPEC.md): one calm chip row; rebuilt only when selection/gating/language changes.
    private void PaintStrip()
    {
        var tun = SatraState.I.Mode == ConnModes.Tun;
        var cur = ServiceLib.Satra.ProfileModes.Normalize(SatraState.I.Profile);
        var sig = $"{cur}{tun}{L.Lang}{_density >= 3}";
        if (sig == _stripSig) return;
        _stripSig = sig;
        _modesStrip.Children.Clear();
        foreach (var (id, glyph) in ModeGlyphs)
        {
            var sel = id == cur;
            var ok = ServiceLib.Satra.ProfileModes.Available(id, tun);
            var mc = Color.Parse(ServiceLib.Satra.ProfileModes.Color(id));
            var info = new Border
            {
                Width = 16, Height = 16, CornerRadius = Ui.RPill, Background = P.B(P.Lerp(P.Surface, mc, sel ? 0.35 : 0.18)),
                Cursor = new Cursor(StandardCursorType.Help), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "!", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = P.B(mc), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            ToolTip.SetTip(info, L.F("mode_info", L.S("mode_" + id)));
            Avalonia.Automation.AutomationProperties.SetName(info, L.F("mode_info", L.S("mode_" + id)));
            info.PointerReleased += (_, e) => { e.Handled = true; ModeInfo(id); };
            var chip = new Border
            {
                Height = 30, Padding = new Thickness(_density >= 3 ? 8 : 10, 0, 7, 0), CornerRadius = Ui.RPill,
                Background = P.B(sel ? P.Lerp(P.Surface, mc, 0.18) : P.Surface),
                BorderBrush = P.B(sel ? P.Lerp(P.Surface, mc, 0.65) : P.Separator), BorderThickness = new Thickness(1),
                Cursor = new Cursor(ok ? StandardCursorType.Hand : StandardCursorType.Arrow), Opacity = ok ? 1 : 0.42,
                Child = Ui.H(6, Ui.T(ServiceLib.Satra.ProfileModes.Emoji(id), 12, FontWeight.Normal, sel ? mc : P.Text3), Ui.T(L.S("mode_" + id), 12.5, sel ? FontWeight.SemiBold : FontWeight.Medium, sel ? P.Text : P.Text2), info),
            };
            if (chip.Child is StackPanel sp) foreach (var k in sp.Children) k.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(chip, ok ? L.S("mode_" + id + "_d") : L.S("gate_tun") + " — " + L.S("mode_gate_d"));
            Avalonia.Automation.AutomationProperties.SetName(chip, L.S("mode_" + id));
            chip.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(220) } };
            chip.PointerReleased += (_, e) =>
            {
                if (e.Handled) return;
                if (!ok) { SatraCore.I.Notify(L.S("gate_tun")); return; }
                if (id == ServiceLib.Satra.ProfileModes.Normalize(SatraState.I.Profile)) return;
                ActivateMode(id);
            };
            _modesStrip.Children.Add(Ui.Pressable(chip));
        }
    }

    public static string? LastActivation; // QA
    /// The activation moment: a floating card lists what the mode changes, ticking each item, then "X mode is on".
    /// It floats above the page (no layout change underneath) and fades away by itself.
    public void ActivateMode(string id)
    {
        var tun = SatraState.I.Mode == ConnModes.Tun;
        var changes = ServiceLib.Satra.ProfileModes.Changes(id, tun);
        LastActivation = id + ":" + string.Join(",", changes.Select(c => c.key));
        var mcol = Color.Parse(ServiceLib.Satra.ProfileModes.Color(id));
        var title = Ui.T(L.F("mode_applying", L.S("mode_" + id)), 15, FontWeight.SemiBold);
        var badge = new Border { Width = 34, Height = 34, CornerRadius = Ui.RPill, Background = P.B(P.Lerp(P.Raised, mcol, 0.25)), Child = new TextBlock { Text = ServiceLib.Satra.ProfileModes.Emoji(id), FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        title.VerticalAlignment = VerticalAlignment.Center;
        var list = new StackPanel { Spacing = 7, Margin = new Thickness(0, 10, 0, 0) };
        var rows = new List<(Border tick, Control row)>();
        foreach (var (key, arg) in changes)
        {
            var tick = new Border { Width = 18, Height = 18, CornerRadius = Ui.RPill, Background = P.B(P.Fill), Child = Ui.T("✓", 10.5, FontWeight.Bold, P.Bg), VerticalAlignment = VerticalAlignment.Center };
            if (tick.Child is TextBlock tt) { tt.HorizontalAlignment = HorizontalAlignment.Center; tt.VerticalAlignment = VerticalAlignment.Center; tt.Opacity = 0; }
            tick.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(200) } };
            var txt = Ui.T(arg == null ? L.S(key) : L.F(key, arg), 13, FontWeight.Medium, P.Text2);
            txt.VerticalAlignment = VerticalAlignment.Center;
            var row = Ui.H(10, tick, txt);
            row.Opacity = 0;
            row.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) } };
            rows.Add((tick, row));
            list.Children.Add(row);
        }
        var foot = Ui.Caption(SatraCore.I.Phase == ConnPhase.On ? "" : L.S("mode_applies_next"), P.Text3);
        foot.Margin = new Thickness(0, 10, 0, 0);
        var card = new Border
        {
            Width = 340, Padding = new Thickness(22, 18), CornerRadius = new CornerRadius(20), Background = P.B(P.Raised),
            BorderBrush = P.B(P.Lerp(P.Raised, mcol, 0.45)), BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("0 24 60 0 #AA000000"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            Child = Ui.V(0, Ui.H(12, badge, title), list, foot), Opacity = 0,
            RenderTransform = TransformOperations.Parse("scale(0.96)"),
            Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(240) },
                new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            },
        };
        _host?.Children.Add(card);
        DispatcherTimer.RunOnce(() => { card.Opacity = 1; card.RenderTransform = TransformOperations.Parse("scale(1)"); }, TimeSpan.FromMilliseconds(16));
        for (var i = 0; i < rows.Count; i++)
        {
            var (tick, row) = rows[i];
            DispatcherTimer.RunOnce(() =>
            {
                row.Opacity = 1;
                DispatcherTimer.RunOnce(() => { tick.Background = P.B(mcol); if (tick.Child is TextBlock t) t.Opacity = 1; }, TimeSpan.FromMilliseconds(160));
            }, TimeSpan.FromMilliseconds(260 + i * 200));
        }
        var end = 260 + rows.Count * 200 + 250;
        DispatcherTimer.RunOnce(() => { title.Text = L.F("mode_on", L.S("mode_" + id)); title.Foreground = P.B(mcol); }, TimeSpan.FromMilliseconds(end));
        DispatcherTimer.RunOnce(() => { card.Opacity = 0; card.RenderTransform = TransformOperations.Parse("scale(0.98)"); }, TimeSpan.FromMilliseconds(end + 1500));
        DispatcherTimer.RunOnce(() => _host?.Children.Remove(card), TimeSpan.FromMilliseconds(end + 1850));
        _ = SatraCore.I.SetProfileAsync(id);
    }

    /// "!" panel: every setting the overlay really writes (ProfileModes.Details mirrors Apply), its value and why.
    public static string? LastInfo; // QA
    public static void ModeInfo(string id)
    {
        var tun = SatraState.I.Mode == ConnModes.Tun;
        var mc = Color.Parse(ServiceLib.Satra.ProfileModes.Color(id));
        var det = ServiceLib.Satra.ProfileModes.Details(id, tun);
        LastInfo = id + ":" + string.Join(",", det.Select(d => d.Key));
        var head = Ui.H(12,
            new Border { Width = 44, Height = 44, CornerRadius = Ui.RPill, Background = P.B(P.Lerp(P.Surface, mc, 0.25)), Child = new TextBlock { Text = ServiceLib.Satra.ProfileModes.Emoji(id), FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            Ui.V(2, Ui.T(L.S("mode_" + id), 17, FontWeight.SemiBold, mc), Ui.Caption(L.S("mode_" + id + "_d"), P.Text2)));
        var list = new StackPanel();
        var first = true;
        foreach (var d in det)
        {
            if (!first) list.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0) });
            first = false;
            var (ak, ac) = d.State switch { ServiceLib.Satra.ProfileModes.Act.On => ("act_on", P.Success), ServiceLib.Satra.ProfileModes.Act.Off => ("act_off", P.Text2), _ => ("act_set", mc) };
            var why = Ui.T(L.S(d.Why), 12.5, FontWeight.Normal, P.Text2); why.TextWrapping = TextWrapping.Wrap;
            var val = Ui.T(Ui.Ltr(d.Value), 11, FontWeight.Normal, P.Text3); val.FontFamily = new FontFamily("monospace"); val.TextWrapping = TextWrapping.Wrap;
            list.Children.Add(new Border { Padding = new Thickness(14, 11), Child = Ui.V(4, Ui.Row(Ui.T(L.S(d.Key), 13.5, FontWeight.SemiBold), Ui.Pill(L.S(ak), ac, P.Lerp(P.Surface, ac, 0.16), 10.5)), val, why) });
        }
        if (det.Count == 0) list.Children.Add(new Border { Padding = new Thickness(14, 14), Child = Ui.T(L.S("mode_info_none"), 13, FontWeight.Normal, P.Text2) });
        Control PcCard(string kind, Color col, string glyph)
        {
            var sp = new StackPanel { Spacing = 8 };
            sp.Children.Add(Ui.T(L.S("mode_" + kind + "s"), 12, FontWeight.SemiBold, col));
            for (var i = 1; L.Has($"{kind}_{id}_{i}"); i++)
            {
                var t = Ui.T(L.S($"{kind}_{id}_{i}"), 12.5, FontWeight.Normal, P.Text); t.TextWrapping = TextWrapping.Wrap;
                var g = Ui.T(glyph, 12, FontWeight.Bold, col); g.Width = 14;
                var row = new DockPanel(); DockPanel.SetDock(g, Dock.Left); row.Children.Add(g); row.Children.Add(t); sp.Children.Add(row);
            }
            return new Border { Background = P.B(P.Lerp(P.Surface, col, 0.07)), BorderBrush = P.B(P.Lerp(P.Surface, col, 0.3)), BorderThickness = new Thickness(1), CornerRadius = Ui.RCard, Padding = new Thickness(14, 12), Child = sp };
        }
        var pc = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        var pros = PcCard("pro", P.Success, "+"); var cons = PcCard("con", P.Warning, "−");
        Grid.SetColumn(cons, 2); pc.Children.Add(pros); pc.Children.Add(cons);
        var kids = new List<Control> { head, pc, Ui.Section(L.S("mode_settings")), new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = list } };
        if (!tun && ServiceLib.Satra.ProfileModes.Details(id, true).Count > det.Count) kids.Add(Ui.Caption(L.S("mode_info_tun"), P.Text3));
        if (!ServiceLib.Satra.ProfileModes.Available(id, tun)) kids.Add(Ui.Caption(L.S("gate_tun"), P.Warning));
        var note = Ui.Caption(L.S("mode_info_note"), P.Text3); note.TextWrapping = TextWrapping.Wrap; kids.Add(note);
        SatraShell.I.Sheet(L.F("mode_info", L.S("mode_" + id)), new ScrollViewer { MaxHeight = 520, Content = Ui.V(12, kids.ToArray()) }, 520);
    }

    private static Control? QuotaPill(string? subId)
    {
        if (subId == null || !SatraState.I.Subs.TryGetValue(subId, out var m) || (m.Total <= 0 && m.Expire <= 0)) return null;
        var (frac, data, time) = SubInfo.Describe(m);
        var col = P.Level(frac);
        return Ui.Pill(string.Join(" · ", new[] { data, time }.Where(x => !string.IsNullOrEmpty(x))), col, P.Lerp(P.Surface, col, 0.15), 10.5);
    }
}
