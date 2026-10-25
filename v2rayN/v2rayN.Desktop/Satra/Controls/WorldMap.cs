using Avalonia.Animation;
using Avalonia.Platform;

namespace v2rayN.Desktop.Satra.Controls;

/// <summary>
/// Home backdrop while connected: monochrome, very low-contrast world map (Natural Earth 110m land, ~9 KB, offline)
/// with one orange dot at the exit country. One fade/grow when the location is found; no looping animation.
/// Same data and framing as SatraNG's WorldMapBackdrop.
/// </summary>
public class WorldMap : Control
{
    private const float Lat0 = 84f, Lat1 = -58f;
    private const string Centroids = "AD42.5,1.5;AE23.5,54.5;AF34.2,66.5;AG17.4,-61.8;AI18.2,-63.0;AL40.7,20.1;AM40.5,44.8;AO-12.2,18.0;AQ-79.8,35.9;AR-33.5,-64.2;AS-14.3,-170.7;AT47.5,14.1;AU-24.1,134.0;AW12.5,-70.0;AX60.2,19.9;AZ40.4,47.2;BA44.1,18.1;BB13.2,-59.6;BD24.2,89.7;BE50.8,4.8;BF12.7,-1.4;BG42.5,25.2;BH26.1,50.6;BI-3.3,29.9;BJ10.3,2.4;BL17.9,-62.8;BM32.3,-64.8;BN4.4,114.6;BO-16.7,-64.6;BR-12.1,-49.6;BS26.4,-77.1;BT27.5,90.0;BW-22.1,24.2;BY53.8,28.4;BZ17.2,-88.7;CA60.3,-101.9;CD-1.9,23.5;CF7.0,20.9;CG0.1,15.9;CH46.7,7.5;CI7.5,-5.6;CK-21.2,-159.8;CL-38.2,-72.3;CM4.6,12.5;CN32.5,106.3;TW23.7,120.9;CO3.4,-73.2;CR10.1,-84.1;CU21.3,-78.0;CV15.1,-23.6;CW12.1,-68.9;CY34.9,33.1;CZ49.9,15.4;DE51.0,9.7;DJ12.0,42.5;DK56.0,9.0;DM15.5,-61.3;DO19.1,-70.7;DZ27.4,2.8;EC-1.3,-78.2;EE58.7,25.9;EG26.2,29.4;EH24.0,-12.6;ER15.8,38.3;ES40.1,-3.5;ET8.0,39.1;FI63.3,27.3;FJ-17.8,178.0;FK-51.6,-58.7;FM6.9,158.2;FO62.2,-7.1;FR46.7,2.6;GA-0.4,11.8;GB54.4,-2.1;GD12.1,-61.7;GE41.9,43.7;GG49.5,-2.6;GH7.7,-1.0;GL74.3,-39.3;GM13.6,-15.0;GN10.6,-10.0;GQ2.3,9.0;GR39.5,21.7;GS-55.7,-31.1;GT15.0,-90.5;GU13.4,144.7;GW12.2,-14.5;GY5.1,-58.9;HK22.4,114.1;HM-53.1,73.5;HN14.8,-86.9;HR45.8,16.4;HT19.3,-72.2;HU47.1,19.4;ID-1.0,101.9;IE53.1,-7.8;IL30.9,34.8;IM54.2,-4.5;IN22.7,79.4;IO-6.2,71.3;IQ33.1,43.3;IR32.2,54.9;IS64.8,-18.7;IT44.7,11.1;JE49.2,-2.1;JM18.1,-77.3;JO30.8,36.4;JP36.1,138.4;KE0.5,37.9;KG41.7,74.5;KH12.6,104.5;KI1.8,-157.4;KM-11.7,43.3;KN17.3,-62.8;KP39.9,126.4;KR36.4,128.1;KW29.4,47.3;KY19.3,-81.2;KZ49.1,68.7;LA19.4,102.5;LB34.1,36.0;LC13.9,-61.0;LI47.1,9.6;LK7.6,80.7;LR6.4,-9.5;LS-29.5,28.2;LT55.1,24.1;LU49.7,6.1;LV57.1,25.5;LY26.6,18.0;MA31.7,-7.2;MC43.7,7.4;MD47.4,28.5;ME42.8,19.1;MF18.1,-63.0;MG-18.6,46.7;MH7.1,171.2;MK41.6,21.6;ML18.7,-2.0;MM21.6,95.8;MN46.0,104.2;MO22.1,113.6;MP15.2,145.7;MR19.6,-9.7;MS16.7,-62.2;MT35.9,14.4;MU-20.3,57.6;MV4.2,73.5;MW-13.4,33.6;MX23.9,-102.3;MY2.5,113.8;MZ-13.9,37.8;NA-20.6,17.1;NC-21.1,165.1;NE17.4,9.5;NF-29.0,168.0;NG9.4,7.5;NI12.7,-85.1;NL52.4,5.6;NO61.4,9.7;NP28.3,83.6;NR-0.5,166.9;NU-19.0,-169.9;NZ-39.8,172.8;OM22.1,57.3;PA8.7,-80.4;PE-13.0,-72.9;PF-17.6,-149.5;PG-5.7,143.9;PH11.2,122.5;PK29.3,68.5;PL52.0,19.5;PM47.0,-56.3;PN-24.4,-128.3;PR18.2,-66.5;PS32.0,35.3;PT39.6,-8.3;PW7.5,134.6;PY-21.7,-60.1;QA25.2,51.1;RO45.7,25.0;RS44.2,20.8;RU58.2,44.7;RW-1.9,30.1;SA23.8,44.7;SB-8.0,159.2;SC-4.7,55.5;SD16.3,29.3;SE65.9,19.0;SG1.4,103.8;SH-16.0,-5.7;SI46.1,14.9;SK48.7,19.0;SL8.6,-11.8;SM43.9,12.4;SN15.1,-14.8;SO3.6,45.2;SR4.1,-55.9;SS7.2,30.4;ST1.0,7.0;SV13.7,-88.9;SX18.0,-63.1;SY35.0,38.3;SZ-26.5,31.5;TC21.8,-71.8;TD15.1,18.6;TF-49.3,69.1;TG8.8,1.1;TH15.5,101.1;TJ38.2,72.6;TL-8.8,125.9;TM39.9,58.7;TN33.7,9.0;TO-21.2,-175.2;TR39.3,34.5;TT11.0,-60.9;TV-8.5,179.2;TZ-6.1,35.0;UA49.7,32.1;UG2.0,32.9;US39.5,-97.5;UY-33.0,-56.0;UZ41.7,64.0;VA41.9,12.5;VC13.1,-61.3;VE7.2,-64.6;VG18.4,-64.6;VI17.7,-64.8;VN21.7,105.4;VU-15.4,166.9;WF-14.3,-178.1;WS-13.6,-172.4;XK42.6,20.9;YE15.3,45.9;ZA-29.7,23.7;ZM-14.7,26.4;ZW-18.9,29.9";
    private static Dictionary<string, (float lat, float lon)>? _table;
    private static (StreamGeometry geo, double w, double h)? _land;
    private (float lat, float lon)? _point;
    private double _dot;
    private DispatcherTimer? _anim;
    private DateTime _animAt;

