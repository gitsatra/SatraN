namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// Circular progress: a soft track with an arc. Indeterminate → the arc breathes and spins; Value 0..1 → the arc
/// eases to that fraction. Done(true/false) fills the ring and draws a ✓ or ✕ that strokes itself in.
/// Redraws only while something moves.
/// </summary>
public class ProgressRing : Control
{
    public enum St { Busy, Ok, Fail }
    private St _st = St.Busy;
    private double? _target;
    private double _shown, _done;
    private DateTime _t0 = DateTime.Now, _doneAt;
    private readonly DispatcherTimer _timer;
    public Color Tint { get; set; } = P.Accent;
    public double Stroke { get; set; } = 2.4;

    public ProgressRing(double size = 18)
    {
        Width = Height = size;
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    /// <summary>null = indeterminate.</summary>
    public double? Value { get => _target; set { _target = value is { } v ? Math.Clamp(v, 0, 1) : null; Kick(); } }
    public St State => _st;

    public void Done(bool ok) { if (_st != St.Busy) return; _st = ok ? St.Ok : St.Fail; _doneAt = DateTime.Now; _target ??= 1; Kick(); }
    public void Reset() { _st = St.Busy; _done = 0; _target = null; _shown = 0; Kick(); }
    private void Kick() { if (!_timer.IsEnabled && this.IsAttachedToVisualTree()) _timer.Start(); }

    private void Step()
    {
        var goal = _st != St.Busy ? 1 : _target ?? 0;
        _shown += (goal - _shown) * 0.14;
        if (_st != St.Busy) _done = Math.Min(1, (DateTime.Now - _doneAt).TotalSeconds / 0.42);
        InvalidateVisual();
        if (_st != St.Busy && _done >= 1 && Math.Abs(goal - _shown) < 0.002) _timer.Stop();
        if (!IsEffectivelyVisible) _timer.Stop();
    }

    private static double Ease(double x) => 1 - Math.Pow(1 - x, 3);

    public override void Render(DrawingContext dc)
    {
        var s = Math.Min(Bounds.Width, Bounds.Height);
        if (s <= 0) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var r = s / 2 - Stroke / 2 - 0.5;
        var col = _st switch { St.Ok => P.Success, St.Fail => P.Danger, _ => Tint };
        dc.DrawEllipse(null, new Pen(P.B(col, 0.18), Stroke), c, r, r);
        var t = (DateTime.Now - _t0).TotalSeconds;
        double start, sweep;
        if (_st == St.Busy && _target == null)
        {
            start = t * 360 * 0.9 % 360;
            sweep = 70 + 160 * (0.5 + 0.5 * Math.Sin(t * 2.6));
        }
        else { start = -90; sweep = 359.9 * _shown; }
        if (sweep > 0.5) dc.DrawGeometry(null, new Pen(P.B(col), Stroke, lineCap: PenLineCap.Round), Arc(c, r, start, sweep));
        if (_st == St.Busy) return;
        var k = Ease(_done);
        dc.DrawEllipse(P.B(col, 0.16 * k), null, c, r * k, r * k);
        var pen = new Pen(P.B(col), Stroke * 0.95, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            if (_st == St.Ok)
            {
                var a = new Point(c.X - r * 0.42, c.Y + r * 0.02); var b = new Point(c.X - r * 0.1, c.Y + r * 0.34); var e = new Point(c.X + r * 0.45, c.Y - r * 0.3);
                ctx.BeginFigure(a, false);
                if (k < 0.4) ctx.LineTo(Lerp(a, b, k / 0.4));
                else { ctx.LineTo(b); ctx.LineTo(Lerp(b, e, (k - 0.4) / 0.6)); }
            }
            else
            {
                var d = r * 0.36;
                ctx.BeginFigure(new Point(c.X - d, c.Y - d), false); ctx.LineTo(Lerp(new Point(c.X - d, c.Y - d), new Point(c.X + d, c.Y + d), Math.Min(1, k * 2)));
                if (k > 0.5) { ctx.BeginFigure(new Point(c.X + d, c.Y - d), false); ctx.LineTo(Lerp(new Point(c.X + d, c.Y - d), new Point(c.X - d, c.Y + d), (k - 0.5) * 2)); }
            }
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static Point Lerp(Point a, Point b, double f) => new(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        double R(double d) => d * Math.PI / 180;
        var p0 = new Point(c.X + r * Math.Cos(R(startDeg)), c.Y + r * Math.Sin(R(startDeg)));
        var p1 = new Point(c.X + r * Math.Cos(R(startDeg + sweepDeg)), c.Y + r * Math.Sin(R(startDeg + sweepDeg)));
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(p0, false);
        ctx.ArcTo(p1, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
        return g;
    }
}
