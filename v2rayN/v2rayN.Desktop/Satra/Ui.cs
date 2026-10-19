using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace v2rayN.Desktop.Satra;

/// <summary>SatraN design tokens (mirrors SatraNG's SatraTheme: orange brand, warm-neutral dark surfaces).</summary>
public static class P
{
    public static readonly Color Bg = Color.Parse("#09090B");
    public static readonly Color Surface = Color.Parse("#151518");
    public static readonly Color Raised = Color.Parse("#1E1E22");
    public static readonly Color Fill = Color.Parse("#232328");
    public static readonly Color FillStrong = Color.Parse("#2E2E34");
    public static readonly Color Separator = Color.Parse("#1FFFFFFF");
    public static readonly Color Text = Color.Parse("#F6F6F8");
    public static readonly Color Text2 = Color.Parse("#A3A3AC");
    public static readonly Color Text3 = Color.Parse("#6E6E78");
    public static readonly Color Accent = Color.Parse("#FF6A1A");
    public static readonly Color AccentPressed = Color.Parse("#E85A0C");
    public static readonly Color AccentSoft = Color.Parse("#2B1709");
    public static readonly Color Success = Color.Parse("#32D768");
    public static readonly Color SuccessSoft = Color.Parse("#0F2A19");
    public static readonly Color Warning = Color.Parse("#FFB524");
    public static readonly Color WarningSoft = Color.Parse("#2E2208");
    public static readonly Color Danger = Color.Parse("#FF4F45");
    public static readonly Color DangerSoft = Color.Parse("#2E1210");
    public static readonly Color Info = Color.Parse("#4DA3FF");
    public static readonly Color InfoSoft = Color.Parse("#10233A");

    public static IBrush B(Color c) => new SolidColorBrush(c);
    public static IBrush B(Color c, double a) => new SolidColorBrush(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B));

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>green → orange → red as a fraction gets used up (quota / time).</summary>
    public static Color Level(double usedFraction) => usedFraction < 0.6 ? Success : usedFraction < 0.85 ? Warning : Danger;
}

public static class Ui
{
    public static readonly CornerRadius RCard = new(18);
    public static readonly CornerRadius RCtl = new(12);
    public static readonly CornerRadius RPill = new(999);

