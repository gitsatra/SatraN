using Avalonia.Media;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// Plain live speed readout: Mbps number updated per sample, a thin progress line and peak · avg · elapsed. No animation, no chart.
/// </summary>
public class SpeedLive : Control
{
    private readonly List<double> _samples = [];
    private double _target, _progress;
    private bool _done;
    private DateTime _start = DateTime.UtcNow;
    private static readonly FontFamily Inter = new("fonts:Inter#Inter");
    private static readonly Typeface TReg = new(Inter), TBold = new(Inter, FontStyle.Normal, FontWeight.Bold);
    public string Via { get; set; } = "";
    public double Peak => _samples.Count == 0 ? 0 : _samples.Max();
    public double Avg => _samples.Count == 0 ? 0 : _samples.Average();

    public SpeedLive() { Height = 92; MinWidth = 240; }

    public void Reset() { _samples.Clear(); _target = _progress = 0; _done = false; _start = DateTime.UtcNow; InvalidateVisual(); }

    /// <summary>Plain live number: redraws only when a new sample arrives (no animation timer).</summary>
    public void Set(double mbps, double progress, bool done = false)
    {
        _target = Math.Max(0, mbps);
        _progress = Math.Clamp(progress, 0, 1);
        if (mbps > 0 && (!done || _samples.Count == 0)) _samples.Add(mbps);
        _done = done;
        InvalidateVisual();
    }

    private FormattedText Ft(string s, Typeface t, double size, Color c) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, t, size, new SolidColorBrush(c));

    public override void Render(DrawingContext dc)
    {
        using var _ = FlowDirection == FlowDirection.RightToLeft ? dc.PushTransform(new Matrix(-1, 0, 0, 1, Bounds.Width, 0)) : default(DrawingContext.PushedState?);
        var w = Bounds.Width; var h = Bounds.Height;
        var accent = _done ? Color.Parse("#34C759") : P.Accent;
        // big number
        var num = Ft(_target < 10 ? _target.ToString("0.0") : _target.ToString("0"), TBold, 46, P.Text);
        dc.DrawText(num, new Point(0, 0));
        dc.DrawText(Ft("Mbps ↓", TReg, 14, P.Text3), new Point(num.Width + 8, 30));
        if (!string.IsNullOrEmpty(Via))
        {
            var v = Ft(Via, TReg, 12, P.Text3);
            dc.DrawText(v, new Point(Math.Max(num.Width + 70, w - v.Width), 8));
        }
        double bottom = h - 24;
        dc.DrawLine(new Pen(P.B(P.Separator), 1), new Point(0, bottom), new Point(w, bottom));
        if (!_done && _progress > 0) dc.DrawLine(new Pen(new SolidColorBrush(accent), 2), new Point(0, bottom), new Point(w * _progress, bottom));
        // stats
        var secs = _done ? _samples.Count * 0.2 : (DateTime.UtcNow - _start).TotalSeconds;
        var st = Ft($"Peak {Peak:0.0}   ·   Avg {Avg:0.0} Mbps   ·   {secs:0.0}s", TReg, 12, P.Text3);
        dc.DrawText(st, new Point(0, h - st.Height));
    }
}
