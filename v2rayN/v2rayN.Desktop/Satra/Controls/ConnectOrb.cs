using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// The big connect button. Off: neutral disc. Connecting: orange arc sweeping around. On: green disc with
/// a breathing glow and expanding ripples. Failed: red flash with a short shake. Press: spring squash.
/// </summary>
public class ConnectOrb : Control
{
    private ConnPhase _phase = ConnPhase.Off;
    private readonly DispatcherTimer _timer;
    private DateTime _t0 = DateTime.Now, _phaseAt = DateTime.Now;
    private double _fill;      // 0 = off colour, 1 = phase colour
    private double _press = 1; // scale
    private double _pressTarget = 1, _pressVel;
    private bool _hover;
    private bool _pressed;
    public event Action? Clicked;

    public ConnectOrb()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        _timer.Start();
        PointerPressed += (_, e) =>
        {
            _pressTarget = 0.93;
            _pressed = true;
            e.Pointer.Capture(this);
        };
        PointerReleased += (_, e) =>
        {
            _pressTarget = 1;
            // Fire when the press started on this orb (hover can be false under X11 automation).
            if (_pressed) Clicked?.Invoke();
            _pressed = false;
            e.Pointer.Capture(null);
        };
        PointerEntered += (_, _) => _hover = true;
        PointerExited += (_, _) => { _hover = false; if (!_pressed) _pressTarget = 1; };
    }

    public ConnPhase Phase
    {
        get => _phase;
        set
        {
            if (_phase == value) return;
            _phase = value;
            _phaseAt = DateTime.Now;
        }
    }

    private void Step()
    {
        // critically-damped-ish spring for the press scale (matches SatraMotion.press: damping .62, stiffness 700)
        const double k = 700, d = 2 * 0.62 * 26.5;
        var dt = 0.016;
        var a = k * (_pressTarget - _press) - d * _pressVel;
        _pressVel += a * dt;
        _press += _pressVel * dt;
        var target = _phase is ConnPhase.Off ? 0 : 1;
        _fill += (target - _fill) * 0.12;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(220, 220);

    private Color PhaseColor => _phase switch
    {
        ConnPhase.On => P.Success,
        ConnPhase.Failed => P.Danger,
        ConnPhase.Disconnecting => P.Text3,
        _ => P.Accent,
    };

    public override void Render(DrawingContext ctx)
    {
        // custom drawing (incl. text) must not be mirrored in RTL layouts
        using var _ = FlowDirection == FlowDirection.RightToLeft ? ctx.PushTransform(new Matrix(-1, 0, 0, 1, Bounds.Width, 0)) : default(DrawingContext.PushedState?);
        var t = (DateTime.Now - _t0).TotalSeconds;
        var since = (DateTime.Now - _phaseAt).TotalSeconds;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var shake = _phase == ConnPhase.Failed && since < 0.5 ? Math.Sin(since * 50) * 8 * (1 - since / 0.5) : 0;
        c = new Point(c.X + shake, c.Y);
        var r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 18;
        var scale = _press * (_hover ? 1.015 : 1);
        var col = PhaseColor;

        // ripples when connected
        if (_phase == ConnPhase.On)
        {
            for (var i = 0; i < 3; i++)
            {
                var p = (t * 0.45 + i / 3.0) % 1.0;
                var rr = r * (1 + p * 0.32);
                ctx.DrawEllipse(null, new Pen(P.B(col, 0.35 * (1 - p)), 2), c, rr, rr);
            }
        }
        // ambient glow
        var glowA = _phase switch { ConnPhase.On => 0.28 + 0.08 * Math.Sin(t * 2.2), ConnPhase.Connecting => 0.22, ConnPhase.Failed => 0.25, _ => 0.06 };
        var glow = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb((byte)(glowA * 255), col.R, col.G, col.B), 0), new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), 1) },
        };
        ctx.DrawEllipse(glow, null, c, r * 1.45, r * 1.45);

        using (ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            // disc
            var off = Color.Parse("#1B1B1F");
            var top = P.Lerp(off, P.Lerp(col, Colors.White, 0.12), _fill);
            var bottom = P.Lerp(Color.Parse("#141417"), P.Lerp(col, Colors.Black, 0.25), _fill);
            var disc = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
            };
            ctx.DrawEllipse(disc, new Pen(P.B(P.Lerp(Color.Parse("#2A2A30"), col, _fill * 0.6)), 1.5), c, r, r);

            // spinner arc while connecting / disconnecting
            if (_phase is ConnPhase.Connecting or ConnPhase.Disconnecting)
            {
                var start = t * 300 % 360;
                var sweep = 70 + 50 * Math.Sin(t * 3);
                DrawArc(ctx, c, r + 9, start, sweep, new Pen(P.B(col), 4, lineCap: PenLineCap.Round));
                DrawArc(ctx, c, r + 9, start + 180, sweep * 0.4, new Pen(P.B(col, 0.4), 4, lineCap: PenLineCap.Round));
            }

            // power glyph
            var gcol = _fill > 0.5 ? Colors.White : P.Lerp(Color.Parse("#B4B4BD"), Colors.White, _fill * 2);
            var gp = new Pen(P.B(gcol), 7, lineCap: PenLineCap.Round);
            var gr = r * 0.34;
            DrawArc(ctx, c, gr, 32, 296, gp);
            ctx.DrawLine(gp, new Point(c.X, c.Y - gr - 6), new Point(c.X, c.Y - gr * 0.25));
        }
    }

    private static void DrawArc(DrawingContext ctx, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        // angles: 0 = top, clockwise
        double Rad(double d) => (d - 90) * Math.PI / 180;
        var a0 = Rad(startDeg);
        var a1 = Rad(startDeg + sweepDeg);
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
            s.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
            s.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }
}
