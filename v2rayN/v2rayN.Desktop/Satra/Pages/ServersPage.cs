using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Layout;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Servers list: structure rebuilds only when membership/order changes; ping/speed update in place.</summary>
public class ServersPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*") };
    private readonly StackPanel _list = new() { Spacing = 8, Margin = new Thickness(0, 0, 0, 28) };
    private readonly WrapPanel _chips = new();
    private readonly TextBox _search = Ui.Input(L.S("search"));
    private readonly Dictionary<string, string?> _cdn = [];
    private readonly Dictionary<string, string?> _entry = [];
    private readonly Dictionary<string, ContentControl> _pingHosts = [];
    private readonly Dictionary<string, ContentControl> _speedHosts = [];
    private bool _favOnly, _sortBest;
    private string _sig = "";

    public Control View => _root;

    public ServersPage()
    {
        _root.Children.Add(Ui.Row(Ui.Title(L.S("servers")), Ui.H(8,
            Ui.Btn(L.S("ping_all"), Ui.Kind.Ghost, () => _ = PingAllAsync(), "◎", 13),
            Ui.Btn(L.S("speed_all"), Ui.Kind.Ghost, () => SpeedSheet(SatraCore.I.Profiles.ProfileItems.ToList()), "↯", 13),
            Ui.Btn(L.S("add"), Ui.Kind.Tinted, () => _ = AddAsync(), "+", 13))));

        var smart = Ui.Card(Ui.Row(
            Ui.H(12, new Border { Width = 36, Height = 36, CornerRadius = Ui.RCtl, Background = P.B(P.AccentSoft), Child = Center(Icons.Make("⚡", 18, P.Accent)) },
                Ui.V(2, Ui.T(L.S("smart"), 15, FontWeight.SemiBold), Ui.Caption(L.S("smart_desc")))),
            Ui.H(10, Ui.Btn(L.S("sort_best"), Ui.Kind.Ghost, () => { _sortBest = !_sortBest; Rebuild(true); }, size: 13),
                Ui.Switch(SatraState.I.SmartConnect, v => { SatraState.I.SmartConnect = v; SatraState.Save(); }))), new Thickness(16, 12));
        smart.Margin = new Thickness(0, 18, 0, 12);
        Grid.SetRow(smart, 1);
        _root.Children.Add(smart);

        _search.Width = 260;
        _search.TextChanged += (_, _) => Rebuild(true);
        var bar = Ui.Row(_chips, _search);
        bar.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetRow(bar, 3);
        _root.Children.Add(bar);

        var sv = new ScrollViewer { Content = _list };
        Grid.SetRow(sv, 4);
        _root.Children.Add(sv);
        // Android parity: thin indeterminate bar while a ping run is active
        Grid.SetRow(_testBar, 4);
        Grid.SetRow(_speedPanel, 2);
        _root.Children.Add(_speedPanel);
        _root.Children.Add(_testBar);

        SatraCore.I.Profiles.ProfileItems.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() => Rebuild(false));
        PingEngine.I.Changed += () => Dispatcher.UIThread.Post(() => { SetTesting(PingEngine.I.Running); UpdatePingsInPlace(); });
    }

    private static Control Center(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    private async Task AddAsync() => await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());

    private readonly ContentControl _speedPanel = new() { Margin = new Thickness(0, 0, 0, 12) };
    private CancellationTokenSource? _speedCts;

    /// <summary>Inline (same page, no popup) download-only speed test with a live gauge; results sorted fastest first.</summary>
    private void SpeedSheet(List<ProfileItemModel> list) => SpeedInline(_speedPanel, list, () => UpdatePingsInPlace());

    public static void SpeedInline(ContentControl host, IReadOnlyList<ProfileItemModel?> list, Action? after = null, bool compact = false)
    {
        if (list.Count == 0) return;
        if (host.Tag is CancellationTokenSource old) old.Cancel();
        var cts = new CancellationTokenSource();
        host.Tag = cts;
        var gauge = new Controls.SpeedGauge { HorizontalAlignment = HorizontalAlignment.Center };
        if (compact) { gauge.Width = 220; gauge.Height = 186; }
        var now = Ui.T("", 15, FontWeight.SemiBold);
        now.HorizontalAlignment = HorizontalAlignment.Center;
        var step = Ui.Caption("", P.Text3);
        step.HorizontalAlignment = HorizontalAlignment.Center;
        step.TextWrapping = TextWrapping.Wrap;
        step.TextAlignment = TextAlignment.Center;
        var results = new StackPanel { Spacing = 6 };
        var done = new List<(ProfileItemModel? P, double Mbps, string? Err)>();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        void Close() { cts.Cancel(); host.Content = null; }
        Button stop = null!, retry = null!, close = null!;
        stop = Ui.Btn(L.S("stop"), Ui.Kind.Ghost, () => cts.Cancel(), size: 13);
        retry = Ui.Btn(L.S("try_again"), Ui.Kind.Tinted, () => SpeedInline(host, list, after, compact), "↻", 13);
        close = Ui.Btn(L.S("close"), Ui.Kind.Ghost, Close, size: 13);
        retry.IsVisible = false;
        btns.Children.AddRange([stop, retry, close]);
        var left = Ui.V(compact ? 4 : 8, gauge, now, step, btns);
        left.Width = 300;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 20 };
        grid.Children.Add(left);
        var sv = new ScrollViewer { Content = results, MaxHeight = 300, VerticalAlignment = VerticalAlignment.Center, IsVisible = list.Count > 1 };
        Grid.SetColumn(sv, 1);
        grid.Children.Add(sv);
        host.Content = Ui.Enter(Ui.Card(Ui.V(10, Ui.Row(Ui.T(L.S("speed_test"), 15, FontWeight.SemiBold), Ui.Caption(L.S("speed_dl_only"), P.Text3)), grid), new Thickness(18, 14)));

        static string Name(ProfileItemModel? p) => p == null ? (SatraCore.I.Phase == ConnPhase.On ? L.S("speed_via_vpn") : L.S("speed_direct")) : Ui.CleanName(p.Remarks);
        void Paint()
        {
            results.Children.Clear();
            var rank = 0;
            foreach (var (pp, v, err) in done.OrderByDescending(d => d.Mbps))
            {
                var best = rank++ == 0 && v > 0 && done.Count > 1;
                var bar = Ui.Bar(v > 0 ? Controls.SpeedGauge.Frac(v) : 0, v <= 0 ? P.Danger : best ? P.Success : P.Accent, 5);
                var row = Ui.V(6, Ui.Row(Ui.T($"{(best ? "★ " : "")}{Name(pp)}", 13, FontWeight.SemiBold),
                    Ui.T(v > 0 ? Ui.Ltr($"{v:0.0} Mbps") : L.S("failed"), 13, FontWeight.Bold, v <= 0 ? P.Danger : best ? P.Success : P.Text)), bar);
                if (v <= 0 && err != null) row.Children.Add(Ui.Caption(err, P.Text3));
                results.Children.Add(Ui.Enter(new Border { Background = P.B(best ? P.SuccessSoft : P.Fill), CornerRadius = Ui.RCtl, Padding = new Thickness(12, 8), Child = row }));
            }
        }

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < list.Count && !cts.IsCancellationRequested; i++)
            {
                var p = list[i];
                var idx = i;
                Dispatcher.UIThread.Post(() => { now.Text = Name(p); step.Text = list.Count > 1 ? L.F("speed_n", idx + 1, list.Count) : L.S("speed_dl"); gauge.Set(0, 0); });
                SpeedEngine.Result r;
                try { r = await SpeedEngine.RunAsync(p, (m, pr) => Dispatcher.UIThread.Post(() => gauge.Set(m, pr)), cts.Token, list.Count > 1 ? TimeSpan.FromSeconds(6) : null); }
                catch (OperationCanceledException) { break; }
                Dispatcher.UIThread.Post(() =>
                {
                    gauge.Set(Math.Max(0, r.Mbps), 1, true);
                    done.Add((p, r.Mbps, r.Error));
                    if (r.Mbps <= 0) step.Text = L.S("failed") + (r.Error != null ? ": " + r.Error : "");
                    Paint();
                });
                if (list.Count > 1) await Task.Delay(500);
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested && host.Content == null) return;
                var best = done.Where(d => d.Mbps > 0).OrderByDescending(d => d.Mbps).FirstOrDefault();
                if (best.Mbps > 0) { now.Text = (done.Count > 1 ? "★ " : "") + Name(best.P); gauge.Set(best.Mbps, 1, true); step.Text = cts.IsCancellationRequested ? L.S("stopped") : list.Count > 1 ? L.S("speed_done") : L.S("done"); }
                else if (cts.IsCancellationRequested) step.Text = L.S("stopped");
                stop.IsVisible = false; retry.IsVisible = true;
                after?.Invoke();
            });
        });
    }

    private readonly ProgressBar _testBar = new()
    {
        IsIndeterminate = true, IsVisible = false, Height = 3, MinHeight = 3, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 0, 0, 0), ZIndex = 5, Foreground = P.B(P.Accent), Background = Brushes.Transparent, CornerRadius = new CornerRadius(2),
    };
    private readonly Dictionary<string, int> _shown = [];
    private DispatcherTimer? _pulse;

    private Task PingAllAsync() => PingEngine.I.PingAllAsync(SatraCore.I.Profiles.ProfileItems.ToList());

    private void SetTesting(bool on)
    {
        _testBar.IsVisible = on;
        if (on && PingEngine.I.Total > 1) { _testBar.IsIndeterminate = false; _testBar.Maximum = PingEngine.I.Total; _testBar.Value = PingEngine.I.Done; }
        else _testBar.IsIndeterminate = true;
        if (on && _pulse == null)
        {
            var t = 0.0;
            _pulse = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Render, (_, _) =>
            {
                t += 0.05;
                var o = 0.45 + 0.55 * (0.5 + 0.5 * Math.Cos(t * Math.PI * 2 / 1.1));
                foreach (var h in _pingHosts.Values)
                    if (h.Content is Border { Tag: "pending" } b) b.Opacity = o;
            });
            _pulse.Start();
        }
        else if (!on) { _pulse?.Stop(); _pulse = null; }
    }

    public static string SubName(string? subId)
    {
        if (string.IsNullOrEmpty(subId)) return "";
        if (SatraState.I.Subs.TryGetValue(subId, out var m) && !string.IsNullOrWhiteSpace(m.Title)) return m.Title!;
        return SatraCore.I.Profiles.SubItems.FirstOrDefault(s => s.Id == subId)?.Remarks is { } r ? Ui.CleanName(r) : "";
    }

    /// <summary>Same thresholds/labels as Android SatraPingBadge: &lt;150 green, ≤250 amber, else red; "—", "timeout".</summary>
    public static Border PingPill(int delay, bool spinning = false)
    {
        var b = delay switch
        {
            _ when spinning => Ui.Pill("…", P.Accent, P.AccentSoft),
            >= 10_000 or < 0 => Ui.Pill("timeout", P.Danger, P.DangerSoft),
            > 0 and < 150 => Ui.Pill(Ui.Ltr($"{delay} ms"), P.Success, P.SuccessSoft),
            > 0 and <= 250 => Ui.Pill(Ui.Ltr($"{delay} ms"), P.Warning, P.WarningSoft),
            > 0 => Ui.Pill(Ui.Ltr($"{delay} ms"), P.Danger, P.DangerSoft),
            _ => Ui.Pill("—", P.Text3, P.Fill),
        };
        if (spinning) b.Tag = "pending";
        return b;
    }

    /// <summary>Fade + slight pop when a server's value changes (Android animates the badge color).</summary>
    private static Border Pop(Border b)
    {
        b.Opacity = 0;
        b.RenderTransform = TransformOperations.Parse("scale(0.85)");
        b.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(260), Easing = new BackEaseOut() },
        ];
        Dispatcher.UIThread.Post(() => { b.Opacity = 1; b.RenderTransform = TransformOperations.Parse("scale(1)"); }, DispatcherPriority.Background);
        return b;
    }

    public void Refresh()
    {
        RebuildChips();
        Rebuild(false);
        UpdatePingsInPlace();
    }

    private void UpdatePingsInPlace()
    {
        FlushPings();
        foreach (var p in SatraCore.I.Profiles.ProfileItems)
        {
            var id = p.IndexId ?? "";
            if (_pingHosts.TryGetValue(id, out var host))
            {
                var d = Delay(p);
                var pending = PingEngine.I.Get(id).Status == PingStatus.Pending;
                var key = pending ? int.MinValue : d;
                if (!(_shown.TryGetValue(id, out var prev) && prev == key && host.Content != null))
                {
                    var changed = _shown.ContainsKey(id) && !pending;
                    _shown[id] = key;
                    var pill = PingPill(d, pending);
                    host.Content = changed ? Pop(pill) : pill;
                }
            }
            if (_speedHosts.TryGetValue(id, out var sh) && !string.IsNullOrEmpty(p.SpeedVal) && p.SpeedVal != "0")
                sh.Content = Ui.Caption(decimal.TryParse(p.SpeedVal, out var mbs) ? $"↓ {mbs * 8:0.#} Mbps" : p.SpeedVal, P.Text2);
        }
    }

    private void RebuildChips()
    {
        _chips.Children.Clear();
        var pv = SatraCore.I.Profiles;
        Border Chip(string text, bool on, Action click)
        {
            var b = Ui.Pressable(new Border
            {
                CornerRadius = Ui.RPill, Padding = new Thickness(14, 7), Margin = new Thickness(0, 0, 8, 6), Cursor = new Cursor(StandardCursorType.Hand),
                Background = P.B(on ? P.Accent : P.Fill), Child = Ui.T(text, 13, FontWeight.SemiBold, on ? Colors.White : P.Text2),
            });
            b.PointerReleased += (_, _) => click();
            return b;
        }
        foreach (var s in pv.SubItems)
        {
            if (!string.IsNullOrEmpty(s.Id) && !s.Enabled) continue;
            var name = string.IsNullOrEmpty(s.Id) ? L.S("all") : SubName(s.Id) is { Length: > 0 } n ? n : s.Remarks;
            var on = !_favOnly && pv.SelectedSub?.Id == s.Id;
            _chips.Children.Add(Chip(name, on, () => { _favOnly = false; pv.SelectedSub = s; RebuildChips(); }));
        }
        _chips.Children.Add(Chip(L.S("favorites"), _favOnly, () => { _favOnly = !_favOnly; RebuildChips(); Rebuild(true); }));
    }

    private void Rebuild(bool force)
    {
        var items = SatraCore.I.Profiles.ProfileItems.ToList();
        var q = _search.Text?.Trim() ?? "";
        IEnumerable<ProfileItemModel> view = items;
        if (q.Length > 0) view = view.Where(p => (p.Remarks ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (p.Address ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        if (_favOnly) view = view.Where(p => SatraState.I.Favorites.Contains(Key(p)));
        var fav = SatraState.I.Favorites;
        view = _sortBest
            ? view.OrderBy(p => Delay(p) > 0 ? Delay(p) : int.MaxValue)
            : view.OrderByDescending(p => fav.Contains(Key(p)));
        var list = view.ToList();
        var active = AppManager.Instance.Config.IndexId;
        // Structure signature — delays excluded so ping does not rebuild the whole list
        var sig = string.Join(",", list.Select(p => $"{p.IndexId}:{NetBadges.ExitCc(p)}:{fav.Contains(Key(p))}:{p.IndexId == active}")) + $"|{_sortBest}|{q}|{_favOnly}";
        if (!force && sig == _sig)
        {
            UpdatePingsInPlace();
            return;
        }
        _sig = sig;
        _pingHosts.Clear();
        _shown.Clear();
        _speedHosts.Clear();
        _list.Children.Clear();
        if (list.Count == 0)
        {
            _list.Children.Add(Ui.Card(Ui.V(10, Ui.T(L.S("no_server"), 16, FontWeight.SemiBold), Ui.Caption(L.S("add_hint")),
                Ui.Btn(L.S("add_clip"), Ui.Kind.Primary, () => _ = AddAsync())), new Thickness(20)));
            return;
        }
        foreach (var p in list.Take(300))
            _list.Children.Add(RowFor(p, p.IndexId == active));
        UpdatePingsInPlace();
    }

    private Control RowFor(ProfileItemModel p, bool isActive)
    {
        var id = p.IndexId ?? "";
        var fav = SatraState.I.Favorites.Contains(Key(p));
        var star = Ui.IconBtn(fav ? "★" : "☆", () =>
        {
            if (!SatraState.I.Favorites.Remove(Key(p))) SatraState.I.Favorites.Add(Key(p));
            SatraState.Save();
            Rebuild(true);
        }, L.S("favorites"), fav ? P.Warning : P.Text3, 15);

        var exitCc = NetBadges.ExitCc(p);
        var flag = NetBadges.FlagView(exitCc, 24);
        flag.VerticalAlignment = VerticalAlignment.Center;

        var badges = new List<Control> { Ui.Pill(Proto(p), P.Text2, P.Fill, 10) };
        if (isActive) badges.Add(Ui.Pill(L.S("active"), P.Success, P.SuccessSoft, 10));

        var name = Ui.T(Ui.CleanName(p.Remarks ?? p.Address), 14.5, FontWeight.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var left = Ui.H(12, star, flag, Ui.V(4, name, Ui.H(6, badges.ToArray())));

        var pingHost = new ContentControl { Content = PingPill(Delay(p)) };
        var speedHost = new ContentControl();
        _pingHosts[id] = pingHost;
        _speedHosts[id] = speedHost;

        var right = new List<Control> { speedHost, pingHost };
        right.Add(Ui.IconBtn("◎", () => _ = PingEngine.I.PingOneAsync(p), L.S("ping"), P.Text2, 14));
        right.Add(Ui.IconBtn("↯", () => SpeedSheet([p]), L.S("speed"), P.Text2, 14));

        var card = Ui.Card(Ui.Row(left, Ui.H(6, right.ToArray())), new Thickness(12, 10), isActive ? P.Lerp(P.Surface, P.Success, 0.08) : null);
        if (isActive) { card.BorderBrush = P.B(P.Success, 0.45); card.BorderThickness = new Thickness(1); }
        card.Cursor = new Cursor(StandardCursorType.Hand);
        card.DoubleTapped += (_, _) => _ = UseAndConnect(p);
        card.PointerReleased += (s, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) != null) return;
            _ = SatraCore.I.UseAsync(p);
        };
        return card;
    }

    private static async Task UseAndConnect(ProfileItemModel p)
    {
        await SatraCore.I.UseAsync(p);
        if (SatraCore.I.Phase != ConnPhase.On) await SatraCore.I.ConnectAsync();
    }

    private static string Proto(ProfileItemModel p)
    {
        var s = p.ConfigType.ToString().ToUpperInvariant();
        if (!string.IsNullOrEmpty(p.Network) && p.Network != "tcp") s += "·" + p.Network.ToUpperInvariant();
        if (p.StreamSecurity is "reality") s += "·REALITY";
        return s;
    }

    public static string Key(ProfileItemModel p) => $"{p.ConfigType}:{p.Address}:{p.Port}:{Ui.CleanName(p.Remarks)}";

    /// <summary>Fresh in-memory result only; never falls back to a persisted old ping.</summary>
    public static int Delay(ProfileItemModel p) => PingEngine.I.Ms(p.IndexId);

    public static void FlushPings() { }
}
