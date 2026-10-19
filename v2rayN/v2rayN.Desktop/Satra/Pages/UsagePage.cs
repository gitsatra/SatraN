using Avalonia.Layout;
using v2rayN.Desktop.Satra.Controls;
using v2rayN.Desktop.Satra.Services;

namespace v2rayN.Desktop.Satra.Pages;

/// <summary>Usage: 7/30-day data and connected-hours charts, totals, and limits on this computer.</summary>
public class UsagePage : IPage
{
    private readonly Grid _root = new() { Margin = new Thickness(36, 28, 36, 0), RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly StackPanel _body = new() { Spacing = 14, Margin = new Thickness(0, 18, 0, 28) };
    private readonly BarChart _data = new() { BarColor = P.Accent, Format = Ui.Bytes };
    private readonly BarChart _hours = new() { BarColor = P.Info, Height = 120, Format = v => $"{v:0.#} h" };
    private readonly TextBlock _sumDown = Ui.Num(Ui.T("", 22, FontWeight.Bold)), _sumUp = Ui.Num(Ui.T("", 22, FontWeight.Bold)), _sumHours = Ui.Num(Ui.T("", 22, FontWeight.Bold));
    private readonly StackPanel _limits = new() { Spacing = 0 };
    private int _days = 7;
    private readonly Border _seg7, _seg30;

    public Control View => _root;

    public UsagePage()
    {
        Border Seg(string t, int d)
        {
            var b = Ui.Pressable(new Border { CornerRadius = Ui.RPill, Padding = new Thickness(14, 6), Cursor = new Cursor(StandardCursorType.Hand), Child = Ui.T(t, 13, FontWeight.SemiBold) });
            b.PointerReleased += (_, _) => { _days = d; Refresh(); };
            return b;
        }
        _seg7 = Seg(L.S("last7"), 7);
        _seg30 = Seg(L.S("last30"), 30);
        var seg = new Border { Background = P.B(P.Fill), CornerRadius = Ui.RPill, Padding = new Thickness(3), Child = Ui.H(2, _seg7, _seg30) };
        _root.Children.Add(Ui.Row(Ui.Title(L.S("usage")), seg));

        var sums = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        Control S(string l, TextBlock v, int col) { var x = Ui.V(4, Ui.Section(l), v); Grid.SetColumn(x, col); return x; }
        sums.Children.Add(S(L.S("download"), _sumDown, 0));
        sums.Children.Add(S(L.S("upload"), _sumUp, 1));
        sums.Children.Add(S(L.S("hours_per_day").Split(' ')[0], _sumHours, 2));
        _body.Children.Add(Ui.Card(sums, new Thickness(20, 16)));
        _body.Children.Add(Ui.Card(Ui.V(12, Ui.Section(L.S("data_per_day")), _data), new Thickness(20, 16)));
        _body.Children.Add(Ui.Card(Ui.V(12, Ui.Section(L.S("hours_per_day")), _hours), new Thickness(20, 16)));
        _body.Children.Add(Ui.Row(Ui.Section(L.S("local_limits")), Ui.Btn(L.S("add_limit"), Ui.Kind.Tinted, () => AddLimitSheet(), "+", 13)));
        _body.Children.Add(new Border { Background = P.B(P.Surface), CornerRadius = Ui.RCard, ClipToBounds = true, Child = _limits });
        _body.Children.Add(Ui.Caption(L.S("server_limits") + " → " + L.S("subs") + " · " + L.S("limits"), P.Text3));
        var sv = new ScrollViewer { Content = _body };
        Grid.SetRow(sv, 1);
        _root.Children.Add(sv);
    }

    public void Refresh()
    {
        foreach (var (b, on) in new[] { (_seg7, _days == 7), (_seg30, _days == 30) })
        {
            b.Background = on ? P.B(P.Raised) : Brushes.Transparent;
            ((TextBlock)b.Child!).Foreground = P.B(on ? P.Text : P.Text2);
        }
        var days = Enumerable.Range(0, _days).Select(i => DateTime.Today.AddDays(i - _days + 1)).ToList();
        var u = days.Select(d => SatraState.I.Days.GetValueOrDefault(d.ToString("yyyy-MM-dd"))).ToList();
        var labels = days.Select(d => _days == 7 ? d.ToString("ddd", L.Culture) : d.Day.ToString()).ToArray();
        _data.SetData(u.Select(x => (double)((x?.Up ?? 0) + (x?.Down ?? 0))).ToArray(), labels);
        _hours.SetData(u.Select(x => (x?.Seconds ?? 0) / 3600.0).ToArray(), labels);
        _sumDown.Text = Ui.Bytes(u.Sum(x => x?.Down ?? 0));
        _sumUp.Text = Ui.Bytes(u.Sum(x => x?.Up ?? 0));
        _sumHours.Text = $"{u.Sum(x => x?.Seconds ?? 0) / 3600.0:0.#} h";
        BuildLimits();
    }

    private string _limSig = "";
    private void BuildLimits()
    {
        var sig = string.Join(",", SatraState.I.Limits.Select(l => $"{l.Id}{l.UsedBytes >> 20}{l.Exhausted}{l.Enabled}"));
        if (sig == _limSig && _limits.Children.Count > 0) return;
        _limSig = sig;
        _limits.Children.Clear();
        if (SatraState.I.Limits.Count == 0)
        {
            _limits.Children.Add(new Border { Padding = new Thickness(16, 14), Child = Ui.Caption(L.S("no_limits"), P.Text3) });
            return;
        }
        var first = true;
        foreach (var l in SatraState.I.Limits)
        {
            if (!first) _limits.Children.Add(new Border { Height = 1, Background = P.B(P.Separator), Margin = new Thickness(14, 0, 0, 0) });
            first = false;
            var cap = l.QuotaBytes + l.TopupBytes;
            var tracked = l.Type == "all";
            var frac = tracked ? (cap > 0 ? Math.Min(1, (double)l.UsedBytes / cap) : 1) : (l.Exhausted || l.QuotaBytes == 0 ? 1 : 0);
            var what = l.Type switch { "domain" => L.S("site"), "process" => L.S("app"), _ => L.S("all_traffic") };
            var status = l.Exhausted || (!tracked && l.QuotaBytes == 0)
                ? Ui.Pill(L.S("blocked"), P.Danger, P.DangerSoft)
                : tracked ? Ui.Pill($"{Ui.Bytes(l.UsedBytes)} / {Ui.Bytes(cap)}", P.Text2, P.Fill) : Ui.Pill(Ui.Bytes(cap) + " " + SubsPage.PeriodText(l.Period), P.Text2, P.Fill);
            var lim = l;
            var right = Ui.H(4, status,
                Ui.IconBtn("+", () => { lim.TopupBytes += 500L << 20; lim.Exhausted = false; _ = SatraCore.I.ApplyLocalLimitsAsync(); _limSig = ""; BuildLimits(); }, L.S("topup") + " 500 MB", P.Success, 15),
                Ui.Switch(l.Enabled, v => { lim.Enabled = v; _ = SatraCore.I.ApplyLocalLimitsAsync(); }),
                Ui.IconBtn("🗑", () => { SatraState.I.Limits.Remove(lim); _ = SatraCore.I.ApplyLocalLimitsAsync(); _limSig = ""; BuildLimits(); }, L.S("delete"), P.Danger, 14));
            var head = Ui.Row(Ui.V(2, Ui.T(l.Name, 14.5, FontWeight.SemiBold), Ui.Caption($"{what} · {string.Join(", ", l.Values)} · {SubsPage.PeriodText(l.Period)}", P.Text3)), right);
            _limits.Children.Add(new Border { Padding = new Thickness(14, 12), Child = Ui.V(8, head, Ui.Bar(frac, P.Level(frac))) });
        }
    }

    private void AddLimitSheet()
    {
        var type = new ComboBox { ItemsSource = new[] { L.S("all_traffic"), L.S("site"), L.S("app") }, SelectedIndex = 0, MinWidth = 150 };
        var vals = Ui.Input("youtube.com, instagram.com  /  telegram.exe");
        var mb = Ui.Input("MB", "1024");
        mb.Width = 100;
        var per = new ComboBox { ItemsSource = new[] { L.S("per_day"), L.S("per_week"), L.S("per_month") }, SelectedIndex = 0 };
        var note = Ui.Caption("", P.Text3);
        void UpdNote()
        {
            vals.IsVisible = type.SelectedIndex != 0;
            note.Text = type.SelectedIndex == 0
                ? L.S("limit_all_note")
                : L.S("limit_site_note");
        }
        type.SelectionChanged += (_, _) => UpdNote();
        UpdNote();
        Action close = () => { };
        var save = Ui.Btn(L.S("save"), Ui.Kind.Primary, () =>
        {
            if (!long.TryParse(mb.Text, out var m) || m < 0) return;
            var t = type.SelectedIndex switch { 1 => "domain", 2 => "process", _ => "all" };
            var values = (vals.Text ?? "").Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
            if (t != "all" && values.Count == 0) return;
            SatraState.I.Limits.Add(new LocalLimit
            {
                Name = t == "all" ? L.S("all_traffic") : values[0], Type = t, Values = values, QuotaBytes = m << 20,
                Period = per.SelectedIndex switch { 1 => "weekly", 2 => "monthly", _ => "daily" },
            });
            _ = SatraCore.I.ApplyLocalLimitsAsync();
            _limSig = "";
            BuildLimits();
            close();
        });
        close = SatraShell.I.Sheet(L.S("add_limit"), Ui.V(12, Ui.Section(L.S("what")), type, vals, Ui.H(8, mb, Mb(), per), note, save), 480);
    }

    private static TextBlock Mb()
    {
        var t = Ui.T("MB", 13, FontWeight.Medium, P.Text2);
        t.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        return t;
    }
}
