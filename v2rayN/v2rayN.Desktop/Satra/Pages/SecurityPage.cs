using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>
/// Security check: four sections of real checks that tick off one by one. Rows are built once and only
/// change in place (no rebuilds, no jumps). Tap a row for what it means and how to fix it.
/// Runs only when the user starts it — on the running config, or (when off) on the selected one through a temporary core.
/// </summary>
public class SecurityPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(0, 18, 0, 28) };
    private readonly List<CheckResult> _res = SecurityCheck.Fresh();
    private readonly Dictionary<string, Row> _rows = new();
    private readonly Dictionary<string, TextBlock> _catPts = new();
    private readonly ScoreRing _ring = new();
    private readonly TextBlock _verdict = Ui.T(L.S("sc_intro"), 16, FontWeight.SemiBold);
    private readonly TextBlock _ctx = Ui.Caption("", P.Text2);
    private readonly TextBlock _progress = Ui.Caption("", P.Text3);
    private readonly Button _run;
    private bool _busy;
    private int? _last;
    private CancellationTokenSource? _cts;

    public Control View => _root;
    public static SecurityPage? Last;
    public Task DevRun() => RunAsync();
    public int? LastScore => _last;
    private bool _unreach;
    public IReadOnlyList<CheckResult> Results => _res;

    public SecurityPage()
    {
        Last = this;
        _run = Ui.Btn(L.S("sc_run"), Ui.Kind.Primary, () => { if (_busy) _cts?.Cancel(); else _ = RunAsync(); }, "⟳", 13);
        _root.Children.Add(Ui.Row(Ui.Title(L.S("security")), _run));
        _verdict.TextWrapping = TextWrapping.Wrap;
        _ctx.TextWrapping = TextWrapping.Wrap;
        var desc = Ui.Caption(L.S("sc_desc2"), P.Text3);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var txt = Ui.V(6, _verdict, _ctx, _progress, desc);
        txt.VerticalAlignment = VerticalAlignment.Center;
        txt.Margin = new Thickness(22, 0, 0, 0);
        Grid.SetColumn(txt, 1);
        head.Children.Add(_ring);
        head.Children.Add(txt);
        _body.Children.Add(Ui.Card(head, new Thickness(22, 18)));

        foreach (var cat in SecurityCheck.Cats)
        {
            var pts = Ui.Caption("", P.Text3);
            _catPts[cat] = pts;
            var hdr = Ui.Row(Ui.H(8, Ui.T(CatIcon(cat), 13, FontWeight.Bold, P.Text2), Ui.Section(L.S("sc_cat_" + cat))), pts);
            hdr.Margin = new Thickness(4, 6, 4, 0);
            _body.Children.Add(hdr);
            if (cat == "tor") _torHdr = hdr;
            var list = new StackPanel();
            var first = true;
            foreach (var r in _res.Where(r => r.Cat == cat))
            {
                if (!first) list.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(60, 0, 0, 0) });
                first = false;
                var row = new Row(r);
                _rows[r.Key] = row;
                list.Children.Add(row.View);
            }
            _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = list });
            if (cat == "tor") _body.Children.Add(Ui.Caption(L.S("sc_tor_note"), P.Text3));
        }
        var sv = _sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
        _ring.Final(null);
        PaintHeader();
    }

    private static string CatIcon(string c) => c switch { "leak" => "◎", "tls" => "⚿", "sys" => "⚙\uFE0E", _ => "⧗" };

    public static bool FocusTor;
    private Control? _torHdr;
    private ScrollViewer? _sv;

    public void Refresh()
    {
        PaintHeader();
        if (FocusTor && _torHdr != null)
        {
            FocusTor = false;
            var h = _torHdr;
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (_sv != null && h.TranslatePoint(new Point(0, 0), _body) is { } pt) _sv.Offset = new Vector(0, Math.Max(0, pt.Y - 6));
                else h.BringIntoView();
            }, TimeSpan.FromMilliseconds(320));
        }
    }

    private void PaintHeader()
    {
        // which config the scan will use — always the current truth (fixes the stale "SatraN is off" after a server switch)
        var c = SatraCore.I;
        var a = c.Active;
        if (!_busy)
        {
            _ctx.Text = a == null ? L.S("no_server")
                : c.Phase == ConnPhase.On ? L.F("sc_ctx_on", Ui.CleanName(a.Remarks))
                : L.F("sc_ctx_off", Ui.CleanName(a.Remarks));
            _run.IsEnabled = a != null;
        }
        foreach (var cat in SecurityCheck.Cats)
        {
            var items = _res.Where(r => r.Cat == cat).ToList();
            var max = items.Sum(r => r.Max);
            var done = items.Count(r => r.Done);
            _catPts[cat].Text = max == 0 ? (done > 0 ? L.S("sc_not_scored") : "") : done == items.Count ? L.F("sc_points", items.Sum(r => r.Points), items.Where(r => r.State != CheckState.Unknown).Sum(r => r.Max)) : "";
        }
    }

    private async Task RunAsync()
    {
        if (_busy || SatraCore.I.Active == null) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        ((TextBlock)((StackPanel)_run.Content!).Children[^1]).Text = L.S("sc_stop");
        _verdict.Text = L.S("sc_running");
        _ctx.Text = SatraCore.I.Phase == ConnPhase.On ? L.F("sc_ctx_on", Ui.CleanName(SatraCore.I.Active.Remarks)) : L.F("sc_ctx_temp", Ui.CleanName(SatraCore.I.Active.Remarks));
        _last = null; _unreach = false;
        _ring.Scanning(0);
        try
        {
            var (score, _) = await SecurityCheck.RunAsync(_res, () => Dispatcher.UIThread.Post(Tick), SatraCore.I.Active, _cts.Token);
            bool Ok(string k) => _res.FirstOrDefault(r => r.Key == k)?.State == CheckState.Pass;
            _unreach = !Ok("sc_ip") && !Ok("sc_tls");
            _last = _unreach ? null : score;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.SaveLog("security", ex); }
        finally
        {
            _busy = false;
            ((TextBlock)((StackPanel)_run.Content!).Children[^1]).Text = L.S("sc_run");
            Dispatcher.UIThread.Post(() =>
            {
                Tick();
                _ring.Final(_last);
                _verdict.Text = _last is int v ? L.S(v >= 85 ? "sc_good" : v >= 60 ? "sc_fair" : "sc_bad") : _unreach ? L.S("sc_unreach") : L.S("sc_intro");
                var weak = _res.Where(r => r.Scored && r.State is CheckState.Fail or CheckState.Warn).OrderByDescending(r => r.Max - r.Points).Take(2).Select(r => L.S(r.Key)).ToList();
                _progress.Text = _last == null ? "" : weak.Count == 0 ? L.S("sc_all_good") : L.F("sc_focus", string.Join(" · ", weak));
                PaintHeader();
            });
        }
    }

    private void Tick()
    {
        foreach (var r in _res) _rows[r.Key].Update(r);
        var total = _res.Count;
        var done = _res.Count(r => r.Done);
        if (_busy) { _ring.Scanning((double)done / total); _progress.Text = $"{done}/{total}"; }
        PaintHeader();
    }

    /// <summary>One check row: built once, updated in place; tap to expand.</summary>
    private sealed class Row
    {
        public readonly Border View;
        private readonly Border _dot;
        private readonly TextBlock _glyph, _detail, _pts, _what, _fix;
        private readonly Border _pill;
        private readonly StackPanel _more;
        private CheckState _state = CheckState.Pending;
        private readonly string _key;
        private readonly int _max;

        public Row(CheckResult r)
        {
            _key = r.Key; _max = r.Max;
            _glyph = new TextBlock { Text = "·", FontSize = 14, FontWeight = FontWeight.Bold, Foreground = P.B(P.Text3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _dot = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = P.B(P.Fill), Child = _glyph, VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = TransformOperations.Parse("scale(1)"),
                Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(380) },
                    new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new BackEaseOut() },
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(500), Easing = new SineEaseInOut() },
                },
            };
            _detail = Ui.Caption(L.S(r.Key + "_d"), P.Text3);
            _detail.MaxWidth = 560;
            _detail.HorizontalAlignment = HorizontalAlignment.Left;
            _detail.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) } };
            _pts = Ui.T("", 12, FontWeight.SemiBold, P.Text3);
            _pts.MinWidth = 54;
            _pts.TextAlignment = TextAlignment.Right;
            _pill = Ui.Pill(" ", P.Text3, P.Fill, 10.5);
            _pill.Opacity = 0;
            _pill.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) } };
            _what = Ui.Caption(L.S(r.Key + "_d"), P.Text2);
            _what.MaxWidth = 560;
            _what.HorizontalAlignment = HorizontalAlignment.Left;
            _fix = Ui.T("", 12.5, FontWeight.Medium, P.Text, TextWrapping.Wrap);
            _fix.MaxWidth = 560;
            _more = Ui.V(6, _what, new Border { Background = P.B(P.Raised), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9), Child = _fix, HorizontalAlignment = HorizontalAlignment.Left });
            _more.IsVisible = false;
            _more.Margin = new Thickness(0, 6, 0, 2);
            var title = Ui.T(L.S(r.Key), 14.5, FontWeight.SemiBold);
            title.HorizontalAlignment = HorizontalAlignment.Left;
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            top.Children.Add(title);
            var pill = _pill; Grid.SetColumn(pill, 1); top.Children.Add(pill);
            _pts.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(_pts, 2); top.Children.Add(_pts);
            var text = Ui.V(3, top, _detail, _more);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(_dot);
            text.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            View = new Border { Padding = new Thickness(14, 12), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = grid };
            View.PointerReleased += (_, _) => _more.IsVisible = !_more.IsVisible;
            if (_max == 0) _pts.Text = "";
        }

        public void Update(CheckResult r)
        {
            var changed = r.State != _state;
            _state = r.State;
            var (glyph, col, soft, status) = r.State switch
            {
                CheckState.Pass => ("✓", P.Success, P.SuccessSoft, L.S("sc_status_pass")),
                CheckState.Warn => ("!", P.Warning, P.WarningSoft, L.S("sc_status_warn")),
                CheckState.Fail => ("✕", P.Danger, P.DangerSoft, L.S("sc_status_fail")),
                CheckState.Unknown => ("?", P.Text2, P.Fill, L.S("sc_status_unknown")),
                CheckState.Running => ("•", P.Accent, P.AccentSoft, ""),
                _ => ("·", P.Text3, P.Fill, ""),
            };
            _glyph.Text = glyph;
            _glyph.Foreground = P.B(col);
            _dot.Background = P.B(soft);
            if (changed && r.Done)
            {
                // soft "tick": pop slightly, settle back
                _dot.RenderTransform = TransformOperations.Parse("scale(1.18)");
                DispatcherTimer.RunOnce(() => _dot.RenderTransform = TransformOperations.Parse("scale(1)"), TimeSpan.FromMilliseconds(170));
            }
            _dot.Opacity = r.State == CheckState.Running ? 0.6 : 1;
            if (_pill.Child is TextBlock pt) { pt.Text = status.Length == 0 ? " " : status; pt.Foreground = P.B(col); }
            _pill.Background = P.B(soft);
            _pill.Opacity = status.Length == 0 ? 0 : 1;
            _detail.Text = r.State switch
            {
                CheckState.Running => L.S("sc_checking"),
                CheckState.Pending => L.S(r.Key + "_d"),
                _ => string.IsNullOrEmpty(r.Detail) ? L.S(r.Key + "_d") : r.Detail,
            };
            _detail.Foreground = P.B(r.State == CheckState.Running ? P.Accent : r.Done ? P.Text2 : P.Text3);
            _pts.Text = !r.Done ? "" : r.Max == 0 ? L.S("sc_info") : r.State == CheckState.Unknown ? $"–/{r.Max}" : $"{r.Points}/{r.Max}";
            _pts.Foreground = P.B(!r.Done || r.Max == 0 ? P.Text3 : col);
            _fix.Text = r.State switch
            {
                CheckState.Pass => "✓ " + L.S(r.Key + "_why"),
                CheckState.Pending or CheckState.Running => L.S("sc_fix_after"),
                _ => L.S("sc_fix") + " " + L.S(r.Key + "_fix"),
            };
        }
    }
}
