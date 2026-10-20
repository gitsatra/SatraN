using Avalonia.Layout;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Servers list: structure rebuilds only when membership/order changes; ping/speed update in place.</summary>
public class ServersPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
    private readonly StackPanel _list = new() { Spacing = 8, Margin = new Thickness(0, 0, 0, 28) };
    private readonly WrapPanel _chips = new();
    private readonly TextBox _search = Ui.Input(L.S("search"));
    private readonly Dictionary<string, string?> _cdn = [];
    private readonly Dictionary<string, string?> _entry = [];
    private readonly Dictionary<string, ContentControl> _pingHosts = [];
    private readonly Dictionary<string, ContentControl> _speedHosts = [];
    private bool _favOnly, _sortBest, _pinging;
    private string _sig = "";

    public Control View => _root;

    public ServersPage()
    {
        _root.Children.Add(Ui.Row(Ui.Title(L.S("servers")), Ui.H(8,
            Ui.Btn(L.S("ping_all"), Ui.Kind.Ghost, () => _ = PingAllAsync(), "◎", 13),
            Ui.Btn(L.S("speed_all"), Ui.Kind.Ghost, () => _ = SatraCore.I.RunTestAsync(ESpeedActionType.Speedtest), "↯", 13),
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
        Grid.SetRow(bar, 2);
        _root.Children.Add(bar);

        var sv = new ScrollViewer { Content = _list };
        Grid.SetRow(sv, 3);
        _root.Children.Add(sv);

        SatraCore.I.Profiles.ProfileItems.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() => Rebuild(false));
        SatraCore.I.Profiles.SpeedTestUpdated += () => Dispatcher.UIThread.Post(UpdatePingsInPlace);
    }

    private static Control Center(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    private async Task AddAsync() => await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());

    private async Task PingAllAsync()
    {
        _pinging = true;
        UpdatePingsInPlace(); // show … on empty
        try { await SatraCore.I.RunTestAsync(ESpeedActionType.Realping); }
        finally { _pinging = false; UpdatePingsInPlace(); }
    }

    public static string SubName(string? subId)
    {
        if (string.IsNullOrEmpty(subId)) return "";
        if (SatraState.I.Subs.TryGetValue(subId, out var m) && !string.IsNullOrWhiteSpace(m.Title)) return m.Title!;
        return SatraCore.I.Profiles.SubItems.FirstOrDefault(s => s.Id == subId)?.Remarks is { } r ? Ui.CleanName(r) : "";
    }

    public static Border PingPill(int delay, bool spinning = false) => delay switch
    {
        _ when spinning && delay == 0 => Ui.Pill("…", P.Accent, P.AccentSoft),
        > 0 and < 300 => Ui.Pill(Ui.Ltr($"{delay} ms"), P.Success, P.SuccessSoft),
        > 0 and < 800 => Ui.Pill(Ui.Ltr($"{delay} ms"), P.Warning, P.WarningSoft),
        > 0 => Ui.Pill(Ui.Ltr($"{delay} ms"), P.Danger, P.DangerSoft),
        < 0 => Ui.Pill("timeout", P.Danger, P.DangerSoft),
        _ => Ui.Pill(Ui.Ltr("— ms"), P.Text3, P.Fill),
    };

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
                host.Content = PingPill(Delay(p), _pinging && p.Delay == 0);
            if (_speedHosts.TryGetValue(id, out var sh) && !string.IsNullOrEmpty(p.SpeedVal) && p.SpeedVal != "0")
                sh.Content = Ui.Caption(p.SpeedVal + (decimal.TryParse(p.SpeedVal, out _) ? " MB/s" : ""), P.Text2);
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
        right.Add(Ui.IconBtn("◎", () => _ = SatraCore.I.RunTestAsync(ESpeedActionType.Realping, [p]), L.S("ping"), P.Text2, 14));
        right.Add(Ui.IconBtn("↯", () => _ = SatraCore.I.RunTestAsync(ESpeedActionType.Speedtest, [p]), L.S("speed"), P.Text2, 14));

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

    public static int Delay(ProfileItemModel p)
    {
        var k = Key(p);
        if (p.Delay > 0)
        {
            if (!SatraState.I.Pings.TryGetValue(k, out var old) || old != p.Delay) { SatraState.I.Pings[k] = p.Delay; _pingDirty = true; }
            return p.Delay;
        }
        if (p.Delay < 0) return p.Delay;
        return SatraState.I.Pings.TryGetValue(k, out var v) ? v : 0;
    }

    private static bool _pingDirty;
    public static void FlushPings() { if (_pingDirty) { _pingDirty = false; SatraState.Save(); } }
}
