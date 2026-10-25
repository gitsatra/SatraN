using Avalonia.Media;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// Download speed gauge. 240° arc (from 150° to 390°), non-linear scale 0·5·10·25·50·100·250·500 Mbps,
/// needle eases toward the live value (critically damped spring, ~250 ms), progress ring around it shows test time.
/// Colours: track #2A2A2E, value arc orange #FF9A1A→#FF4D00 while running, green #4ADE80→#16A34A when done.
/// The timer only runs while the value or progress is moving (no idle CPU).
/// </summary>
public class SpeedGauge : Control
{
    private static readonly double[] Ticks = [0, 5, 10, 25, 50, 100, 250, 500];
    private double _target, _shown, _vel, _progress, _shownProgress;
    private bool _done;
    private static readonly FontFamily Inter = new("fonts:Inter#Inter");
    private static readonly Typeface TReg = new(Inter), TBold = new(Inter, FontStyle.Normal, FontWeight.Bold);
    private DispatcherTimer? _timer;

    public SpeedGauge() { Width = 260; Height = 220; }

    public void Set(double mbps, double progress, bool done = false)
    {
        _target = Math.Max(0, mbps);
        _progress = Math.Clamp(progress, 0, 1);
        _done = done;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        _timer.Start();
    }

    private void Step()
    {
        const double k = 90, c = 19; // spring
        var a = k * (_target - _shown) - c * _vel;
        _vel += a * 0.016;
        _shown += _vel * 0.016;
        _shownProgress += (_progress - _shownProgress) * 0.2;
        InvalidateVisual();
        if (Math.Abs(_target - _shown) < 0.01 && Math.Abs(_vel) < 0.01 && Math.Abs(_progress - _shownProgress) < 0.001)
        {
            _shown = _target; _shownProgress = _progress;
            _timer?.Stop();
        }
    }

    /// <summary>Maps Mbps to 0..1 along the piecewise scale.</summary>
    public static double Frac(double v)
    {
        if (v <= 0) return 0;
        for (var i = 1; i < Ticks.Length; i++)
            if (v <= Ticks[i]) return (i - 1 + (v - Ticks[i - 1]) / (Ticks[i] - Ticks[i - 1])) / (Ticks.Length - 1);
        return 1;
    }

    private static Point P(Point c, double r, double deg) { var t = deg * Math.PI / 180; return new Point(c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t)); }

    private static Geometry Arc(Point c, double r, double from, double sweep)
    {
        var g = new StreamGeometry();
        using var x = g.Open();
        x.BeginFigure(P(c, r, from), false);
        x.ArcTo(P(c, r, from + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise);
        x.EndFigure(false);
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        var c = new Point(Bounds.Width / 2, Bounds.Height * 0.58);
        var r = Math.Min(Bounds.Width, Bounds.Height * 1.3) / 2 - 18;
        const double start = 150, span = 240;
        var track = new Pen(new SolidColorBrush(Color.Parse("#2A2A2E")), 14, lineCap: PenLineCap.Round);
        dc.DrawGeometry(null, track, Arc(c, r, start, span));
        var (c1, c2) = _done ? (Color.Parse("#4ADE80"), Color.Parse("#16A34A")) : (Color.Parse("#FF9A1A"), Color.Parse("#FF4D00"));
        var grad = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(c1, 0), new GradientStop(c2, 1) } };
        var f = Frac(_shown);
        if (f > 0.002) dc.DrawGeometry(null, new Pen(grad, 14, lineCap: PenLineCap.Round), Arc(c, r, start, span * f));
        // progress ring (thin, outside)
        if (_shownProgress > 0.002)
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#66FFFFFF")), 3, lineCap: PenLineCap.Round), Arc(c, r + 14, start, span * _shownProgress));
        // ticks
        var tickBrush = new SolidColorBrush(Color.Parse("#8A8A90"));
        for (var i = 0; i < Ticks.Length; i++)
        {
            var deg = start + span * i / (Ticks.Length - 1);
            var p = P(c, r - 26, deg);
            var ft = new FormattedText(Ticks[i].ToString("0"), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TReg, 10.5, tickBrush);
            dc.DrawText(ft, new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2));
        }
        // needle
        var nd = start + span * f;
        dc.DrawLine(new Pen(new SolidColorBrush(Colors.White), 3, lineCap: PenLineCap.Round), P(c, 10, nd), P(c, r - 40, nd));
        dc.DrawEllipse(new SolidColorBrush(Colors.White), null, c, 6, 6);
        // value
        var val = new FormattedText(_shown < 10 ? _shown.ToString("0.0") : _shown.ToString("0"), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            TBold, 34, new SolidColorBrush(Colors.White));
        dc.DrawText(val, new Point(c.X - val.Width / 2, c.Y + 22));
        var unit = new FormattedText("Mbps ↓", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TReg, 12, tickBrush);
        dc.DrawText(unit, new Point(c.X - unit.Width / 2, c.Y + 62));
    }
}