    public WorldMap()
    {
        IsHitTestVisible = false;
        FlowDirection = FlowDirection.LeftToRight; // never mirror the map in RTL (fa)
        Opacity = 0;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(1100), Easing = new Avalonia.Animation.Easings.CubicEaseOut() }];
    }

    public static (float lat, float lon)? Centroid(string? cc)
    {
        if (string.IsNullOrEmpty(cc)) return null;
        _table ??= Centroids.Split(';').Select(e => (k: e[..2], v: e[2..].Split(','))).GroupBy(x => x.k)
            .ToDictionary(g => g.Key, g => (float.Parse(g.First().v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(g.First().v[1], System.Globalization.CultureInfo.InvariantCulture)));
        return _table.TryGetValue(cc.ToUpperInvariant(), out var p) ? p : null;
    }

    private static (StreamGeometry, double, double)? Land()
    {
        if (_land != null) return _land;
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://SatraN/Assets/Map/world_land.bin"));
            using var br = new BinaryReader(s);
            int w = br.ReadUInt16(), h = br.ReadUInt16(), n = br.ReadUInt16();
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                for (var k = 0; k < n; k++)
                {
                    int cnt = br.ReadUInt16();
                    for (var i = 0; i < cnt; i++)
                    {
                        var pt = new Point(br.ReadUInt16(), br.ReadUInt16());
                        if (i == 0) c.BeginFigure(pt, true); else c.LineTo(pt);
                    }
                    if (cnt > 0) c.EndFigure(true);
                }
            }
            _land = (g, w - 1, h - 1);
        }
        catch (Exception ex) { Logging.SaveLog("satra worldmap", ex); }
        return _land;
    }

    /// <summary>Show the map at a country (null hides it with a short fade).</summary>
    public void Show(string? cc)
    {
        var p = Centroid(cc);
        if (p == null || Land() == null) { Opacity = 0; _point = null; return; }
        if (_point == p && Opacity > 0) return;
        _point = p;
        _dot = 0;
        Opacity = 1;
        _animAt = DateTime.Now;
        _anim ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            var t = (DateTime.Now - _animAt).TotalMilliseconds - 350;
            _dot = Math.Clamp(t / 900, 0, 1);
            _dot = 1 - Math.Pow(1 - _dot, 3);
            InvalidateVisual();
            if (t >= 900) _anim!.Stop();
        });
        _anim.Start();
        InvalidateVisual();
    }

    /// <summary>The connect button (horizontal no-go zone) and the block whose height the map may use.</summary>
    public Control? Avoid, Band;

    private Rect? FreeBox(double W, double H)
    {
        if (Avoid == null || Band == null) return null;
        var o = Avoid.TranslatePoint(new Point(0, 0), this);
        var bt = Band.TranslatePoint(new Point(0, 0), this);
        if (o is not { } op || bt is not { } bp) return null;
        // Right side only: from just right of the connect button to the page's right edge.
        const double gap = 36, edge = 24;
        var x = op.X + Avoid.Bounds.Width + gap;
        var r = new Rect(x, bp.Y, Math.Max(0, W - edge - x), Band.Bounds.Height);
        return r.Width < 140 || r.Height < 60 ? null : r;
    }

    public override void Render(DrawingContext dc)
    {
        if (_point is not { } p || Land() is not { } l) return;
        var (geo, mw, mh) = l;
        double W = Bounds.Width, H = Bounds.Height;
        if (W <= 0 || H <= 0) return;
        var px = (p.lon + 180f) / 360f * mw;
        var py = (Lat0 - Math.Clamp(p.lat, Lat1, Lat0)) / (Lat0 - Lat1) * mh;
        // Whole map, fitted into the free space beside the connect button (never under it). Too little room → hidden.
        var box = FreeBox(W, H);
        if (box is not { } fb) return;
        var s = Math.Min(fb.Width / mw, fb.Height / mh);
        var tx = fb.Right - mw * s; // hug the right edge
        var ty = fb.Y + (fb.Height - mh * s) / 2;
        var dark = (P.Bg.R + P.Bg.G + P.Bg.B) < 384;
        var ink = dark ? Colors.White : Colors.Black;
        var land = new SolidColorBrush(ink, dark ? 0.06 : 0.07);
        var fade = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(tx, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(tx + mw * s * 0.35, 0, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) }
        };
        using (dc.PushOpacityMask(fade, new Rect(tx, ty, mw * s, mh * s)))
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(tx, ty)))
            dc.DrawGeometry(land, null, geo);
        var d = _dot;
        if (d <= 0) return;
        var c = new Point(tx + px * s, ty + py * s);
        const double r = 4.5;
        dc.DrawEllipse(new SolidColorBrush(P.Accent, 0.16 * d), null, c, r * (1 + 2.6 * d), r * (1 + 2.6 * d));
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(P.Accent, 0.35 * d), 1), c, r * (1 + 1.2 * d), r * (1 + 1.2 * d));
        dc.DrawEllipse(new SolidColorBrush(P.Accent, d), null, c, r * (0.6 + 0.4 * d), r * (0.6 + 0.4 * d));
    }
}
