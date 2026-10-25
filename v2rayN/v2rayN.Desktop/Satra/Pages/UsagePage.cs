using Avalonia.Animation;
using Avalonia.Layout;
using Avalonia.Threading;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

using ServiceLib.Satra;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Usage: charts, live per-process traffic from Clash API, and local limits.</summary>
public class UsagePage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(0, 18, 0, 28) };
    private readonly BarChart _data = new() { BarColor = P.Accent, Format = Ui.Bytes };
    private readonly BarChart _hours = new() { BarColor = P.Info, Height = 120, Format = v => $"{v:0.#} h" };
    private readonly TextBlock _sumDown = Ui.Num(Ui.T("", 22, FontWeight.Bold)), _sumUp = Ui.Num(Ui.T("", 22, FontWeight.Bold)), _sumTotal = Ui.Num(Ui.T("", 22, FontWeight.Bold)), _sumHours = Ui.Num(Ui.T("", 22, FontWeight.Bold));
    private readonly StackPanel _limits = new() { Spacing = 0 };
    private readonly StackPanel _live = new() { Spacing = 0 };
    private readonly StackPanel _top = new() { Spacing = 0 };
    private TextBox _search = null!;
    private char _topKind = 'c';
    private int _topDays = 7;

    private string _topSig = "";
    private SlideSegmented _kindSeg = null!;
    private readonly Border _topHost = new() { MinHeight = 120 };

    /// History list (Configs / Apps / Sites). Rebuilt only when what it shows changes, so the page never jumps.
    private void BuildTop(bool force = false)
    {
        var q = _search.Text?.Trim() ?? "";
        if (_topKind == 'c')
        {
            var per = UsageHistory.PerConfig(_topDays);
            var sigC = $"c{_topDays}{q}{SatraCore.I.Active?.IndexId}" + string.Join(",", per.Select(kv => $"{kv.Key}{(kv.Value.Up + kv.Value.Down) >> 16}"));
            if (!force && sigC == _topSig) return;
            _topSig = sigC;
            _top.Children.Clear();
            BuildConfigs(per);
            return;
        }
        var rows = UsageHistory.Top(_topKind, _topDays, q);
        var blockedSig = string.Join(",", SatraState.I.Limits.Where(l => l.Enabled).SelectMany(l => l.Values));
        var sig = $"{_topKind}{_topDays}{q}{blockedSig}{L.Lang}" + string.Join(",", rows.Select(r => $"{r.Name}{r.Bytes >> 16}"));
        if (!force && sig == _topSig) return;
        _topSig = sig;
        _top.Children.Clear();
        if (rows.Count == 0)
        {
            _top.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("usage_history_empty"), P.Text3) });
            return;
        }
        var max = Math.Max(1, rows[0].Bytes);
        var first = true;
        foreach (var (name, bytes) in rows)
        {
            if (!first) _top.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(_topKind == 'a' ? 60 : 14, 0, 0, 0) });
            first = false;
            if (_topKind == 'a') { _top.Children.Add(AppRow(name, bytes, max)); continue; }
            var key = "host:" + name;
            var blocked = SatraState.I.Limits.Any(l => l.Enabled && l.Values.Any(v => v.Equals(name, StringComparison.OrdinalIgnoreCase)));
            var btn = blocked
                ? Ui.Btn(L.S("unblock_site"), Ui.Kind.Tinted, () => UnblockProcess(key, name), null, 12)
                : Ui.Btn(L.S("block_site"), Ui.Kind.Ghost, () => BlockProcess(key, name), null, 12);
            _top.Children.Add(new Border { Padding = new Thickness(14, 10), Child = Ui.V(6,
                Ui.Row(Ui.H(10, SiteBadge(name), Ui.T(name, 13.5, FontWeight.SemiBold)), Ui.H(10, Ui.T(Ui.Bytes(bytes), 13, FontWeight.Bold, P.Text2), btn)),
                Ui.Bar((double)bytes / max, blocked ? P.Danger : P.Accent, 4)) });
        }
    }

    /// One app in history: icon + friendly name, ↓/↑, share bar; tap → destinations.
    private Control AppRow(string name, long bytes, long max)
    {
        var t = UsageHistory.AppTotals(name, _topDays);
        var hosts = UsageHistory.AppHosts(name, _topDays);
        var blocked = IsAppBlocked(name, name);
        var title = Ui.T(AppIdentity.Cached(name)?.Name ?? UsageModel.Pretty(name), 14, FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = Ui.Caption(Ui.Ltr($"{name}") + (hosts.Count > 0 ? " · " + string.Format(L.S("u_n_sites"), hosts.Count) : ""), P.Text3);
        sub.TextWrapping = TextWrapping.NoWrap;
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        var right = Ui.V(2, Ui.T(Ui.Bytes(bytes), 13.5, FontWeight.Bold, blocked ? P.Danger : P.Text), Ui.Caption(Ui.Ltr($"↓ {Ui.Bytes(t.Down)}  ↑ {Ui.Bytes(t.Up)}"), P.Text3));
        right.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var c in right.Children.OfType<TextBlock>()) c.HorizontalAlignment = HorizontalAlignment.Right;
        var chev = Ui.T("›", 20, FontWeight.Medium, P.Text3);
        chev.VerticalAlignment = VerticalAlignment.Center;
        if (L.Lang == "fa") chev.Text = "‹";
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        var icon = AppIcon(name, 34);
        var mid = Ui.V(5, Ui.V(1, title, sub), Ui.Bar((double)bytes / max, blocked ? P.Danger : P.Accent, 4));
        Grid.SetColumn(mid, 1); Grid.SetColumn(right, 2); Grid.SetColumn(chev, 3);
        g.Children.Add(icon); g.Children.Add(mid); g.Children.Add(right); g.Children.Add(chev);
        var row = new Border { Padding = new Thickness(14, 10), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = g };
        row.PointerEntered += (_, _) => row.Background = P.B(P.Fill);
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, _) => AppSheet(name);
        _ = AppIdentity.GetAsync(name).ContinueWith(r => Dispatcher.UIThread.Post(() => { if (r.Result.Name.Length > 0) title.Text = r.Result.Name; }));
        return row;
    }

    private static bool IsAppBlocked(string key, string name) =>
        SatraState.I.Limits.Any(l => l.Enabled && l.Type == "process" && l.Values.Any(v =>
            string.Equals(v, key, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(v), name, StringComparison.OrdinalIgnoreCase)));

    /// Rounded app icon (real icon when found, else a tinted letter badge); swaps in when it loads.
    public static Control AppIcon(string name, double size)
    {
        var hue = unchecked((uint)name.ToLowerInvariant().Aggregate(17, (h, ch) => unchecked(h * 31 + ch))) % 6;
        var tint = hue switch { 0 => P.Accent, 1 => P.Info, 2 => P.Success, 3 => P.Warning, 4 => Color.Parse("#A78BFA"), _ => Color.Parse("#F472B6") };
        var letter = Ui.T(UsageModel.Pretty(name).FirstOrDefault(char.IsLetterOrDigit).ToString().ToUpperInvariant(), size * 0.44, FontWeight.Bold, tint);
        letter.HorizontalAlignment = HorizontalAlignment.Center;
        letter.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = P.B(tint, 0.16), ClipToBounds = true, Child = letter, VerticalAlignment = VerticalAlignment.Center };
        void Set(AppIdentity.Info i)
        {
            if (i.Icon == null) return;
            b.Background = Brushes.Transparent;
            b.Child = new Image { Source = i.Icon, Width = size, Height = size, Stretch = Stretch.Uniform };
        }
        if (AppIdentity.Cached(name) is { } c) Set(c);
        else _ = AppIdentity.GetAsync(name).ContinueWith(r => Dispatcher.UIThread.Post(() => Set(r.Result)));
        return b;
    }

    private static Control SiteBadge(string host)
    {
        var t = Ui.T(host.FirstOrDefault(char.IsLetterOrDigit).ToString().ToUpperInvariant(), 11, FontWeight.Bold, P.Info);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = P.B(P.InfoSoft), Child = t };
    }

    /// App detail: totals, every destination it reached with MB per host, block/unblock.
    private void AppSheet(string name)
    {
        var info = AppIdentity.Cached(name);
        var t = UsageHistory.AppTotals(name, _topDays);
        var hosts = UsageHistory.AppHosts(name, _topDays, 200);
        string? path;
        lock (SatraState.I.AppPaths) path = SatraState.I.AppPaths.GetValueOrDefault(name);
        Control Tile(string l, string v, Color c) => new Border { Background = P.B(P.Fill), CornerRadius = Ui.RCtl, Padding = new Thickness(14, 10), Child = Ui.V(2, Ui.Caption(l, P.Text3), Ui.Num(Ui.T(v, 17, FontWeight.Bold, c))) };
        var tiles = new UniformGrid { Rows = 1, ColumnSpacing = 10 };
        tiles.Children.Add(Tile(L.S("download"), Ui.Bytes(t.Down), P.Text));
        tiles.Children.Add(Tile(L.S("upload"), Ui.Bytes(t.Up), P.Text));
        tiles.Children.Add(Tile(L.S("total"), Ui.Bytes(t.Total), P.Accent));
        var head = Ui.H(14, AppIcon(name, 48), Ui.V(2, Ui.T(info?.Name ?? UsageModel.Pretty(name), 18, FontWeight.Bold),
            Ui.Caption(Ui.Ltr(path ?? name), P.Text3), Ui.Caption(SpanText(), P.Text3)));
        var list = new StackPanel();
        if (hosts.Count == 0) list.Children.Add(new Border { Padding = new Thickness(14, 12), Child = Ui.Caption(L.S("u_no_hosts"), P.Text3) });
        var max = Math.Max(1, hosts.FirstOrDefault()?.Total ?? 1);
        var first = true;
        foreach (var h in hosts)
        {
            if (!first) list.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            first = false;
            var hn = Ui.T(Ui.Ltr(h.Host), 13, FontWeight.Medium);
            hn.TextTrimming = TextTrimming.CharacterEllipsis;
            list.Children.Add(new Border { Padding = new Thickness(14, 9), Child = Ui.V(5,
                Ui.Row(hn, Ui.H(10, Ui.Caption(Ui.Ltr($"↓ {Ui.Bytes(h.Down)}  ↑ {Ui.Bytes(h.Up)}"), P.Text3), Ui.T(Ui.Bytes(h.Total), 13, FontWeight.Bold, P.Text2))),
                Ui.Bar((double)h.Total / max, P.Info, 3)) });
        }
        var blocked = IsAppBlocked(path ?? name, name);
        Action close = () => { };
        var act = blocked
            ? Ui.Btn(L.S("unblock_app"), Ui.Kind.Tinted, () => { UnblockProcess(path ?? name, name); close(); })
            : Ui.Btn(L.S("block_app"), Ui.Kind.Danger, () => { BlockProcess(path ?? name, name); close(); });
        var body = Ui.V(14, head, tiles, Ui.Row(Ui.Section(string.Format(L.S("u_hosts"), hosts.Count)), act),
            new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = new ScrollViewer { MaxHeight = 380, Content = list } });
        close = SatraShell.I.Sheet(L.S("u_app_detail"), body, 620);
    }

    internal void DevOpenApp(string name) => AppSheet(name);
    internal void DevKind(int i) => _kindSeg.Select(i, true);

    private string SpanText() => _topDays switch { 1 => L.S("today"), 7 => L.S("week"), _ => L.S("month") };

    /// Usage per config/server (all modes), saved per day; names resolved from the current profile list.
    private void BuildConfigs(Dictionary<string, (long Up, long Down)> per)
    {
        var profiles = SatraCore.I.Profiles.ProfileItems.GroupBy(p => p.IndexId ?? "").ToDictionary(g => g.Key, g => g.First());
        var q = _search.Text?.Trim() ?? "";
        var rows = per.Select(kv => (Id: kv.Key, kv.Value.Up, kv.Value.Down, P: profiles.GetValueOrDefault(kv.Key)))
            .Select(r => (r.Id, r.Up, r.Down, Name: r.P != null ? Ui.CleanName(r.P.Remarks) : L.S("deleted_config"), Sub: r.P != null ? ServersPage.SubName(r.P.Subid) : ""))
            .Where(r => q.Length == 0 || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Sub.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Up + r.Down).Take(60).ToList();
        if (rows.Count == 0)
        {
            _top.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("usage_cfg_empty"), P.Text3) });
            return;
        }
        var max = Math.Max(1, rows[0].Up + rows[0].Down);
        var first = true;
        foreach (var r in rows)
        {
            if (!first) _top.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            first = false;
            var active = r.Id == SatraCore.I.Active?.IndexId;
            _top.Children.Add(new Border { Padding = new Thickness(14, 10), Child = Ui.V(6,
                Ui.Row(Ui.V(2, Ui.T((active ? "● " : "") + r.Name, 13.5, FontWeight.SemiBold), Ui.Caption(r.Sub, P.Text3)),
                    Ui.V(2, Ui.T(Ui.Bytes(r.Up + r.Down), 13, FontWeight.Bold, P.Text2), Ui.Caption(Ui.Ltr($"↓ {Ui.Bytes(r.Down)}  ↑ {Ui.Bytes(r.Up)}"), P.Text3))),
                Ui.Bar((double)(r.Up + r.Down) / max, active ? P.Success : P.Accent, 4)) });
        }
    }

    private int _days = 7;
    private readonly Border _seg7, _seg30;
    private readonly DispatcherTimer _liveTimer;
    private Dictionary<string, (ulong up, ulong down)> _prevSnap = new();
    private DateTime _prevAt = DateTime.MinValue;

    public Control View => _root;

    public UsagePage()
    {
        Border Seg(string t, int d)
        {
            var b = Ui.Pressable(new Border { CornerRadius = Ui.RPill, Padding = new Thickness(14, 6), Cursor = new Cursor(StandardCursorType.Hand), Child = Ui.T(t, 13, FontWeight.SemiBold) });
            b.PointerReleased += (_, _) => { _days = d; Refresh(); };
            return b;
        }
        _seg7 = Seg(L.S("last7"), 7);
        _seg30 = Seg(L.S("last30"), 30);
        var seg = new Border { Background = P.B(P.Fill), CornerRadius = Ui.RPill, Padding = new Thickness(3), Child = Ui.H(2, _seg7, _seg30) };
        _root.Children.Add(Ui.Row(Ui.Title(L.S("usage")), seg));

        var sums = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        Control S(string l, TextBlock v, int col) { var x = Ui.V(4, Ui.Section(l), v); Grid.SetColumn(x, col); return x; }
        sums.Children.Add(S(L.S("download"), _sumDown, 0));
        sums.Children.Add(S(L.S("upload"), _sumUp, 1));
        sums.Children.Add(S(L.S("total"), _sumTotal, 2));
        sums.Children.Add(S(L.S("hours_per_day").Split(' ')[0], _sumHours, 3));
        _body.Children.Add(Ui.Card(sums, new Thickness(20, 16)));
        // Persisted history: top apps / top sites per day, week, month — with search and one-tap block
        _search = Ui.Input(L.S("search_usage"));
        _search.Width = 220;
        _search.TextChanged += (_, _) => BuildTop();
        var kindSeg = _kindSeg = new SlideSegmented([L.S("top_configs"), L.S("top_apps"), L.S("top_sites")], 0, i => { _topKind = i switch { 0 => 'c', 1 => 'a', _ => 'd' }; SwapTop(); }) { Width = 330, HorizontalAlignment = HorizontalAlignment.Left };
        var spanSeg = new SlideSegmented([L.S("today"), L.S("week"), L.S("month")], 1, i => { _topDays = i switch { 0 => 1, 1 => 7, _ => 30 }; SwapTop(); }) { Width = 260 };
        _body.Children.Add(Ui.Row(Ui.Section(L.S("usage_history")), _search));
        _body.Children.Add(Ui.Row(kindSeg, spanSeg));
        _topHost.Background = P.B(P.Surface);
        _topHost.CornerRadius = Ui.RCard;
        _topHost.ClipToBounds = true;
        _topHost.Child = _top;
        _top.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };
        _body.Children.Add(_topHost);
        _body.Children.Add(Ui.Section(L.S("live_apps")));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _live });
        _body.Children.Add(Ui.Card(Ui.V(12, Ui.Section(L.S("data_per_day")), _data), new Thickness(20, 16)));
        _body.Children.Add(Ui.Card(Ui.V(12, Ui.Section(L.S("hours_per_day")), _hours), new Thickness(20, 16)));
        _body.Children.Add(Ui.Row(Ui.Section(L.S("local_limits")), Ui.Btn(L.S("add_limit"), Ui.Kind.Tinted, () => AddLimitSheet(), "+", 13)));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _limits });
        _body.Children.Add(Ui.Caption(L.S("server_limits") + " → " + L.S("subs") + " · " + L.S("limits"), P.Text3));
        var sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveTimer.Tick += (_, _) => _ = RefreshLiveAsync();
        _root.AttachedToVisualTree += (_, _) => { _liveTimer.Start(); _ = RefreshLiveAsync(); };
        _root.DetachedFromVisualTree += (_, _) => _liveTimer.Stop();
    }

    /// Switch the history list in place: keep the card's height while the new rows come in (no jump), soft fade.
    private void SwapTop()
    {
        _topHost.MinHeight = Math.Max(120, _topHost.Bounds.Height);
        _top.Opacity = 0.35;
        BuildTop(true);
        DispatcherTimer.RunOnce(() => { _top.Opacity = 1; _topHost.MinHeight = 120; }, TimeSpan.FromMilliseconds(170));
    }

    public void Refresh()
    {
        foreach (var (b, on) in new[] { (_seg7, _days == 7), (_seg30, _days == 30) })
        {
            b.Background = on ? P.B(P.Raised) : Brushes.Transparent;
            ((TextBlock)b.Child!).Foreground = P.B(on ? P.Text : P.Text2);
        }
        var days = Enumerable.Range(0, _days).Select(i => DateTime.Today.AddDays(i - _days + 1)).ToList();
        var u = days.Select(d => SatraState.I.Days.GetValueOrDefault(d.ToString("yyyy-MM-dd"))).ToList();
        var labels = days.Select(d => _days == 7 ? d.ToString("ddd", L.Culture) : d.Day.ToString()).ToArray();
        _data.SetData(u.Select(x => (double)((x?.Up ?? 0) + (x?.Down ?? 0))).ToArray(), labels);
        _hours.SetData(u.Select(x => (x?.Seconds ?? 0) / 3600.0).ToArray(), labels);
        _sumDown.Text = Ui.Bytes(u.Sum(x => x?.Down ?? 0));
        _sumUp.Text = Ui.Bytes(u.Sum(x => x?.Up ?? 0));
        _sumTotal.Text = Ui.Bytes(u.Sum(x => (x?.Up ?? 0) + (x?.Down ?? 0)));
        _sumHours.Text = $"{u.Sum(x => x?.Seconds ?? 0) / 3600.0:0.#} h";
        BuildLimits();
        BuildTop();
        _ = RefreshLiveAsync();
    }

    private async Task RefreshLiveAsync()
    {
        try
        {
            var ret = await ClashApiManager.Instance.GetConnections();
            var now = DateTime.UtcNow;
            var agg = new Dictionary<string, (string name, ulong up, ulong down, string? host)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in ret?.connections ?? [])
            {
                var meta = c.metadata;
                if (meta == null) continue;
                var key = meta.processPath.IsNotEmpty() ? meta.processPath! : (meta.process.IsNotEmpty() ? meta.process! : (meta.host.IsNotEmpty() ? "host:" + meta.host : "unknown"));
                var name = meta.process.IsNotEmpty()
                    ? meta.process!
                    : (meta.processPath.IsNotEmpty() ? Path.GetFileName(meta.processPath) : (meta.host ?? "unknown"));
                if (!agg.TryGetValue(key, out var cur)) cur = (name, 0, 0, meta.host ?? meta.sniffHost);
                agg[key] = (cur.name, cur.up + c.upload, cur.down + c.download, cur.host ?? meta.host ?? meta.sniffHost);
            }

            var dt = _prevAt == DateTime.MinValue ? 2.0 : Math.Max(0.5, (now - _prevAt).TotalSeconds);
            var rows = agg.Select(kv =>
            {
                _prevSnap.TryGetValue(kv.Key, out var prev);
                var dUp = kv.Value.up >= prev.up ? kv.Value.up - prev.up : 0UL;
                var dDown = kv.Value.down >= prev.down ? kv.Value.down - prev.down : 0UL;
                var bps = (dUp + dDown) / dt;
                return (key: kv.Key, kv.Value.name, total: kv.Value.up + kv.Value.down, bps, kv.Value.host);
            }).OrderByDescending(r => r.bps).ThenByDescending(r => r.total).Take(20).ToList();

            _prevSnap = agg.ToDictionary(kv => kv.Key, kv => (kv.Value.up, kv.Value.down), StringComparer.OrdinalIgnoreCase);
            _prevAt = now;

            await Dispatcher.UIThread.InvokeAsync(() => BuildLive(rows));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("UsageLive", ex);
        }
    }

    private string _liveSig = "";
    private void BuildLive(List<(string key, string name, ulong total, double bps, string? host)> rows)
    {
        var sig = string.Join(",", rows.Select(r => $"{r.key}{(long)r.bps >> 12}{r.total >> 16}{r.host}")) + SatraState.I.Limits.Count;
        if (sig == _liveSig) return;
        _liveSig = sig;
        _live.Children.Clear();
        if (rows.Count == 0)
        {
            _live.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("live_apps_empty"), P.Text3) });
            return;
        }
        var first = true;
        foreach (var r in rows)
        {
            if (!first) _live.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            first = false;
            var blocked = SatraState.I.Limits.Any(l => l.Enabled && l.Type == "process" && l.Values.Any(v =>
                string.Equals(v, r.key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(v), r.name, StringComparison.OrdinalIgnoreCase)));
            var key = r.key;
            var name = r.name;
            var right = Ui.H(6,
                Ui.Caption(Ui.Speed((long)r.bps), P.Text2),
                Ui.Pill(Ui.Bytes((long)r.total), P.Text2, P.Fill),
                blocked
                    ? Ui.Btn(L.S("unblock_app"), Ui.Kind.Tinted, () => UnblockProcess(key, name), null, 12)
                    : Ui.Btn(L.S("block_app"), Ui.Kind.Danger, () => BlockProcess(key, name), null, 12));
            var sub = r.host.IsNotEmpty() ? r.host! : (r.key.Contains(Path.DirectorySeparatorChar) || r.key.Contains('/') ? r.key : "");
            var isApp = !r.key.StartsWith("host:", StringComparison.OrdinalIgnoreCase) && r.name != "unknown";
            if (isApp) AppIdentity.Remember(name, r.key.Contains('/') || r.key.Contains('\\') ? r.key : null);
            var title = Ui.T(isApp ? AppIdentity.Cached(name)?.Name ?? name : name, 14, FontWeight.SemiBold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var subTb = Ui.Caption(Ui.Ltr(sub), P.Text3);
            subTb.TextWrapping = TextWrapping.NoWrap;
            subTb.TextTrimming = TextTrimming.CharacterEllipsis;
            var left = Ui.H(12, isApp ? AppIcon(name, 30) : SiteBadge(name), Ui.V(2, title, string.IsNullOrEmpty(sub) ? new TextBlock() : subTb));
            var row = new Border { Padding = new Thickness(14, 10), Background = Brushes.Transparent, Child = Ui.Row(left, right) };
            if (isApp)
            {
                row.Cursor = new Cursor(StandardCursorType.Hand);
                row.PointerReleased += (_, e) => { if (e.Source is Visual v && v.FindAncestorOfType<Button>() == null) AppSheet(name); };
                _ = AppIdentity.GetAsync(name).ContinueWith(t => Dispatcher.UIThread.Post(() => title.Text = t.Result.Name));
            }
            _live.Children.Add(row);
        }
    }

    private void BlockProcess(string key, string name)
    {
        var match = key.StartsWith("host:", StringComparison.OrdinalIgnoreCase) ? key["host:".Length..] : key;
        var type = key.StartsWith("host:", StringComparison.OrdinalIgnoreCase) ? "domain" : "process";
        var existing = SatraState.I.Limits.FirstOrDefault(l => l.Type == type && l.Values.Any(v => string.Equals(v, match, StringComparison.OrdinalIgnoreCase)));
        if (existing != null)
        {
            existing.Enabled = true;
            existing.QuotaBytes = 0;
            existing.Exhausted = true;
        }
        else
        {
            SatraState.I.Limits.Add(new LocalLimit
            {
                Name = name,
                Type = type,
                Values = [match],
                QuotaBytes = 0,
                Exhausted = true,
                Enabled = true,
            });
        }
        SatraState.Save();
        _ = SatraCore.I.ApplyLocalLimitsAsync();
        _limSig = "";
        _liveSig = "";
        BuildLimits();
        BuildTop(true);
        _ = RefreshLiveAsync();
    }

    private void UnblockProcess(string key, string name)
    {
        var match = key.StartsWith("host:", StringComparison.OrdinalIgnoreCase) ? key["host:".Length..] : key;
        SatraState.I.Limits.RemoveAll(l =>
            l.Type is "process" or "domain" &&
            l.Values.Any(v => string.Equals(v, match, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(Path.GetFileName(v), name, StringComparison.OrdinalIgnoreCase)));
        SatraState.Save();
        _ = SatraCore.I.ApplyLocalLimitsAsync();
        _limSig = "";
        _liveSig = "";
        BuildLimits();
        BuildTop(true);
        _ = RefreshLiveAsync();
    }

    private string _limSig = "";
    private void BuildLimits()
    {
        var sig = string.Join(",", SatraState.I.Limits.Select(l => $"{l.Id}{l.UsedBytes >> 20}{l.Exhausted}{l.Enabled}"));
        if (sig == _limSig && _limits.Children.Count > 0) return;
        _limSig = sig;
        _limits.Children.Clear();
        if (SatraState.I.Limits.Count == 0)
        {
            _limits.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("no_limits"), P.Text3) });
            return;
        }
        var first = true;
        foreach (var l in SatraState.I.Limits)
        {
            if (!first) _limits.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            first = false;
            var cap = l.QuotaBytes + l.TopupBytes;
            var tracked = l.Type == "all";
            var frac = tracked ? (cap > 0 ? Math.Min(1, (double)l.UsedBytes / cap) : 1) : (l.Exhausted || l.QuotaBytes == 0 ? 1 : 0);
            var what = l.Type switch { "domain" => L.S("site"), "process" => L.S("app"), _ => L.S("all_traffic") };
            var status = l.Exhausted || (!tracked && l.QuotaBytes == 0)
                ? Ui.Pill(L.S("blocked"), P.Danger, P.DangerSoft)
                : tracked ? Ui.Pill($"{Ui.Bytes(l.UsedBytes)} / {Ui.Bytes(cap)}", P.Text2, P.Fill) : Ui.Pill(Ui.Bytes(cap) + " " + SubsPage.PeriodText(l.Period), P.Text2, P.Fill);
            var lim = l;
            var right = Ui.H(4, status,
                Ui.IconBtn("+", () => { lim.TopupBytes += 500L << 20; lim.Exhausted = false; _ = SatraCore.I.ApplyLocalLimitsAsync(); _limSig = ""; BuildLimits(); }, L.S("topup") + " 500 MB", P.Success, 15),
                Ui.Switch(l.Enabled, v => { lim.Enabled = v; _ = SatraCore.I.ApplyLocalLimitsAsync(); }),
                Ui.IconBtn("🗑", () => { SatraState.I.Limits.Remove(lim); _ = SatraCore.I.ApplyLocalLimitsAsync(); _limSig = ""; BuildLimits(); }, L.S("delete"), P.Danger, 14));
            var head = Ui.Row(Ui.V(2, Ui.T(l.Name, 14.5, FontWeight.SemiBold), Ui.Caption($"{what} · {string.Join(", ", l.Values)} · {SubsPage.PeriodText(l.Period)}", P.Text3)), right);
            _limits.Children.Add(new Border { Padding = new Thickness(14, 12), Child = Ui.V(8, head, Ui.Bar(frac, P.Level(frac))) });
        }
    }

    private void AddLimitSheet()
    {
        var type = new ComboBox { ItemsSource = new[] { L.S("all_traffic"), L.S("site"), L.S("app") }, SelectedIndex = 0, MinWidth = 150 };
        var vals = Ui.Input("youtube.com, instagram.com  /  telegram.exe");
        var mb = Ui.Input("MB", "1024");
        mb.Width = 100;
        var per = new ComboBox { ItemsSource = new[] { L.S("per_day"), L.S("per_week"), L.S("per_month") }, SelectedIndex = 0 };
        var note = Ui.Caption("", P.Text3);
        void UpdNote()
        {
            vals.IsVisible = type.SelectedIndex != 0;
            note.Text = type.SelectedIndex == 0
                ? L.S("limit_all_note")
                : L.S("limit_site_note");
        }
        type.SelectionChanged += (_, _) => UpdNote();
        UpdNote();
        Action close = () => { };
        var save = Ui.Btn(L.S("save"), Ui.Kind.Primary, () =>
        {
            if (!long.TryParse(mb.Text, out var m) || m < 0) return;
            var t = type.SelectedIndex switch { 1 => "domain", 2 => "process", _ => "all" };
            var values = (vals.Text ?? "").Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
            if (t != "all" && values.Count == 0) return;
            SatraState.I.Limits.Add(new LocalLimit
            {
                Name = t == "all" ? L.S("all_traffic") : values[0], Type = t, Values = values, QuotaBytes = m << 20,
                Period = per.SelectedIndex switch { 1 => "weekly", 2 => "monthly", _ => "daily" },
            });
            _ = SatraCore.I.ApplyLocalLimitsAsync();
            _limSig = "";
            BuildLimits();
            close();
        });
        close = SatraShell.I.Sheet(L.S("add_limit"), Ui.V(12, Ui.Section(L.S("what")), type, vals, Ui.H(8, mb, Mb(), per), note, save), 480);
    }

    private static TextBlock Mb()
    {
        var t = Ui.T("MB", 13, FontWeight.Medium, P.Text2);
        t.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        return t;
    }
}
