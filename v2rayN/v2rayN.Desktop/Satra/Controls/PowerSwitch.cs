using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// v1.9 power control: a wide capsule switch. Off: dark track, thumb at the start, "Connect".
/// Connecting: the thumb glides across with a soft orbiting ring while the track warms up.
/// On: thumb at the end, calm green track, "Connected". Failed: thumb eases back, a single gentle shake, red tint.
/// Spring motion (no linear tweens), redraws only while something moves. Mirrors naturally in RTL.
/// </summary>
public class PowerSwitch : Control
{
    private ConnPhase _phase = ConnPhase.Off;
    private readonly DispatcherTimer _timer;
    private DateTime _t0 = DateTime.Now, _phaseAt = DateTime.Now;
    private double _pos, _posVel, _tint, _press = 1, _pressVel, _pressTarget = 1;
    private bool _hover, _pressed;
    private readonly TextBlock _label = new() { FontSize = 15.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sub = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = P.B(P.Text3) };
    private readonly StackPanel _text;
    public event Action? Clicked;
    public double Thumb => Bounds.Height - 12;

    public PowerSwitch()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = 268; Height = 68;
        _label.TextTrimming = _sub.TextTrimming = TextTrimming.CharacterEllipsis;
        _text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { _label, _sub } };
        VisualChildren.Add(_text); LogicalChildren.Add(_text);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        AttachedToVisualTree += (_, _) => Kick();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        PointerPressed += (_, e) => { _pressTarget = 0.97; _pressed = true; e.Pointer.Capture(this); Kick(); };
        PointerReleased += (_, e) => { _pressTarget = 1; if (_pressed) Clicked?.Invoke(); _pressed = false; e.Pointer.Capture(null); Kick(); };
        PointerEntered += (_, _) => { _hover = true; Kick(); };
        PointerExited += (_, _) => { _hover = false; Kick(); };
        Focusable = true;
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter) Clicked?.Invoke(); };
        Apply();
    }

    public void SetSize(double w, double h) { Width = w; Height = h; _label.FontSize = h >= 64 ? 15.5 : 14; _sub.IsVisible = h >= 60; InvalidateArrange(); }

    public ConnPhase Phase
    {
        get => _phase;
        set { if (_phase == value) return; _phase = value; _phaseAt = DateTime.Now; Apply(); Kick(); }
    }

    /// <summary>Main label + small second line (e.g. "Ports only · Game").</summary>
    public void SetText(string label, string sub)
    {
        if (_label.Text != label) _label.Text = label;
        if (_sub.Text != sub) _sub.Text = sub;
        Avalonia.Automation.AutomationProperties.SetName(this, label);
    }

    private double TargetPos => _phase switch { ConnPhase.On => 1, ConnPhase.Connecting => 0.72, ConnPhase.Disconnecting => 0.28, _ => 0 };
    private Color Col => _phase switch { ConnPhase.On => P.Success, ConnPhase.Failed => P.Danger, ConnPhase.Disconnecting => P.Text3, _ => P.Accent };

    private void Apply()
    {
        _label.Foreground = P.B(_phase == ConnPhase.On ? Colors.White : _phase == ConnPhase.Failed ? P.Danger : P.Text);
        _sub.Foreground = P.B(_phase == ConnPhase.On ? Color.FromArgb(200, 255, 255, 255) : P.Text3);
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size a) { _text.Measure(new Size(Math.Max(0, Width - Height - 34), Height)); return new Size(Width, Height); }

    protected override Size ArrangeOverride(Size s)
    {
        // text sits on the free side of the thumb: right of it when off, left of it when on
        var th = s.Height - 12;
        var onSide = _phase is ConnPhase.On or ConnPhase.Connecting;
        var tw = s.Width - th - 34;
        _text.Arrange(onSide ? new Rect(22, 0, tw, s.Height) : new Rect(th + 18, 0, tw, s.Height));
        return s;
    }

    private void Step()
    {
        const double dt = 0.016;
        // gentle springs: position (damping .8), press (snappy)
        var a = 170 * (TargetPos - _pos) - 2 * 0.8 * 13 * _posVel; _posVel += a * dt; _pos += _posVel * dt;
        var b = 700 * (_pressTarget - _press) - 2 * 0.62 * 26.5 * _pressVel; _pressVel += b * dt; _press += _pressVel * dt;
        var tt = _phase == ConnPhase.Off ? 0 : 1; _tint += (tt - _tint) * 0.1;
        InvalidateVisual();
        var since = (DateTime.Now - _phaseAt).TotalSeconds;
        var moving = _phase is ConnPhase.Connecting or ConnPhase.Disconnecting || Math.Abs(TargetPos - _pos) > 0.001 || Math.Abs(_posVel) > 0.005
            || Math.Abs(_pressTarget - _press) > 0.001 || Math.Abs(tt - _tint) > 0.003 || since < (_phase == ConnPhase.On ? 2.4 : 0.7);
        if (!moving || TopLevel.GetTopLevel(this) is { IsVisible: false }) { _pos = TargetPos; _press = _pressTarget; _timer.Stop(); InvalidateVisual(); }
    }

    public void Kick() { if (!_timer.IsEnabled && this.IsAttachedToVisualTree()) _timer.Start(); }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        var t = (DateTime.Now - _t0).TotalSeconds;
        var since = (DateTime.Now - _phaseAt).TotalSeconds;
        var shake = _phase == ConnPhase.Failed && since < 0.45 ? Math.Sin(since * 38) * 5 * (1 - since / 0.45) : 0;
        var col = Col;
        using var sc = ctx.PushTransform(Matrix.CreateTranslation(-w / 2, -h / 2) * Matrix.CreateScale(_press * (_hover ? 1.01 : 1), _press * (_hover ? 1.01 : 1)) * Matrix.CreateTranslation(w / 2 + shake, h / 2));
        var track = new Rect(0, 0, w, h);
        var rr = h / 2;
        // soft outer glow when on
        if (_tint > 0.02 && _phase == ConnPhase.On)
            ctx.DrawRectangle(P.B(col, 0.10 * _tint), null, track.Inflate(5), rr + 5, rr + 5);
        var offTop = Color.Parse("#1D1D22"); var offBot = Color.Parse("#151518");
        var onTop = P.Lerp(col, Colors.White, 0.10); var onBot = P.Lerp(col, Colors.Black, 0.30);
        var k = _phase == ConnPhase.Connecting ? 0.28 + 0.06 * Math.Sin(t * 3) : _phase == ConnPhase.Failed ? 0.22 : _phase == ConnPhase.Disconnecting ? 0.15 : _tint;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(P.Lerp(offTop, onTop, k), 0), new GradientStop(P.Lerp(offBot, onBot, k), 1) },
        };
        ctx.DrawRectangle(fill, new Pen(P.B(P.Lerp(Color.Parse("#2C2C33"), col, _phase == ConnPhase.Off ? 0.35 : 0.55)), 1), track, rr, rr);
        // connecting: a light sheen sweeping along the track
        if (_phase == ConnPhase.Connecting)
        {
            var x = (t * 0.7 % 1.0) * (w + 120) - 60;
            var sheen = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(Math.Clamp((x - 60) / w, -1, 2), 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(Math.Clamp((x + 60) / w, -1, 2), 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(28, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
            };
            ctx.DrawRectangle(sheen, null, track, rr, rr);
        }
        // thumb
        var th = h - 12;
        var cx = 6 + th / 2 + _pos * (w - th - 12);
        var c = new Point(cx, h / 2);
        ctx.DrawEllipse(P.B(Colors.Black, 0.35), null, new Point(c.X, c.Y + 2), th / 2 + 1, th / 2 + 1);
        var thumbTop = _phase == ConnPhase.Off ? Color.Parse("#2E2E35") : Color.Parse("#FFFFFF");
        var thumbBot = _phase == ConnPhase.Off ? Color.Parse("#232328") : Color.Parse("#E9E9EE");
        var tb = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(P.Lerp(Color.Parse("#2E2E35"), thumbTop, _tint), 0), new GradientStop(P.Lerp(Color.Parse("#232328"), thumbBot, _tint), 1) },
        };
        ctx.DrawEllipse(tb, new Pen(P.B(Colors.White, 0.08), 1), c, th / 2, th / 2);
        if (_phase is ConnPhase.Connecting or ConnPhase.Disconnecting)
        {
            var start = t * 260 % 360;
            DrawArc(ctx, c, th / 2 + 3.5, start, 90, new Pen(P.B(col, 0.95), 2.5, lineCap: PenLineCap.Round));
        }
        // power glyph
        var gcol = _phase == ConnPhase.Off ? P.Accent : col;
        var gp = new Pen(P.B(gcol), Math.Max(2.4, th * 0.075), lineCap: PenLineCap.Round);
        var gr = th * 0.2;
        DrawArc(ctx, c, gr, 35, 290, gp);
        ctx.DrawLine(gp, new Point(c.X, c.Y - gr - 3), new Point(c.X, c.Y - gr * 0.2));
    }

    private static void DrawArc(DrawingContext ctx, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        double Rad(double d) => (d - 90) * Math.PI / 180;
        var a0 = Rad(startDeg); var a1 = Rad(startDeg + sweepDeg);
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