    public static TextBlock T(string text, double size = 14, FontWeight weight = FontWeight.Normal, Color? color = null, TextWrapping wrap = TextWrapping.NoWrap) =>
        new TextBlock()
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = P.B(color ?? P.Text), TextWrapping = wrap,
            TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            FlowDirection = text.StartsWith('\u200E') ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
        }.Inherit(text);

    /// <summary>Numbers with units stay left-to-right; everything else inherits the page direction.</summary>
    private static TextBlock Inherit(this TextBlock t, string text)
    {
        if (!text.StartsWith('\u200E')) t.ClearValue(Visual.FlowDirectionProperty);
        return t;
    }

    /// <summary>Marks a TextBlock whose text is updated later with numbers/units as left-to-right.</summary>
    public static TextBlock Num(TextBlock t)
    {
        t.FlowDirection = FlowDirection.LeftToRight;
        if (L.Rtl) t.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        return t;
    }

    public static TextBlock Title(string text) => T(text, 30, FontWeight.Bold);
    public static TextBlock Section(string text) => T(text.ToUpperInvariant(), 11.5, FontWeight.SemiBold, P.Text3);
    public static TextBlock Caption(string text, Color? c = null) => T(text, 12.5, FontWeight.Normal, c ?? P.Text2, TextWrapping.Wrap);

    public static Border Card(Control child, Thickness? pad = null, Color? bg = null) => new()
    {
        Background = P.B(bg ?? P.Surface),
        CornerRadius = RCard,
        Padding = pad ?? new Thickness(18, 16),
        Child = child,
        BoxShadow = BoxShadows.Parse("0 6 24 0 #33000000"),
    };

    public static StackPanel V(double spacing, params Control[] kids)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    public static StackPanel H(double spacing, params Control[] kids)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    /// <summary>Row: left content stretches, right content hugs.</summary>
    public static Grid Row(Control left, Control? right = null, double gap = 12)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        left.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(left);
        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(gap, 0, 0, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
        }
        return g;
    }

    public static Border Pill(string text, Color fg, Color bg, double size = 11) => new()
    {
        Background = P.B(bg),
        CornerRadius = RPill,
        Padding = new Thickness(8, 2),
        Child = T(text, size, FontWeight.SemiBold, fg),
        VerticalAlignment = VerticalAlignment.Center,
        FlowDirection = text.StartsWith('\u200E') ? FlowDirection.LeftToRight : (L.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight),
    };

    public static Border Sep() => new() { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(0, 4) };

    /// <summary>Press-scale + hover-tint behaviour shared by every Satra button (spring-like overshoot curve).</summary>
    public static T Pressable<T>(T c) where T : Control
    {
        c.RenderTransformOrigin = RelativePoint.Center;
        c.RenderTransform = TransformOperations.Parse("scale(1)");
        c.Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new BackEaseOut() },
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
        };
        c.PointerPressed += (_, _) => c.RenderTransform = TransformOperations.Parse("scale(0.95)");
        c.PointerReleased += (_, _) => c.RenderTransform = TransformOperations.Parse("scale(1)");
        c.PointerExited += (_, _) => c.RenderTransform = TransformOperations.Parse("scale(1)");
        return c;
    }

    public enum Kind { Primary, Tinted, Ghost, Danger, Success }

    public static Button Btn(string text, Kind kind = Kind.Primary, Action? onClick = null, string? icon = null, double size = 14)
    {
        var (bg, fg) = kind switch
        {
            Kind.Primary => (P.Accent, Colors.White),
            Kind.Tinted => (P.AccentSoft, P.Accent),
            Kind.Danger => (P.DangerSoft, P.Danger),
            Kind.Success => (P.SuccessSoft, P.Success),
            _ => (P.Fill, P.Text),
        };
        var content = icon == null ? (Control)T(text, size, FontWeight.SemiBold, fg) : H(6, T(icon, size, FontWeight.SemiBold, fg), T(text, size, FontWeight.SemiBold, fg));
        var b = new Button
        {
            Content = content,
            Background = P.B(bg),
            Foreground = P.B(fg),
            CornerRadius = RCtl,
            Padding = new Thickness(16, 9),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "satra" },
        };
        if (onClick != null) b.Click += (_, _) => onClick();
        return Pressable(b);
    }

    public static Button IconBtn(string glyph, Action onClick, string? tip = null, Color? fg = null, double size = 16)
    {
        var b = new Button
        {
            Content = T(glyph, size, FontWeight.SemiBold, fg ?? P.Text2),
            Background = P.B(P.Fill),
            CornerRadius = RPill,
            Width = 34, Height = 34,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Classes = { "satra" },
        };
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => onClick();
        return Pressable(b);
    }

    public static TextBox Input(string watermark, string text = "") => new()
    {
        Watermark = watermark,
        Text = text,
        Background = P.B(P.Fill),
        Foreground = P.B(P.Text),
        BorderThickness = new Thickness(0),
        CornerRadius = RCtl,
        Padding = new Thickness(12, 9),
        FontSize = 14,
        Classes = { "satra" },
    };

    /// <summary>Thin rounded progress bar coloured by level.</summary>
    public static Control Bar(double fraction, Color? color = null, double height = 6)
    {
        fraction = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        var g = new Grid { Height = height, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(fraction, 0.0001)}*,{Math.Max(1 - fraction, 0.0001)}*") };
        var track = new Border { Background = P.B(P.Fill), CornerRadius = RPill };
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        g.Children.Add(new Border { Background = P.B(color ?? P.Level(fraction)), CornerRadius = RPill });
        return g;
    }

    public static ToggleSwitch Switch(bool on, Action<bool> changed)
    {
        var t = new ToggleSwitch { IsChecked = on, OnContent = "", OffContent = "", Classes = { "satra" } };
        t.IsCheckedChanged += (_, _) => changed(t.IsChecked == true);
        return t;
    }

    /// <summary>Grouped-inset list row (Apple-style): title + subtitle on the left, accessory on the right.</summary>
    public static Border ListRow(string title, string? subtitle, Control? accessory, Action? onClick = null, string? glyph = null, Color? glyphColor = null)
    {
        var left = new StackPanel { Spacing = 2 };
        left.Children.Add(T(title, 14.5, FontWeight.Medium));
        if (!string.IsNullOrEmpty(subtitle)) left.Children.Add(Caption(subtitle!));
        Control content = left;
        if (glyph != null)
        {
            var ic = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = P.B(glyphColor ?? P.Accent, 0.16), Child = T(glyph, 15, FontWeight.SemiBold, glyphColor ?? P.Accent), VerticalAlignment = VerticalAlignment.Center };
            ((TextBlock)ic.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)ic.Child).VerticalAlignment = VerticalAlignment.Center;
            content = H(12, ic, left);
        }
        var row = new Border { Padding = new Thickness(14, 11), Background = Brushes.Transparent, Child = Row(content, accessory ?? (onClick != null ? T("›", 18, FontWeight.Normal, P.Text3) : null)) };
        if (onClick != null)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            row.PointerEntered += (_, _) => row.Background = P.B(P.Fill, 0.6);
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.PointerReleased += (_, _) => onClick();
        }
        return row;
    }

    public static Border Group(params Control[] rows)
    {
        var s = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) s.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            s.Children.Add(rows[i]);
        }
        return new Border { Background = P.B(P.Surface), CornerRadius = RCard, Child = s, ClipToBounds = true };
    }

    /// <summary>Fade + rise entrance used when a page or card appears.</summary>
    public static T Enter<T>(T c, int delayMs = 0) where T : Control
    {
        c.Opacity = 0;
        c.RenderTransform = TransformOperations.Parse("translateY(10px)");
        c.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(420), Easing = new BackEaseOut() },
        };
        DispatcherTimer.RunOnce(() =>
        {
            c.Opacity = 1;
            c.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, TimeSpan.FromMilliseconds(30 + delayMs));
        return c;
    }

    public static string Bytes(double b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return Ltr(i == 0 ? $"{b:0} {u[i]}" : $"{b:0.##} {u[i]}");
    }

    public static string Speed(double bps) => Ltr(Bytes(bps) + "/s");

    /// <summary>Keeps numbers with units ("4 ms", "1.2 GB") in reading order inside RTL text.</summary>
    public static string Ltr(string s) => s.Length > 0 && s[0] == '\u200E' ? s : "\u200E" + s + "\u200E";
}
