using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using v2rayN.Desktop.Satra.Pages;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra;

public interface IPage
{
    Control View { get; }
    void Refresh();
}

/// <summary>Root of the SatraN UI: sidebar navigation, animated page host, toasts and sheets.</summary>
public class SatraShell : Grid
{
    public static SatraShell I { get; private set; } = null!;
    private readonly ContentControl _host = new();
    private readonly Panel _overlay = new() { IsHitTestVisible = false };
    private readonly StackPanel _nav = new() { Spacing = 4 };
    private readonly Border _connectGlow = new() { IsHitTestVisible = false, Opacity = 0 };
    private readonly Dictionary<string, IPage> _pages = [];
    private readonly Dictionary<string, Border> _navItems = [];
    private string _current = "";
    private readonly List<Action> _sheets = [];
    public Window Window { get; }

    public SatraShell(Window window)
    {
        I = this;
        Window = window;
        FlowDirection = L.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Background = P.B(P.Bg);
        ColumnDefinitions = new ColumnDefinitions("232,*");

        // sidebar
        var side = new Border { Background = P.B(Color.Parse("#0E0E11")), Padding = new Thickness(14, 22, 14, 16), BorderBrush = P.B(P.Separator), BorderThickness = new Thickness(0, 0, 1, 0) };
        var logoDot = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#FF8A3D"), 0), new GradientStop(Color.Parse("#E04F00"), 1) } }, Child = Ui.T("S", 16, FontWeight.Black, Colors.White) };
        ((TextBlock)logoDot.Child).HorizontalAlignment = HorizontalAlignment.Center;
        ((TextBlock)logoDot.Child).VerticalAlignment = VerticalAlignment.Center;
        var top = new StackPanel { Spacing = 22 };
        top.Children.Add(Ui.H(10, logoDot, Ui.T("SatraN", 20, FontWeight.Bold)));
        top.Children.Add(_nav);
        var sideGrid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        sideGrid.Children.Add(top);
        var foot = Ui.Caption($"v{Identity.Version} · {Identity.Os}", P.Text3);
        Grid.SetRow(foot, 1);
        sideGrid.Children.Add(foot);
        side.Child = sideGrid;
        Children.Add(side);

        // content
        var content = new Grid();
        _connectGlow.Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#4032D768"), 0), new GradientStop(Color.Parse("#0032D768"), 1) },
        };
        _connectGlow.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(700), Easing = new CubicEaseInOut() } };
        content.Children.Add(_connectGlow);
        content.Children.Add(_host);
        content.Children.Add(_overlay);
        Grid.SetColumn(content, 1);
        Children.Add(content);

        AddNav("home", "◉", new HomePage());
        AddNav("servers", "≡", new ServersPage());
        AddNav("subs", "▣", new SubsPage());
        AddNav("usage", "▥", new UsagePage());
        AddNav("settings", "⚙\uFE0E", new SettingsPage());
        Go("home");

        SatraCore.I.Changed += () => Dispatcher.UIThread.Post(OnCoreChanged);
        SatraCore.I.Toast += m => Dispatcher.UIThread.Post(() => Toast(m));
        OnCoreChanged();
    }

    private void OnCoreChanged()
    {
        _connectGlow.Opacity = SatraCore.I.Phase == ConnPhase.On ? 1 : 0;
        if (_pages.TryGetValue(_current, out var p)) p.Refresh();
    }

    private void AddNav(string key, string glyph, IPage page)
    {
        _pages[key] = page;
        var label = Ui.T(L.S(key), 14.5, FontWeight.Medium, P.Text2);
        var ic = Ui.T(glyph, 15, FontWeight.Bold, P.Text3);
        ic.Width = 22;
        var item = new Border { CornerRadius = Ui.RCtl, Padding = new Thickness(12, 10), Child = Ui.H(10, ic, label), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
        item.Transitions = new Transitions { new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(180) } };
        item.PointerEntered += (_, _) => { if (_current != key) item.Background = P.B(P.Fill, 0.5); };
        item.PointerExited += (_, _) => { if (_current != key) item.Background = Brushes.Transparent; };
        item.PointerReleased += (_, _) => Go(key);
        _navItems[key] = Ui.Pressable(item);
        _nav.Children.Add(item);
    }

    public void Go(string key)
    {
        foreach (var close in _sheets.ToList()) close(); // never leave a sheet over another page
        foreach (var (k, b) in _navItems)
        {
            var on = k == key;
            b.Background = on ? P.B(P.AccentSoft) : Brushes.Transparent;
            var sp = (StackPanel)b.Child!;
            ((TextBlock)sp.Children[0]).Foreground = P.B(on ? P.Accent : P.Text3);
            ((TextBlock)sp.Children[1]).Foreground = P.B(on ? P.Text : P.Text2);
            ((TextBlock)sp.Children[1]).FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium;
        }
        _current = key;
        var page = _pages[key];
        page.Refresh();
        _host.Content = Ui.Enter(page.View);
    }

    public void Toast(string msg)
    {
        var t = new Border
        {
            Background = P.B(P.Raised), CornerRadius = Ui.RPill, Padding = new Thickness(18, 10),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 28),
            BoxShadow = BoxShadows.Parse("0 10 30 0 #66000000"), Child = Ui.T(msg, 13.5, FontWeight.Medium),
        };
        _overlay.Children.Add(Ui.Enter(t));
        DispatcherTimer.RunOnce(() => { t.Opacity = 0; DispatcherTimer.RunOnce(() => _overlay.Children.Remove(t), TimeSpan.FromMilliseconds(400)); }, TimeSpan.FromSeconds(2.6));
    }

    /// <summary>Modal sheet with a scrim; returns a close action.</summary>
    public Action Sheet(string title, Control body, double width = 560)
    {
        var scrim = new Border { Background = P.B(Colors.Black, 0.55) };
        var panel = new Border
        {
            Background = P.B(P.Surface), CornerRadius = new CornerRadius(22), Padding = new Thickness(22, 18), Width = width, MaxHeight = 660,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, BoxShadow = BoxShadows.Parse("0 24 70 0 #99000000"),
        };
        var host = new Grid();
        void Close()
        {
            _sheets.Remove(Close);
            host.Opacity = 0;
            _overlay.IsHitTestVisible = _sheets.Count > 0;
            DispatcherTimer.RunOnce(() => _overlay.Children.Remove(host), TimeSpan.FromMilliseconds(250));
        }
        var head = Ui.Row(Ui.T(title, 19, FontWeight.Bold), Ui.IconBtn("✕", Close, size: 13));
        panel.Child = new DockPanel { Children = { WithDock(head, Dock.Top), new ScrollViewer { Content = body, Margin = new Thickness(0, 12, 0, 0) } } };
        scrim.PointerReleased += (_, _) => Close();
        host.Children.Add(scrim);
        host.Children.Add(panel);
        _overlay.IsHitTestVisible = true;
        _overlay.Children.Add(Ui.Enter(host));
        _sheets.Add(Close);
        return Close;
    }

    private static Control WithDock(Control c, Dock d)
    {
        DockPanel.SetDock(c, d);
        return c;
    }

    public async Task<string?> Prompt(string title, string watermark, bool secret = false)
    {
        var tcs = new TaskCompletionSource<string?>();
        var box = Ui.Input(watermark);
        if (secret) box.PasswordChar = '•';
        Action close = () => { };
        var ok = Ui.Btn(L.S("save"), Ui.Kind.Primary, () => { tcs.TrySetResult(box.Text); close(); });
        var cancel = Ui.Btn(L.S("cancel"), Ui.Kind.Ghost, () => { tcs.TrySetResult(null); close(); });
        close = Sheet(title, Ui.V(14, box, Ui.H(10, ok, cancel)), 420);
        box.AttachedToVisualTree += (_, _) => box.Focus();
        return await tcs.Task;
    }

    public async Task<string?> ClipboardText() => await AvaUtils.GetClipboardData(Window);
}
