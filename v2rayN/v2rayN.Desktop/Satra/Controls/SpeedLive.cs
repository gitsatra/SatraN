using Avalonia.Media;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// Slim live speed view (no dial): big count-up Mbps number, thin area chart that draws as samples arrive,
/// and a stats line (peak · average · elapsed). Animation timer runs only while the number is moving.
/// </summary>
public class SpeedLive : Control
{
    private readonly List<double> _samples = [];
    private double _target, _shown, _progress;
    private bool _done;
    private DateTime _start = DateTime.UtcNow;
    private DispatcherTimer? _timer;
    private static readonly FontFamily Inter = new("fonts:Inter#Inter");
    private static readonly Typeface TReg = new(Inter), TBold = new(Inter, FontStyle.Normal, FontWeight.Bold);
    public string Via { get; set; } = "";
    public double Peak => _samples.Count == 0 ? 0 : _samples.Max();
    public double Avg => _samples.Count == 0 ? 0 : _samples.Average();

    public SpeedLive() { Height = 190; MinWidth = 260; }

    public void Reset() { _samples.Clear(); _target = _shown = _progress = 0; _done = false; _start = DateTime.UtcNow; InvalidateVisual(); }

    public void Set(double mbps, double progress, bool done = false)
    {
        _target = Math.Max(0, mbps);
        _progress = Math.Clamp(progress, 0, 1);
        if (!done && mbps > 0) _samples.Add(mbps);
        if (done && mbps > 0 && _samples.Count == 0) _samples.Add(mbps);
        _done = done;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        _timer.Start();
        InvalidateVisual();
    }

    private void Step()
    {
        _shown += (_target - _shown) * 0.14;
        InvalidateVisual();
        if (Math.Abs(_target - _shown) < 0.05) { _shown = _target; _timer?.Stop(); }
    }

    private FormattedText Ft(string s, Typeface t, double size, Color c) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, t, size, new SolidColorBrush(c));

    public override void Render(DrawingContext dc)
    {
        using var _ = FlowDirection == FlowDirection.RightToLeft ? dc.PushTransform(new Matrix(-1, 0, 0, 1, Bounds.Width, 0)) : default(DrawingContext.PushedState?);
        var w = Bounds.Width; var h = Bounds.Height;
        var accent = _done ? Color.Parse("#34C759") : P.Accent;
        // big number
        var num = Ft(_shown < 10 ? _shown.ToString("0.0") : _shown.ToString("0"), TBold, 46, P.Text);
        dc.DrawText(num, new Point(0, 0));
        dc.DrawText(Ft("Mbps ↓", TReg, 14, P.Text3), new Point(num.Width + 8, 30));
        if (!string.IsNullOrEmpty(Via))
        {
            var v = Ft(Via, TReg, 12, P.Text3);
            dc.DrawText(v, new Point(Math.Max(num.Width + 70, w - v.Width), 8));
        }
        // chart
        double top = 66, bottom = h - 26, ch = bottom - top;
        dc.DrawLine(new Pen(P.B(P.Separator), 1), new Point(0, bottom), new Point(w, bottom));
        if (_samples.Count >= 2)
        {
            var max = Math.Max(10, _samples.Max()) * 1.15;
            var n = Math.Max(_samples.Count - 1, (int)(_samples.Count / Math.Max(0.05, _progress)) - 1);
            if (_done) n = _samples.Count - 1;
            var pts = _samples.Select((s, i) => new Point(i * w / n, bottom - s / max * ch)).ToList();
            var line = new StreamGeometry(); var area = new StreamGeometry();
            using (var s = line.Open())
            {
                s.BeginFigure(pts[0], false);
                for (var i = 1; i < pts.Count; i++) s.QuadraticBezierTo(pts[i - 1], new Point((pts[i - 1].X + pts[i].X) / 2, (pts[i - 1].Y + pts[i].Y) / 2));
                s.LineTo(pts[^1]); s.EndFigure(false);
            }
            using (var s = area.Open())
            {
                s.BeginFigure(new Point(pts[0].X, bottom), true);
                foreach (var p in pts) s.LineTo(p);
                s.LineTo(new Point(pts[^1].X, bottom)); s.EndFigure(true);
            }
            dc.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(80, accent.R, accent.G, accent.B), 0), new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1) },
            }, null, area);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2, lineCap: PenLineCap.Round), line);
            if (!_done) dc.DrawEllipse(new SolidColorBrush(accent), null, pts[^1], 3.5, 3.5);
        }
        else dc.DrawLine(new Pen(P.B(accent, 0.35), 1.5, new DashStyle([3, 4], 0)), new Point(0, bottom - ch / 2), new Point(w, bottom - ch / 2));
        // progress hairline
        if (!_done && _progress > 0) dc.DrawLine(new Pen(new SolidColorBrush(accent), 2), new Point(0, bottom), new Point(w * _progress, bottom));
        // stats
        var secs = _done ? _samples.Count * 0.2 : (DateTime.UtcNow - _start).TotalSeconds;
        var st = Ft($"Peak {Peak:0.0}   ·   Avg {Avg:0.0} Mbps   ·   {secs:0.0}s", TReg, 12, P.Text3);
        dc.DrawText(st, new Point(0, h - st.Height));
    }
}
