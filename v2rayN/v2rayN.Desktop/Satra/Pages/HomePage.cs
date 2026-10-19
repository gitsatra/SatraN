using Avalonia.Layout;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Home: one non-scrolling screen – connect orb, status, active server, live speed and totals.</summary>
public class HomePage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 28), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
    private readonly ConnectOrb _orb = new() { Width = 260, Height = 260, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _status = Ui.T("", 26, FontWeight.Bold);
    private readonly TextBlock _hint = Ui.T("", 13.5, FontWeight.Normal, P.Text2);
    private readonly TextBlock _timer = Ui.T("", 13.5, FontWeight.Medium, P.Text2);
    private readonly ContentControl _server = new();
    private readonly TextBlock _down = Ui.T("0 B/s", 22, FontWeight.Bold), _up = Ui.T("0 B/s", 22, FontWeight.Bold);
    private readonly TextBlock _downTot = Ui.Caption(""), _upTot = Ui.Caption("");
    private readonly TextBlock _today = Ui.T("", 15, FontWeight.SemiBold);
    private readonly Sparkline _spark = new() { Height = 54 };
    private readonly Border _modeChip = new();
    private string _lastServerKey = "";

    public Control View => _root;

    public HomePage()
    {
        var head = Ui.Row(Ui.Title(L.S("home")), Ui.H(8,
            Ui.IconBtn("⟳", () => _ = SatraCore.I.UpdateSubsAsync(), L.S("update_all")),
            Ui.Btn(L.S("add"), Ui.Kind.Tinted, () => _ = AddAsync(), "+")));
        _root.Children.Add(head);

        _status.HorizontalAlignment = _hint.HorizontalAlignment = _timer.HorizontalAlignment = HorizontalAlignment.Center;
        _orb.Clicked += () => _ = SatraCore.I.ToggleAsync();
        var center = Ui.V(10, _orb, _status, _timer, _hint);
        center.VerticalAlignment = VerticalAlignment.Center;
        center.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(center, 1);
        _root.Children.Add(center);

        _server.Margin = new Thickness(0, 0, 0, 14);
        _server.MaxWidth = 640;
        Grid.SetRow(_server, 2);
        _root.Children.Add(_server);

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), MaxWidth = 640 };
        Control Stat(string label, TextBlock big, TextBlock small, Color dot)
        {
            var d = new Border { Width = 8, Height = 8, CornerRadius = Ui.RPill, Background = P.B(dot), VerticalAlignment = VerticalAlignment.Center };
            return Ui.V(4, Ui.H(6, d, Ui.Section(label)), big, small);
        }
        var c1 = Stat(L.S("download"), _down, _downTot, P.Success);
        var c2 = Stat(L.S("upload"), _up, _upTot, P.Accent);
        var c3 = Ui.V(4, Ui.Section(L.S("today")), _today, _modeChip);
        Grid.SetColumn(c2, 1);
        Grid.SetColumn(c3, 2);
        stats.Children.Add(c1);
        stats.Children.Add(c2);
        stats.Children.Add(c3);
        var statCard = Ui.Card(Ui.V(12, stats, _spark), new Thickness(20, 16));
        statCard.MaxWidth = 640;
        Grid.SetRow(statCard, 3);
        _root.Children.Add(statCard);

        SatraCore.I.Speed += (_, _) => Dispatcher.UIThread.Post(UpdateSpeed);
    }

    private async Task AddAsync() => await SatraCore.I.AddFromClipboardAsync(await SatraShell.I.ClipboardText());

    private void UpdateSpeed()
    {
        var c = SatraCore.I;
        _down.Text = Ui.Speed(c.DownRate);
        _up.Text = Ui.Speed(c.UpRate);
        _downTot.Text = $"{L.S("session")} {Ui.Bytes(c.SessionDown)}";
        _upTot.Text = $"{L.S("session")} {Ui.Bytes(c.SessionUp)}";
        var d = SatraState.I.Today();
        _today.Text = $"↓ {Ui.Bytes(d.Down)}  ↑ {Ui.Bytes(d.Up)}";
        _spark.LineColor = c.Phase == ConnPhase.On ? P.Success : P.Accent;
        _spark.SetData(c.History);
    }

    public void Refresh()
    {
        var c = SatraCore.I;
        _orb.Phase = c.Phase;
        (_status.Text, _hint.Text, var col) = c.Phase switch
        {
            ConnPhase.On => (L.S("connected"), L.S("tap_disconnect"), P.Success),
            ConnPhase.Connecting => (L.S("connecting"), SatraState.I.SmartConnect ? L.S("smart_desc") : "", P.Accent),
            ConnPhase.Disconnecting => (L.S("disconnecting"), "", P.Text2),
            ConnPhase.Failed => (L.S("failed"), L.S("retry"), P.Danger),
            _ => (L.S("disconnected"), L.S("tap_connect"), P.Text),
        };
        _status.Foreground = P.B(col);
        _timer.Text = c.Since is { } s ? (DateTime.Now - s).ToString(@"hh\:mm\:ss") : "";
        _timer.IsVisible = c.Phase == ConnPhase.On;

        var cfg = AppManager.Instance.Config;
        var mode = cfg.TunModeItem.EnableTun ? L.S("mode_tun") : cfg.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange || c.Phase != ConnPhase.On ? L.S("mode_proxy") : L.S("mode_proxy_only");
        _modeChip.Child = Ui.Pill(mode, P.Text2, P.Fill, 10.5).Child;
        _modeChip.Background = P.B(P.Fill);
        _modeChip.CornerRadius = Ui.RPill;
        _modeChip.Padding = new Thickness(8, 2);
        _modeChip.HorizontalAlignment = HorizontalAlignment.Left;

        var a = c.Active;
        var key = $"{a?.IndexId}|{a?.Delay}|{a != null && NetBadges.ExitCc(a) is { } cc2}|{c.Phase}";
        if (key != _lastServerKey)
        {
            _lastServerKey = key;
            _server.Content = ServerCard(a);
        }
        UpdateSpeed();
    }

    private Control ServerCard(ProfileItemModel? a)
    {
        if (a == null)
        {
            return Ui.Card(Ui.Row(Ui.V(4, Ui.T(L.S("no_server"), 16, FontWeight.SemiBold), Ui.Caption(L.S("add_hint"))),
                Ui.Btn(L.S("add_clip"), Ui.Kind.Primary, () => _ = AddAsync())), new Thickness(18, 14));
        }
        var cc = NetBadges.ExitCc(a);
        var flag = Ui.T(NetBadges.Flag(cc), 26);
        var sub = ServersPage.SubName(a.Subid);
        var meta = new List<Control>();
        if (!string.IsNullOrEmpty(sub)) meta.Add(Ui.Caption(sub, P.Text3));
        if (SatraState.I.SmartConnect) meta.Add(Ui.Pill("⚡ " + L.S("smart"), P.Accent, P.AccentSoft, 10.5));
        var left = Ui.H(12, flag, Ui.V(3, Ui.T(a.Remarks, 15.5, FontWeight.SemiBold), Ui.H(8, meta.ToArray())));
        var right = Ui.H(10, ServersPage.PingPill(a.Delay), Ui.Btn(L.S("servers"), Ui.Kind.Ghost, () => SatraShell.I.Go("servers"), size: 13));
        return Ui.Enter(Ui.Card(Ui.Row(left, right), new Thickness(18, 12)));
    }
}
