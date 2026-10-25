using Avalonia.Media;
using Avalonia.Threading;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>Calm score ring: a soft track, an arc that eases to its value, a number that counts up once at the end.</summary>
public class ScoreRing : Control
{
    private double _shown, _target, _progress;
    private bool _busy;
    private int? _score;
    private Color _col = P.FillStrong;
    private readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _spin;

    public ScoreRing()
    {
        Width = Height = 132;
        _t.Tick += (_, _) =>
        {
            var d = _target - _shown;
            _shown += d * 0.09;
            if (_busy) _spin = (_spin + 2.4) % 360;
            if (Math.Abs(d) < 0.001 && !_busy) { _shown = _target; _t.Stop(); }
            InvalidateVisual();
        };
    }

    /// <summary>While scanning: arc follows progress (0..1), no number.</summary>
    public void Scanning(double progress) { _busy = true; _score = null; _progress = progress; _target = progress; _col = P.Accent; _t.Start(); }

    public void Final(int? score)
    {
        _busy = false; _score = score;
        _target = (score ?? 0) / 100.0;
        _col = score is int s ? (s >= 85 ? P.Success : s >= 60 ? P.Warning : P.Danger) : P.FillStrong;
        _t.Start();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        var c = new Point(w / 2, h / 2);
        var r = Math.Min(w, h) / 2 - 8;
        ctx.DrawEllipse(null, new Pen(P.B(P.Fill), 9), c, r, r);
        var frac = Math.Clamp(_shown, 0, 1);
        if (frac > 0.002)
        {
            var start = -90.0;
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                var a0 = start * Math.PI / 180; var a1 = (start + 359.9 * frac) * Math.PI / 180;
                gc.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
                gc.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, frac > 0.5, SweepDirection.Clockwise);
            }
            ctx.DrawGeometry(null, new Pen(P.B(_col), 9, lineCap: PenLineCap.Round), g);
        }
        if (_busy)
        {
            // a small orbiting dot says "working" without moving anything else
            var a = (_spin - 90) * Math.PI / 180;
            ctx.DrawEllipse(P.B(Color.FromArgb(150, _col.R, _col.G, _col.B)), null, new Point(c.X + (r - 16) * Math.Cos(a), c.Y + (r - 16) * Math.Sin(a)), 3, 3);
        }
        var txt = _busy ? $"{(int)Math.Round(_progress * 100)}%" : _score is int ? ((int)Math.Round(_shown * 100)).ToString() : "–";
        var ft = new FormattedText(txt, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(GetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty), FontStyle.Normal, FontWeight.Bold), _busy ? 22 : 40, P.B(_busy ? P.Text2 : _score == null ? P.Text3 : _col));
        ctx.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
    }
}
