using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Live tray menu: status line, Connect/Disconnect that says what it will do, quick server switch, localized labels.
/// Rebuilt only when something visible changed (no work while idle).</summary>
public static class SatraTray
{
    private static NativeMenu? _menu;
    private static TrayIcon? _icon;
    private static NativeMenuItem? _status, _toggle, _servers;
    private static string _sig = "";

    public static void Attach()
    {
        if (Application.Current is not { } app || TrayIcon.GetIcons(app) is not { Count: > 0 } icons) return;
        _icon = icons[0];
        _menu = _icon.Menu;
        if (_menu == null) return;
        var items = _menu.Items.Where(i => i is NativeMenuItem and not NativeMenuItemSeparator).Cast<NativeMenuItem>().ToList();
        if (items.Count < 5) return;
        items[0].Header = L.S("tray_show");
        _toggle = items[1];
        items[2].Header = L.S("add_clip");
        items[3].Header = L.S("tray_subs");
        items[4].Header = L.S("tray_quit");
        _status = new NativeMenuItem { IsEnabled = false };
        _servers = new NativeMenuItem { Header = L.S("servers"), Menu = new NativeMenu() };
        _menu.Items.Insert(0, _status);
        _menu.Items.Insert(1, new NativeMenuItemSeparator());
        _menu.Items.Insert(4, _servers);
        SatraCore.I.Changed += Refresh;
        SatraCore.I.Profiles.ProfileItems.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    private static void Refresh() => Dispatcher.UIThread.Post(() =>
    {
        if (_status == null || _toggle == null || _servers?.Menu == null || _icon == null) return;
        var c = SatraCore.I;
        var act = c.Active;
        var list = c.Profiles.ProfileItems.Take(15).ToList();
        var sig = $"{c.Phase}|{act?.IndexId}|{act?.Remarks}|{string.Join(',', list.Select(p => p.IndexId + p.Remarks))}";
        if (sig == _sig) return;
        _sig = sig;
        var phase = c.Phase switch
        {
            ConnPhase.On => L.S("connected"),
            ConnPhase.Connecting => L.S("connecting"),
            ConnPhase.Disconnecting => L.S("disconnecting"),
            ConnPhase.Failed => L.S("failed"),
            _ => L.S("disconnected"),
        };
        _status.Header = act != null ? $"{(c.Phase == ConnPhase.On ? "●" : "○")} {phase} · {act.Remarks}" : phase;
        _toggle.Header = c.Phase == ConnPhase.On ? L.S("tray_disconnect") : L.S("tray_connect");
        _icon.ToolTipText = act != null ? $"SatraN · {phase} · {act.Remarks}" : $"SatraN · {phase}";
        _servers.Menu.Items.Clear();
        foreach (var p in list)
        {
            var mi = new NativeMenuItem { Header = p.Remarks, ToggleType = MenuItemToggleType.Radio, IsChecked = p.IndexId == act?.IndexId };
            var pp = p;
            mi.Click += async (_, _) =>
            {
                try
                {
                    await c.UseAsync(pp);
                    if (c.Phase == ConnPhase.Off) await c.ConnectAsync();
                }
                catch (Exception ex) { Logging.SaveLog("tray", ex); }
            };
            _servers.Menu.Items.Add(mi);
        }
    });

    /// <summary>Headers for QA (DevDriver "tray").</summary>
    public static IEnumerable<string> Dump() => _menu?.Items.Select(i => i is NativeMenuItem m and not NativeMenuItemSeparator ? (m.Header ?? "") + (m.Menu != null ? $" [{m.Menu.Items.Count}]" : "") : "---") ?? [];
}
