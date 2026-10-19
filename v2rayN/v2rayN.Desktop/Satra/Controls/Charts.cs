namespace v2rayN.Desktop.Satra.Controls;

/// <summary>Bar chart that animates bar heights toward new values instead of redrawing from zero.</summary>
public class BarChart : Control
{
    private double[] _shown = [];
    private double[] _target = [];
    private string[] _labels = [];
    private readonly DispatcherTimer _anim;
    public Color BarColor { get; set; } = P.Accent;
    public Func<double, string> Format { get; set; } = v => v.ToString("0");
    public int Highlight { get; set; } = -1;

    public BarChart()
    {
        Height = 170;
        _anim = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var moving = false;
            for (var i = 0; i < _shown.Length; i++)
            {
                var d = _target[i] - _shown[i];
                if (Math.Abs(d) > 0.0005) { _shown[i] += d * 0.16; moving = true; } else _shown[i] = _target[i];
            }
            InvalidateVisual();
            if (!moving) _animStop();
        });
    }

    private void _animStop() => _anim.Stop();

    public void SetData(double[] values, string[] labels)
    {
        var max = Math.Max(1e-9, values.DefaultIfEmpty(0).Max());
        var norm = values.Select(v => v / max).ToArray();
        if (_shown.Length != norm.Length) _shown = new double[norm.Length];
        _target = norm;
        _labels = labels;
        _values = values;
        _anim.Start();
    }

    private double[] _values = [];

    public override void Render(DrawingContext ctx)
    {
        var n = _shown.Length;
        if (n == 0) return;
        var w = Bounds.Width;
        var h = Bounds.Height - 22;
        var slot = w / n;
        var bw = Math.Max(3, Math.Min(26, slot * 0.62));
        var lblEvery = n > 14 ? 5 : 1;
        for (var i = 0; i < n; i++)
        {
            var x = i * slot + (slot - bw) / 2;
            var bh = Math.Max(3, _shown[i] * (h - 18));
            var isHi = i == (Highlight < 0 ? n - 1 : Highlight);
            var col = isHi ? BarColor : P.Lerp(BarColor, P.Surface, 0.45);
            ctx.DrawRectangle(P.B(P.Fill), null, new RoundedRect(new Rect(x, 18, bw, h - 18), bw / 2.6));
            ctx.DrawRectangle(P.B(col), null, new RoundedRect(new Rect(x, h - bh, bw, bh), bw / 2.6));
            if (i % lblEvery == 0 || i == n - 1)
            {
                var ft = new FormattedText(_labels.ElementAtOrDefault(i) ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10.5, P.B(isHi ? P.Text : P.Text3));
                ctx.DrawText(ft, new Point(x + bw / 2 - ft.Width / 2, h + 5));
            }
            if (isHi && _values.Length > i)
            {
                var ft = new FormattedText(Format(_values[i]), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, P.B(P.Text));
                ctx.DrawText(ft, new Point(Math.Clamp(x + bw / 2 - ft.Width / 2, 0, w - ft.Width), Math.Max(0, h - bh - 16)));
            }
        }
    }
}

/// <summary>Live throughput sparkline (last 60 s) with a soft gradient fill.</summary>
public class Sparkline : Control
{
    private double[] _data = [];
    public Color LineColor { get; set; } = P.Accent;

    public void SetData(IEnumerable<double> d)
    {
        _data = d.ToArray();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (_data.Length < 2) return;
        var w = Bounds.Width;
        var h = Bounds.Height;
        var max = Math.Max(1024, _data.Max()) * 1.15;
        var pts = _data.Select((v, i) => new Point(i * w / 59.0 + (60 - _data.Length) * w / 59.0, h - v / max * h)).ToList();
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var s = line.Open())
        {
            s.BeginFigure(pts[0], false);
            for (var i = 1; i < pts.Count; i++)
            {
                var m = new Point((pts[i - 1].X + pts[i].X) / 2, (pts[i - 1].Y + pts[i].Y) / 2);
                s.QuadraticBezierTo(pts[i - 1], m);
            }
            s.LineTo(pts[^1]);
            s.EndFigure(false);
        }
        using (var s = area.Open())
        {
            s.BeginFigure(new Point(pts[0].X, h), true);
            foreach (var p in pts) s.LineTo(p);
            s.LineTo(new Point(pts[^1].X, h));
            s.EndFigure(true);
        }
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(90, LineColor.R, LineColor.G, LineColor.B), 0), new GradientStop(Color.FromArgb(0, LineColor.R, LineColor.G, LineColor.B), 1) },
        };
        ctx.DrawGeometry(fill, null, area);
        ctx.DrawGeometry(null, new Pen(P.B(LineColor), 2, lineCap: PenLineCap.Round), line);
    }
}
