using Avalonia.Layout;
using Avalonia.Media;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Security Check with live per-row phases (like SatraNG Android).</summary>
public class SecurityPage : IPage
{
    private static readonly Dictionary<string, int> Weights = new()
    {
        ["sc_ip"] = 25, ["sc_dns"] = 20, ["sc_webrtc"] = 15, ["sc_ipv6"] = 10, ["sc_tls"] = 20, ["sc_core"] = 10,
    };

    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(0, 18, 0, 28) };
    private readonly List<CheckResult> _res = [];
    private readonly TextBlock _score = Ui.Num(Ui.T("–", 44, FontWeight.Bold));
    private readonly TextBlock _verdict = Ui.T(L.S("sc_intro"), 15, FontWeight.SemiBold);
    private readonly TextBlock _progress = Ui.Caption("", P.Text3);
    private readonly Border _ring = new() { Width = 118, Height = 118, CornerRadius = new CornerRadius(59), BorderThickness = new Thickness(7) };
    private readonly StackPanel _rows = new() { Spacing = 0 };
    private readonly Button _run;
    private bool _busy, _autoStarted;
    private int? _last;
    private readonly Border _gate = new() { Padding = new Thickness(24, 28), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 12) };

    /// <summary>Show the "Connect first" state when the VPN is not on. Returns true if gated.</summary>
    private bool Gate()
    {
        var off = SatraCore.I.Phase != ConnPhase.On;
        _gate.Background = P.B(P.Surface);
        _gate.IsVisible = off;
        _run.IsEnabled = !off && !_busy;
        return off;
    }

    public Control View => _root;

    public SecurityPage()
    {
        _run = Ui.Btn(L.S("sc_run"), Ui.Kind.Primary, () => _ = RunAsync(), "⟳", 13);
        _root.Children.Add(Ui.Row(Ui.Title(L.S("security")), _run));
        _score.HorizontalAlignment = HorizontalAlignment.Center;
        _score.VerticalAlignment = VerticalAlignment.Center;
        _ring.Child = _score;
        _ring.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(420) },
        };
        _body.Children.Add(Ui.Card(Ui.H(22, _ring, Ui.V(6, _verdict, _progress, Ui.Caption(L.S("sc_desc")))), new Thickness(22, 18)));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _rows });
        // "Connect first" gate: the scan only makes sense through the VPN
        var go = Ui.Btn(L.S("sc_connect"), Ui.Kind.Primary, async () =>
        {
            await SatraCore.I.ConnectAsync();
            if (SatraCore.I.Phase == ConnPhase.On) await RunAsync();
            else Gate();
        }, "⏻", 14);
        go.HorizontalAlignment = HorizontalAlignment.Center;
        var gt = Ui.T(L.S("sc_need_vpn"), 18, FontWeight.SemiBold);
        gt.HorizontalAlignment = HorizontalAlignment.Center;
        var gc = Ui.Caption(L.S("sc_need_vpn_d"));
        gc.HorizontalAlignment = HorizontalAlignment.Center;
        gc.TextWrapping = TextWrapping.Wrap;
        gc.TextAlignment = TextAlignment.Center;
        _gate.Child = Ui.V(12, gt, gc, go);
        _gate.IsVisible = false;
        _body.Children.Insert(0, _gate);
        var sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
        foreach (var k in SecurityCheck.Keys) _res.Add(new CheckResult { Key = k, State = CheckState.Pending, Detail = "" });
        Paint();
    }

    public void Refresh()
    {
        Paint();
        if (Gate()) { _autoStarted = false; return; }
        if (!_autoStarted && !_busy)
        {
            _autoStarted = true;
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        if (_busy || Gate()) return;
        _busy = true;
        _run.IsEnabled = false;
        _verdict.Text = L.S("sc_running");
        _progress.Text = "";
        _last = null;
        try
        {
            _last = await SecurityCheck.RunAsync(_res, () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var done = _res.Count(r => r.State is CheckState.Pass or CheckState.Warn or CheckState.Fail or CheckState.Unknown);
                    _progress.Text = $"{done}/{_res.Count}";
                    var partial = (int)Math.Round(_res.Sum(r => Weights.GetValueOrDefault(r.Key) * (r.State switch
                    {
                        CheckState.Pass => 1.0, CheckState.Warn => 0.5, CheckState.Unknown => 0.6, _ => 0
                    })));
                    _score.Text = partial == 0 ? "…" : partial.ToString();
                    Paint();
                });
            });
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
        var done = _res.Count(r => r.State is not CheckState.Pending and not CheckState.Running);
        var c = _last is int s ? (s >= 85 ? P.Success : s >= 60 ? P.Warning : P.Danger) : (_busy ? P.Accent : P.FillStrong);
        _ring.BorderBrush = P.B(c);
        if (!_busy) _score.Text = _last?.ToString() ?? "–";
        _score.Foreground = P.B(_last == null && !_busy ? P.Text3 : c);
        if (!_busy && _last is int v) _verdict.Text = L.S(v >= 85 ? "sc_good" : v >= 60 ? "sc_fair" : "sc_bad");
        else if (_busy) _verdict.Text = L.S("sc_running");
        _progress.Text = _busy || done > 0 ? $"{done}/{_res.Count}" : "";

        _rows.Children.Clear();
        var i = 0;
        foreach (var r in _res)
        {
            if (i++ > 0) _rows.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(56, 0, 0, 0) });
            _rows.Children.Add(Row(r));
        }
    }

    private static Border Row(CheckResult r)
    {
        var (glyph, col, soft, status) = r.State switch
        {
            CheckState.Pass => ("✓", P.Success, P.SuccessSoft, L.S("sc_status_pass")),
            CheckState.Warn => ("!", P.Warning, P.WarningSoft, L.S("sc_status_warn")),
            CheckState.Fail => ("✕", P.Danger, P.DangerSoft, L.S("sc_status_fail")),
            CheckState.Unknown => ("?", P.Text2, P.Fill, L.S("sc_status_unknown")),
            CheckState.Running => ("◉", P.Accent, P.AccentSoft, L.S("sc_checking")),
            _ => ("·", P.Text3, P.Fill, ""),
        };

        var maxPts = Weights.GetValueOrDefault(r.Key);
        var pts = r.State switch
        {
            CheckState.Pass => maxPts,
            CheckState.Warn => maxPts / 2,
            CheckState.Unknown => (int)Math.Round(maxPts * 0.6),
            _ => 0,
        };
        var ptsText = r.State is CheckState.Pass or CheckState.Warn or CheckState.Fail or CheckState.Unknown
            ? L.F("sc_points", pts, maxPts)
            : "";

        var why = Why(r);
        var fact = string.IsNullOrEmpty(r.Detail) || r.State is CheckState.Pending or CheckState.Running
            ? L.S(r.Key + "_d")
            : r.Detail;

        var dot = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = P.B(soft),
            Child = new TextBlock
            {
                Text = glyph, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = P.B(col),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (r.State == CheckState.Running)
        {
            dot.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(520) },
            };
            Pulse(dot);
        }

        var titleRow = Ui.Row(
            Ui.T(L.S(r.Key), 14.5, FontWeight.SemiBold),
            string.IsNullOrEmpty(status) ? new Control() : Ui.Pill(status, col, soft, 10.5));
        var lines = new List<Control> { titleRow };
        if (!string.IsNullOrEmpty(ptsText))
            lines.Add(Ui.Caption(ptsText, col));
        var factTb = Ui.Caption(fact, r.State == CheckState.Running ? P.Accent : P.Text2);
        factTb.TextWrapping = TextWrapping.Wrap;
        factTb.MaxWidth = 520;
        lines.Add(factTb);
        if (!string.IsNullOrEmpty(why))
        {
            var whyTb = Ui.T(why, 12.5, FontWeight.Medium, r.State is CheckState.Fail or CheckState.Warn ? col : P.Text3, TextWrapping.Wrap);
            whyTb.MaxWidth = 520;
            lines.Add(whyTb);
        }

        var row = Ui.Row(Ui.H(14, dot, Ui.V(4, lines.ToArray())));
        var border = new Border { Padding = new Thickness(14, 13), Child = row };
        if (r.State is CheckState.Pass or CheckState.Fail or CheckState.Warn or CheckState.Running)
            return Ui.Enter(border, 0);
        return border;
    }

    private static void Pulse(Border dot)
    {
        async void Loop()
        {
            try
            {
                for (var n = 0; n < 24; n++)
                {
                    await Task.Delay(280);
                    if (dot.Parent == null) return;
                    await Dispatcher.UIThread.InvokeAsync(() => dot.Opacity = n % 2 == 0 ? 0.45 : 1);
                }
            }
            catch { }
        }
        Loop();
    }

    private static string Why(CheckResult r) => r.State switch
    {
        CheckState.Pass => L.S(r.Key + "_y_pass"),
        CheckState.Warn => L.S(r.Key + "_y_warn"),
        CheckState.Fail => L.S(r.Key + "_y_fail"),
        CheckState.Unknown => L.S("sc_y_unknown"),
        CheckState.Running => L.S("sc_checking"),
        _ => "",
    };
}
