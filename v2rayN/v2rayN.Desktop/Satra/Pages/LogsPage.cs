using System.Collections.ObjectModel;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Logs: level filter, search, live follow, copy/export, clear. Virtualized list, appended in batches — never rebuilt or jumping.</summary>
public class LogsPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
    private readonly ObservableCollection<LogBuffer.Line> _rows = new();
    private readonly ItemsControl _list;
    private readonly ScrollViewer _sv;
    private readonly TextBox _search;
    private readonly TextBlock _count = Ui.Caption("", P.Text3);
    private readonly ToggleSwitch _follow;
    private readonly List<Border> _segs = new();
    private LogBuffer.Lvl _min = LogBuffer.Lvl.Info;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    public static LogsPage? I { get; private set; }

    public Control View => _root;

    public LogsPage()
    {
        I = this;

        string[] names = [L.S("log_all"), L.S("log_info"), L.S("log_warn"), L.S("log_error")];
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var i = 0; i < names.Length; i++)
        {
            var idx = i;
            var b = Ui.Pressable(new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 7), Cursor = new Cursor(StandardCursorType.Hand), Child = Ui.T(names[i], 13, FontWeight.Medium) });
            b.PointerReleased += (_, _) => SetLevel((LogBuffer.Lvl)idx);
            _segs.Add(b);
            seg.Children.Add(b);
        }
        var segHost = new Border { Background = P.B(P.Fill), CornerRadius = Ui.RCtl, Padding = new Thickness(3), Child = seg };
        _search = Ui.Input(L.S("log_search"));
        _search.MinWidth = 160;
        _search.TextChanged += (_, _) => Rebuild();
        _follow = new ToggleSwitch { IsChecked = true, OnContent = null, OffContent = null, MinWidth = 0 };
        _follow.IsCheckedChanged += (_, _) => { if (_follow.IsChecked == true) ScrollEnd(); };
        var tools = Ui.H(8,
            Ui.H(4, Ui.T(L.S("log_follow"), 13, FontWeight.Medium, P.Text2), _follow),
            Ui.Btn(L.S("log_copy"), Ui.Kind.Ghost, () => _ = CopyAsync(), null, 12.5),
            Ui.Btn(L.S("log_export"), Ui.Kind.Ghost, () => _ = ExportAsync(), null, 12.5),
            Ui.Btn(L.S("log_clear"), Ui.Kind.Ghost, () => { LogBuffer.Clear(); _rows.Clear(); UpdateCount(); }, null, 12.5));
        _root.Children.Add(Ui.Row(Ui.Title(L.S("logs")), tools));
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 14, 0, 12) };
        bar.Children.Add(segHost);
        _search.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_search, 1); bar.Children.Add(_search);
        Grid.SetRow(bar, 1); _root.Children.Add(bar);

        _list = new ItemsControl
        {
            ItemsSource = _rows, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(10, 8),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<LogBuffer.Line>((l, _) => l == null ? new TextBlock() : new SelectableTextBlock
            {
                Text = $"{l.At:HH:mm:ss}  {Strip(l.Text)}", Margin = new Thickness(0, 1.5), FontFamily = new FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"), FontSize = 12,
                TextWrapping = TextWrapping.Wrap, Foreground = P.B(l.Level switch { LogBuffer.Lvl.Error => P.Danger, LogBuffer.Lvl.Warn => P.Warning, LogBuffer.Lvl.Debug => P.Text3, _ => P.Text2 }),
            }),
        };
        _sv = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var card = new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _sv };
        var empty = Ui.Caption(L.S("log_empty"), P.Text3);
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
        empty.IsHitTestVisible = false;
        var stack = new Grid { Children = { card, empty } };
        _rows.CollectionChanged += (_, _) => empty.IsVisible = _rows.Count == 0;
        Grid.SetRow(stack, 2); _root.Children.Add(stack);
        _count.Margin = new Thickness(4, 8, 0, 0);
        Grid.SetRow(_count, 3); _root.Children.Add(_count);
        // user scrolls up → stop following; back at the bottom → follow again
        _sv.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => { if (e.Delta.Y > 0) _follow.IsChecked = false; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _timer.Tick += (_, _) => Pump();
        _root.AttachedToVisualTree += (_, _) => { _timer.Start(); Pump(); };
        _root.DetachedFromVisualTree += (_, _) => _timer.Stop();
        PaintSeg();
        Rebuild();
    }

    private bool Match(LogBuffer.Line l)
    {
        if (_min != LogBuffer.Lvl.Debug && l.Level < _min) return false;
        var q = _search.Text?.Trim();
        return string.IsNullOrEmpty(q) || l.Text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // "All" (index 0) maps to Debug, i.e. everything
    public void SetLevel(LogBuffer.Lvl idx) { _min = idx; PaintSeg(); Rebuild(); }

    private void PaintSeg()
    {
        for (var i = 0; i < _segs.Count; i++)
        {
            var on = (int)_min == i;
            _segs[i].Background = on ? P.B(P.Raised) : Brushes.Transparent;
            ((TextBlock)_segs[i].Child!).Foreground = P.B(on ? P.Text : P.Text2);
        }
    }

    private void Pump()
    {
        var add = LogBuffer.Drain().Where(Match).ToList();
        if (add.Count == 0) return;
        foreach (var l in add) _rows.Add(l);
        while (_rows.Count > 3000) _rows.RemoveAt(0);
        UpdateCount();
        if (_follow.IsChecked == true) ScrollEnd();
    }

    private void Rebuild()
    {
        LogBuffer.Drain();
        _rows.Clear();
        foreach (var l in LogBuffer.Snapshot().Where(Match).TakeLast(3000)) _rows.Add(l);
        UpdateCount();
        if (_follow.IsChecked == true) ScrollEnd();
    }

    private void UpdateCount() => _count.Text = L.F("log_count", _rows.Count);

    private void ScrollEnd()
    {
        if (_rows.Count > 0) Dispatcher.UIThread.Post(() => _sv.ScrollToEnd(), DispatcherPriority.Background);
    }

    private string Text() => string.Join(Environment.NewLine, _rows.Select(l => $"{l.At:yyyy-MM-dd HH:mm:ss} {l.Text}"));

    private async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(_root)?.Clipboard is { } cb) { await cb.SetTextAsync(Text()); SatraShell.I.Toast(L.S("copied")); }
    }

    private async Task ExportAsync()
    {
        try
        {
            var sp = TopLevel.GetTopLevel(_root)?.StorageProvider;
            if (sp == null) return;
            var f = await sp.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = $"satran-{DateTime.Now:yyyyMMdd-HHmm}.log", DefaultExtension = "log" });
            if (f == null) return;
            await using var s = await f.OpenWriteAsync();
            await using var w = new StreamWriter(s);
            await w.WriteAsync(Text());
            SatraShell.I.Toast(L.S("log_saved"));
        }
        catch (Exception ex) { Logging.SaveLog("logs", ex); }
    }

    private static readonly System.Text.RegularExpressions.Regex Ts = new(@"^\d{4}[/-]\d{2}[/-]\d{2}[ T]\d{2}:\d{2}:\d{2}(\.\d+)?\s*", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Drops the core's own date/time prefix (we show one already).</summary>
    public static string Strip(string t) => Ts.Replace(t, "");

    public void Refresh() { }
}
