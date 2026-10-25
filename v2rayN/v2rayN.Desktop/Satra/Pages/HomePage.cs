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
    private readonly ConnectOrb _orb = new() { Width = 136, Height = 136, HorizontalAlignment = HorizontalAlignment.Center };
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
        _root.Margin = d >= 2 ? new Thickness(32, 18, 32, 14) : new Thickness(36, 28, 36, 24);
        _center.Margin = d >= 2 ? new Thickness(0, 4, 0, 10) : new Thickness(0, 12, 0, 18);
        _spark.IsVisible = d == 0;
        foreach (var t in _tileDescs) t.IsVisible = d < 1;
        if (_smartCaption != null) _smartCaption.IsVisible = d < 2;
        if (_statCard != null) _statCard.IsVisible = d < 3 && _speedHost.Content == null;
        _miniSpeed.IsVisible = d >= 3 && SatraCore.I.Phase == ConnPhase.On;
        _hint.MaxLines = d >= 4 ? 1 : 2;
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
        _hint.TextWrapping = TextWrapping.Wrap; _hint.TextAlignment = TextAlignment.Center; _hint.MaxWidth = 460;
        _hint.TextTrimming = TextTrimming.CharacterEllipsis;
        _miniSpeed.HorizontalAlignment = HorizontalAlignment.Center;
        var center = _center = Ui.V(8, _orb, _status, _timer, _miniSpeed, _hint, _ports);
        center.Margin = new Thickness(0, 12, 0, 18);
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(center, 1);
        _map.Avoid = _orb; _map.Band = center;
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
        (_status.Text, _hint.Text, var col) = c.Phase switch
        {
            ConnPhase.On when c.ServerSilent => (L.S("st_on_" + Short(m)), L.S("srv_no_answer"), P.Warning),
            ConnPhase.On => (L.S("st_on_" + Short(m)), L.S("d_on_" + Short(m)), P.Success),
            ConnPhase.Connecting => (L.S("connecting"), L.S("d_connecting"), P.Accent),
            ConnPhase.Disconnecting => (L.S("disconnecting"), "", P.Text2),
            ConnPhase.Failed => (L.S("failed"), c.LastError ?? L.S("retry"), P.Danger),
            _ => (L.S("st_off"), L.S("d_off"), P.Text),
        };
        var psig = $"{c.Phase == ConnPhase.On}{m}{L.Lang}";
        if (psig != _portsSig) { _portsSig = psig; _ports.Content = c.Phase == ConnPhase.On && m != ConnModes.Tun ? PortChips() : null; }
        _status.Foreground = P.B(col);
        _timer.Text = c.Since is { } s ? (DateTime.Now - s).ToString(@"hh\:mm\:ss") : "";
        _timer.IsVisible = c.Phase == ConnPhase.On;

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
        var key = $"{a?.IndexId}|{c.Phase}|{(a != null ? NetBadges.ExitCc(a) : null)}|{SatraState.I.SmartConnect}|{L.Lang}";
        if (key != _lastServerKey)
        {
            _lastServerKey = key;
            _server.Content = ServerCard(a);
            Refit();
        }
        else { UpdateHomePing(); UpdatePicks(); }
        _miniSpeed.IsVisible = _density >= 3 && c.Phase == ConnPhase.On;
        UpdateSpeed();
    }

    private void UpdateHomePing()
    {
        var a = SatraCore.I.Active;
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
            Ui.Btn(L.S("ping_all_short"), Ui.Kind.Ghost, () => _ = PingEngine.I.PingAllAsync(SatraCore.I.Profiles.ProfileItems.ToList()), "◎", 12),
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
        var left = Ui.H(12, flag, Ui.V(3, Ui.T(Ui.CleanName(a.Remarks), 15.5, FontWeight.SemiBold), Ui.H(8, meta.ToArray())));
        var ip = SatraState.I.Geo.TryGetValue(NetBadges.GeoKey(a), out var g) ? g.Ip : "";
        if (SatraCore.I.Phase == ConnPhase.On && ip.Length > 0)
            meta.Add(Ui.Btn(Ui.Ltr(ip) + "  ⧉", Ui.Kind.Ghost, () => _ = SatraShell.I.Copy(ip), size: 11));
        var q = QuotaPill(a.Subid);
        if (q != null) meta.Add(q);
        // Fresh ContentControl each rebuild — reusing _homePing under a new parent crashes Avalonia.
        _homePing = new ContentControl { Content = ServersPage.PingPill(ServersPage.Delay(a), PingEngine.I.Get(a.IndexId).Status == PingStatus.Pending) };
        var pingBtn = Ui.IconBtn("◎", () => _ = PingEngine.I.PingOneAsync(a), L.S("ping"), P.Text2, 15);
        var right = Ui.H(8, _homePing, pingBtn);
        return Ui.Row(left, right);
    }

    private static Control? QuotaPill(string? subId)
    {
        if (subId == null || !SatraState.I.Subs.TryGetValue(subId, out var m) || (m.Total <= 0 && m.Expire <= 0)) return null;
        var (frac, data, time) = SubInfo.Describe(m);
        var col = P.Level(frac);
        return Ui.Pill(string.Join(" · ", new[] { data, time }.Where(x => !string.IsNullOrEmpty(x))), col, P.Lerp(P.Surface, col, 0.15), 10.5);
    }
}
