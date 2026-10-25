using ServiceLib.Handler.Fmt;
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
        Last = this;
        _root.Children.Add(Ui.Row(Ui.Title(L.S("servers")), Ui.H(8,
            Ui.Btn(L.S("ping_all"), Ui.Kind.Ghost, () => _ = PingAllAsync(), "◎", 13),
            Ui.Btn(L.S("speed_all"), Ui.Kind.Ghost, () => SpeedSheet(SatraCore.I.Profiles.ProfileItems.ToList()), "↯", 13),
            AddButton())));

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

    private Button AddButton()
    {
        var b = Ui.Btn(L.S("add"), Ui.Kind.Tinted, null, "+", 13);
        var menu = new ContextMenu();
        menu.Items.Add(Mi(L.S("add_clip"), () => _ = AddAsync()));
        menu.Items.Add(Mi(L.S("ed_add") + "…", () => ServerEditor.Open()));
        menu.Items.Add(Mi(L.S("qr_image") + "…", () => _ = ImportQrImage()));
        menu.Items.Add(Mi(L.S("qr_screen"), () => _ = ScanScreen()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Mi(L.S("export_all"), () => _ = ExportAll()));
        menu.Items.Add(Mi(L.S("dedup"), () => _ = Dedup()));
        menu.Items.Add(Mi(L.S("remove_dead"), () => _ = RemoveDead()));
        b.ContextMenu = menu;
        b.Click += (_, _) => menu.Open(b);
        return b;
    }

    private static MenuItem Mi(string header, Action a)
    {
        var m = new MenuItem { Header = header };
        m.Click += (_, _) => a();
        return m;
    }

    /// <summary>Right-click on a server row: everything v2rayN offers per server, in plain words.</summary>
    private ContextMenu RowMenu(ProfileItemModel p)
    {
        var m = new ContextMenu();
        m.Items.Add(Mi(L.S("cm_connect"), () => _ = UseAndConnect(p)));
        m.Items.Add(Mi(L.S("cm_set_active"), () => _ = SatraCore.I.UseAsync(p)));
        m.Items.Add(new Separator());
        m.Items.Add(Mi(L.S("cm_edit"), () => ServerEditor.Open(p)));
        m.Items.Add(Mi(L.S("cm_duplicate"), () => _ = Do(async c => await ConfigHandler.CopyServer(c, [await Prof(p)]))));
        m.Items.Add(Mi(L.S("ping"), () => _ = PingEngine.I.PingOneAsync(p)));
        var mux = new MenuItem { Header = L.S("mux"), ToggleType = MenuItemToggleType.CheckBox };
        m.Opening += async (_, _) =>
        {
            mux.IsChecked = MuxOn(p);
            // xhttp multiplexes on its own; upstream v2rayN/Xray force mux off there
            mux.IsEnabled = (await Prof(p)).GetNetwork() != "xhttp";
        };
        mux.Click += (_, _) => _ = ToggleMux(p);
        m.Items.Add(mux);
        m.Items.Add(Mi(L.S("speed"), () => SpeedSheet([p])));
        m.Items.Add(new Separator());
        m.Items.Add(Mi(L.S("ed_copy_link"), () => _ = CopyLink(p)));
        m.Items.Add(Mi(L.S("cm_qr"), () => ServerEditor.ShowQr(p)));
        var move = new MenuItem { Header = L.S("cm_move") };
        move.Items.Add(Mi(L.S("cm_top"), () => _ = Move(p, EMove.Top)));
        move.Items.Add(Mi(L.S("cm_up"), () => _ = Move(p, EMove.Up)));
        move.Items.Add(Mi(L.S("cm_down"), () => _ = Move(p, EMove.Down)));
        move.Items.Add(Mi(L.S("cm_bottom"), () => _ = Move(p, EMove.Bottom)));
        m.Items.Add(move);
        m.Items.Add(new Separator());
        m.Items.Add(Mi(L.S("cm_delete"), () => _ = Delete(p)));
        return m;
    }

    private static async Task<ProfileItem> Prof(ProfileItemModel p) { var id = p.IndexId; return (await Task.Run(() => AppManager.Instance.GetProfileItem(id)))!; }

    private static async Task Do(Func<Config, Task<int>> f)
    {
        await f(AppManager.Instance.Config);
        await SatraCore.I.Profiles.RefreshServers();
        SatraCore.I.RaiseChanged();
    }

    private static async Task CopyLink(ProfileItemModel p)
    {
        var link = FmtHandler.GetShareUri(await Prof(p));
        if (string.IsNullOrEmpty(link)) { SatraShell.I.Toast(L.S("ed_no_link")); return; }
        await SatraShell.I.Copy(link);
    }

    private static async Task Move(ProfileItemModel p, EMove how)
    {
        var ids = SatraCore.I.Profiles.ProfileItems.Select(x => x.IndexId).ToList();
        var i = ids.IndexOf(p.IndexId);
        if (i < 0) return;
        await Do(c => ConfigHandler.MoveServer(c, ids, i, how));
    }

    private static async Task Delete(ProfileItemModel p)
    {
        var prof = await Prof(p);
        await Do(c => ConfigHandler.RemoveServers(c, [prof]));
        SatraShell.I.Toast(L.S("cm_deleted"), L.S("undo"), () => _ = Do(async c => { prof.IndexId = ""; return await ConfigHandler.AddServer(c, prof); }));
    }

    private static async Task ImportQrImage()
    {
        var files = await SatraShell.I.Window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }]
        });
        if (files.Count == 0) return;
        var path = Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]);
        if (path == null) return;
        await ImportQrFile(path);
    }

    public static async Task ScanScreen()
    {
        var text = await Parity.ScanScreenAsync();
        if (string.IsNullOrEmpty(text)) { SatraShell.I.Toast(L.S("qr_none")); return; }
        await SatraCore.I.AddFromClipboardAsync(text);
    }

    /// <summary>Per-server multiplexing (v2rayN "Mux"); reconnects if this server is live.</summary>
    public static bool MuxOn(ProfileItemModel p) => SatraState.I.Mux.TryGetValue(Key(p), out var v) && v;

    /// <summary>Key for a ProfileItem (same shape as <see cref="Key(ProfileItemModel)"/>), for the core-config hook.</summary>
    public static string Key(ProfileItem p) => $"{p.ConfigType}:{p.Address}:{p.Port}:{Ui.CleanName(p.Remarks)}";

    public static async Task ToggleMux(ProfileItemModel p)
    {
        var on = !MuxOn(p);
        SatraState.I.Mux[Key(p)] = on;
        SatraState.Save();
        var prof = await Prof(p);
        prof.MuxEnabled = on;
        await Do(c => ConfigHandler.AddServer(c, prof));
        SatraShell.I.Toast(L.S(on ? "mux_on" : "mux_off"));
        if (SatraCore.I.Active?.IndexId == p.IndexId && SatraCore.I.Phase == ConnPhase.On) _ = SatraCore.I.ReconnectAsync();
    }

    public static async Task ImportQrFile(string path)
    {
        var text = await Task.Run(() => QRCodeUtils.ParseBarcode(path));
        if (string.IsNullOrEmpty(text)) { SatraShell.I.Toast(L.S("qr_none")); return; }
        await SatraCore.I.AddFromClipboardAsync(text);
    }

    /// <summary>All share links (one per line) to the clipboard — backup or move to another device.</summary>
    public static async Task ExportAll()
    {
        var ids = SatraCore.I.Profiles.ProfileItems.Select(x => x.IndexId).ToList();
        var links = await Task.Run(() => ids.Select(id => AppManager.Instance.GetProfileItem(id).Result).Where(x => x != null).Select(x => FmtHandler.GetShareUri(x!)).Where(l => !string.IsNullOrEmpty(l)).ToList());
        if (links.Count == 0) { SatraShell.I.Toast(L.S("ed_no_link")); return; }
        await SatraShell.I.Copy(string.Join("\n", links));
        SatraShell.I.Toast(L.F("exported_n", links.Count));
    }

    public static async Task Dedup()
    {
        var before = SatraCore.I.Profiles.ProfileItems.Count;
        await Do(async c => (await ConfigHandler.DedupServerList(c, SatraCore.I.Profiles.ProfileItems.FirstOrDefault()?.Subid ?? "")).Item2);
        SatraShell.I.Toast(L.F("removed_n", before - SatraCore.I.Profiles.ProfileItems.Count));
    }

    /// <summary>Removes servers whose last ping timed out (run "Ping all" first). Undo restores them.</summary>
    public static async Task RemoveDead()
    {
        var dead = SatraCore.I.Profiles.ProfileItems.Where(p => PingEngine.I.Get(p.IndexId).Status == PingStatus.Timeout).ToList();
        if (dead.Count == 0) { SatraShell.I.Toast(L.S("remove_dead_none")); return; }
        var profs = new List<ProfileItem>();
        foreach (var d in dead) profs.Add(await Prof(d));
        await Do(c => ConfigHandler.RemoveServers(c, profs));
        SatraShell.I.Toast(L.F("removed_n", profs.Count), L.S("undo"), () => _ = Do(async c => { foreach (var x in profs) { x.IndexId = ""; await ConfigHandler.AddServer(c, x); } return 0; }));
    }

    private async Task AddAsync() => await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());

    private readonly ContentControl _speedPanel = new() { Margin = new Thickness(0, 0, 0, 12) };
    private CancellationTokenSource? _speedCts;

    /// <summary>Inline (same page, no popup) download-only speed test with a live gauge; results sorted fastest first.</summary>
    public static ServersPage? Last;
    public void DevCtx(int n) { if (_list.Children.Count > n && _list.Children[n] is Control c) c.ContextMenu?.Open(c); }
    public void DevSpeed(int n) => SpeedSheet(n < 0 ? SatraCore.I.Profiles.ProfileItems.Take(3).ToList() : [SatraCore.I.Profiles.ProfileItems[n]]);
    private void SpeedSheet(List<ProfileItemModel> list) => SpeedInline(_speedPanel, list, () => UpdatePingsInPlace());

    public static void SpeedInline(ContentControl host, IReadOnlyList<ProfileItemModel?> list, Action? after = null, bool compact = false)
    {
        if (list.Count == 0) return;
        if (host.Tag is CancellationTokenSource old) old.Cancel();
        var cts = new CancellationTokenSource();
        host.Tag = cts;
        var gauge = new Controls.SpeedLive { HorizontalAlignment = HorizontalAlignment.Stretch };
        if (compact) gauge.Height = 170;
        var now = Ui.T("", 15, FontWeight.SemiBold);
        now.HorizontalAlignment = HorizontalAlignment.Center;
        var step = Ui.Caption("", P.Text3);
        step.HorizontalAlignment = HorizontalAlignment.Center;
        step.TextWrapping = TextWrapping.Wrap;
        step.TextAlignment = TextAlignment.Center;
        var results = new StackPanel { Spacing = 6 };
        var done = new List<(ProfileItemModel? P, double Mbps, string? Err)>();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        void Close() { cts.Cancel(); host.Content = null; }
        Button stop = null!, retry = null!, close = null!;
        stop = Ui.Btn(L.S("stop"), Ui.Kind.Ghost, () => cts.Cancel(), size: 13);
        retry = Ui.Btn(L.S("try_again"), Ui.Kind.Tinted, () => SpeedInline(host, list, after, compact), "↻", 13);
        close = Ui.Btn(L.S("close"), Ui.Kind.Ghost, Close, size: 13);
        retry.IsVisible = false;
        btns.Children.AddRange([stop, retry, close]);
        now.HorizontalAlignment = HorizontalAlignment.Left; step.HorizontalAlignment = HorizontalAlignment.Left; step.TextAlignment = TextAlignment.Left;
        var sv = new ScrollViewer { Content = results, MaxHeight = 260, IsVisible = list.Count > 1 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Ui.V(2, Ui.Row(Ui.T(L.S("speed_test"), 15, FontWeight.SemiBold), Ui.Caption(L.S("speed_dl_only"), P.Text3)), step));
        Grid.SetColumn(btns, 1); head.Children.Add(btns);
        host.Content = Ui.Enter(Ui.Card(Ui.V(12, head, gauge, sv), new Thickness(18, 14)));

        static string Name(ProfileItemModel? p) => p == null ? (SatraCore.I.Phase == ConnPhase.On ? L.S("speed_via_vpn") : L.S("speed_direct")) : Ui.CleanName(p.Remarks);
        void Paint()
        {
            results.Children.Clear();
            var rank = 0;
            foreach (var (pp, v, err) in done.OrderByDescending(d => d.Mbps))
            {
                var best = rank++ == 0 && v > 0 && done.Count > 1;
                var bar = Ui.Bar(v > 0 ? Math.Min(1, v / Math.Max(1, done.Max(d => d.Mbps))) : 0, v <= 0 ? P.Danger : best ? P.Success : P.Accent, 5);
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
                Dispatcher.UIThread.Post(() => { now.Text = Name(p); step.Text = (list.Count > 1 ? L.F("speed_n", idx + 1, list.Count) : L.S("speed_dl")) + " · " + Name(p); gauge.Reset(); gauge.Via = Name(p); });
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
                if (best.Mbps > 0) { now.Text = (done.Count > 1 ? "★ " : "") + Name(best.P); if (done.Count > 1) { gauge.Reset(); gauge.Via = "★ " + Name(best.P); } gauge.Set(best.Mbps, 1, true); step.Text = cts.IsCancellationRequested ? L.S("stopped") : list.Count > 1 ? L.S("speed_done") : L.S("done"); }
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
        card.ContextMenu = RowMenu(p);
        card.DoubleTapped += (_, _) => _ = UseAndConnect(p);
        card.PointerReleased += (s, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
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
