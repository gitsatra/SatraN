using Avalonia.Layout;
using Avalonia.Threading;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

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
    private char _topKind = 'a';
    private int _topDays = 7;

    private void BuildTop()
    {
        _top.Children.Clear();
        var rows = UsageHistory.Top(_topKind, _topDays, _search.Text);
        if (rows.Count == 0)
        {
            _top.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("usage_history_empty"), P.Text3) });
            return;
        }
        var max = Math.Max(1, rows[0].Bytes);
        var first = true;
        foreach (var (name, bytes) in rows)
        {
            if (!first) _top.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            first = false;
            var key = _topKind == 'd' ? "host:" + name : name;
            var blocked = SatraState.I.Limits.Any(l => l.Enabled && l.Values.Any(v => v.Equals(name, StringComparison.OrdinalIgnoreCase)));
            var btn = blocked
                ? Ui.Btn(L.S(_topKind == 'd' ? "unblock_site" : "unblock_app"), Ui.Kind.Tinted, () => { UnblockProcess(key, name); BuildTop(); }, null, 12)
                : Ui.Btn(L.S(_topKind == 'd' ? "block_site" : "block_app"), Ui.Kind.Ghost, () => { BlockProcess(key, name); BuildTop(); }, null, 12);
            _top.Children.Add(new Border { Padding = new Thickness(14, 10), Child = Ui.V(6,
                Ui.Row(Ui.T(name, 13.5, FontWeight.SemiBold), Ui.H(10, Ui.T(Ui.Bytes(bytes), 13, FontWeight.Bold, P.Text2), btn)),
                Ui.Bar((double)bytes / max, blocked ? P.Danger : P.Accent, 4)) });
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
        _body.Children.Add(Ui.Card(Ui.V(12, Ui.Section(L.S("data_per_day")), _data), new Thickness(20, 16)));
        _body.Children.Add(Ui.Card(Ui.V(12, Ui.Section(L.S("hours_per_day")), _hours), new Thickness(20, 16)));
        // Persisted history: top apps / top sites per day, week, month — with search and one-tap block
        _search = Ui.Input(L.S("search_usage"));
        _search.Width = 220;
        _search.TextChanged += (_, _) => BuildTop();
        var kindSeg = SettingsPage.Segmented([L.S("top_apps"), L.S("top_sites")], 0, i => { _topKind = i == 0 ? 'a' : 'd'; BuildTop(); });
        var spanSeg = SettingsPage.Segmented([L.S("today"), L.S("week"), L.S("month")], 1, i => { _topDays = i switch { 0 => 1, 1 => 7, _ => 30 }; BuildTop(); });
        _body.Children.Add(Ui.Row(Ui.Section(L.S("usage_history")), _search));
        _body.Children.Add(Ui.H(10, kindSeg, spanSeg));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _top });
        _body.Children.Add(Ui.Section(L.S("live_apps")));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _live });
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

    private void BuildLive(List<(string key, string name, ulong total, double bps, string? host)> rows)
    {
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
            _live.Children.Add(new Border
            {
                Padding = new Thickness(14, 10),
                Child = Ui.Row(Ui.V(2, Ui.T(name, 14, FontWeight.SemiBold), string.IsNullOrEmpty(sub) ? new TextBlock() : Ui.Caption(sub, P.Text3)), right),
            });
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
        BuildLimits();
        BuildTop();
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
        BuildLimits();
        BuildTop();
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
