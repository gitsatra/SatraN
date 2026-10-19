using Avalonia.Layout;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Connection Security Check: score ring + one row per check, re-run on demand.</summary>
public class SecurityPage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(0, 18, 0, 28) };
    private readonly List<CheckResult> _res = [];
    private readonly TextBlock _score = Ui.Num(Ui.T("–", 44, FontWeight.Bold));
    private readonly TextBlock _verdict = Ui.T(L.S("sc_intro"), 15, FontWeight.SemiBold);
    private readonly Border _ring = new() { Width = 112, Height = 112, CornerRadius = new CornerRadius(56), BorderThickness = new Thickness(6) };
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Button _run;
    private bool _busy;
    private int? _last;

    public Control View => _root;

    public SecurityPage()
    {
        _run = Ui.Btn(L.S("sc_run"), Ui.Kind.Primary, () => _ = RunAsync(), "⟳", 13);
        _root.Children.Add(Ui.Row(Ui.Title(L.S("security")), _run));
        _score.HorizontalAlignment = HorizontalAlignment.Center;
        _score.VerticalAlignment = VerticalAlignment.Center;
        _ring.Child = _score;
        _ring.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(500) } };
        _body.Children.Add(Ui.Card(Ui.H(22, _ring, Ui.V(6, _verdict, Ui.Caption(L.S("sc_desc")))), new Thickness(22, 18)));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _rows });
        var sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
        foreach (var k in SecurityCheck.Keys) _res.Add(new CheckResult { Key = k, State = CheckState.Pending, Detail = "" });
        Paint();
    }

    public void Refresh() => Paint();

    private async Task RunAsync()
    {
        if (_busy) return;
        _busy = true;
        _run.IsEnabled = false;
        _verdict.Text = L.S("sc_running");
        try
        {
            _last = await SecurityCheck.RunAsync(_res, () => Dispatcher.UIThread.Post(Paint));
        }
        finally
        {
            _busy = false;
            _run.IsEnabled = true;
            Dispatcher.UIThread.Post(Paint);
        }
    }

    private void Paint()
    {
        var c = _last is int s ? (s >= 85 ? P.Success : s >= 60 ? P.Warning : P.Danger) : P.FillStrong;
        _ring.BorderBrush = P.B(c);
        _score.Text = _last?.ToString() ?? "–";
        _score.Foreground = P.B(_last == null ? P.Text3 : c);
        if (!_busy && _last is int v) _verdict.Text = L.S(v >= 85 ? "sc_good" : v >= 60 ? "sc_fair" : "sc_bad");
        _rows.Children.Clear();
        var i = 0;
        foreach (var r in _res)
        {
            if (i++ > 0) _rows.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(56, 0, 0, 0) });
            var (glyph, col, soft) = r.State switch
            {
                CheckState.Pass => ("✓", P.Success, P.SuccessSoft),
                CheckState.Warn => ("!", P.Warning, P.WarningSoft),
                CheckState.Fail => ("✕", P.Danger, P.DangerSoft),
                CheckState.Unknown => ("?", P.Text2, P.Fill),
                _ => (_busy ? "…" : "·", P.Text3, P.Fill),
            };
            var dot = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = P.B(soft), Child = new TextBlock { Text = glyph, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = P.B(col), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var row = Ui.Row(Ui.H(14, dot, Ui.V(2, Ui.T(L.S(r.Key), 14.5, FontWeight.SemiBold), Ui.Caption(r.Detail.Length > 0 ? r.Detail : L.S(r.Key + "_d"), P.Text2))));
            _rows.Children.Add(new Border { Padding = new Thickness(14, 12), Child = row });
        }
    }
}
