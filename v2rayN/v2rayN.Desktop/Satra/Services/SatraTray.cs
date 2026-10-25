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
    private static NativeMenuItem? _status, _toggle, _servers, _show, _add, _subs, _quit, _modes, _conn;
    private static string _sig = "";

    public static void Attach()
    {
        if (Application.Current is not { } app || TrayIcon.GetIcons(app) is not { Count: > 0 } icons) return;
        _icon = icons[0];
        _menu = _icon.Menu;
        if (_menu == null) return;
        var items = _menu.Items.Where(i => i is NativeMenuItem and not NativeMenuItemSeparator).Cast<NativeMenuItem>().ToList();
        if (items.Count < 5) return;
        _show = items[0]; _toggle = items[1]; _add = items[2]; _subs = items[3]; _quit = items[4];
        _modes = new NativeMenuItem { Menu = new NativeMenu() };
        _conn = new NativeMenuItem { Menu = new NativeMenu() };
        _status = new NativeMenuItem { IsEnabled = false };
        _servers = new NativeMenuItem { Header = L.S("servers"), Menu = new NativeMenu() };
        _menu.Items.Insert(0, _status);
        _menu.Items.Insert(1, new NativeMenuItemSeparator());
        _menu.Items.Insert(4, _servers);
        _menu.Items.Insert(5, _modes);
        _menu.Items.Insert(6, _conn);
        SatraCore.I.Changed += Refresh;
        SatraCore.I.Profiles.ProfileItems.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Re-label after a language switch.</summary>
    public static void Relocalize() { _sig = ""; Refresh(); }

    private static void Refresh() => Dispatcher.UIThread.Post(() =>
    {
        if (_status == null || _toggle == null || _servers?.Menu == null || _icon == null) return;
        var c = SatraCore.I;
        var act = c.Active;
        var list = c.Profiles.ProfileItems.Take(15).ToList();
        var sig = $"{L.Lang}|{SatraState.I.Mode}|{SatraState.I.Profile}|{c.Phase}|{act?.IndexId}|{act?.Remarks}|{string.Join(',', list.Select(p => p.IndexId + p.Remarks))}";
        if (sig == _sig) return;
        _sig = sig;
        // every label follows the current app language (they used to be set once at start-up)
        if (_show != null) _show.Header = L.S("tray_show");
        if (_add != null) _add.Header = L.S("add_clip");
        if (_subs != null) _subs.Header = L.S("tray_subs");
        if (_quit != null) _quit.Header = L.S("tray_quit");
        _servers.Header = L.S("servers");
        var tun = SatraState.I.Mode == ConnModes.Tun;
        var prof = ServiceLib.Satra.ProfileModes.Normalize(SatraState.I.Profile);
        if (_modes?.Menu != null)
        {
            _modes.Header = $"{L.S("mode_title")}: {L.S("mode_" + prof)}";
            _modes.Menu.Items.Clear();
            foreach (var id in ServiceLib.Satra.ProfileModes.All)
            {
                var ok = ServiceLib.Satra.ProfileModes.Available(id, tun);
                var mi = new NativeMenuItem { Header = L.S("mode_" + id) + (ok ? "" : $"  ({L.S("gate_tun")})"), ToggleType = MenuItemToggleType.Radio, IsChecked = id == prof, IsEnabled = ok };
                var mid = id;
                mi.Click += async (_, _) => { try { await c.SetProfileAsync(mid); } catch (Exception ex) { Logging.SaveLog("tray", ex); } };
                _modes.Menu.Items.Add(mi);
            }
        }
        if (_conn?.Menu != null)
        {
            _conn.Header = $"{L.S("route_title")}: {L.S(ConnModes.Label(SatraState.I.Mode))}";
            _conn.Menu.Items.Clear();
            foreach (var m in ConnModes.All)
            {
                var mi = new NativeMenuItem { Header = L.S(ConnModes.Label(m)), ToggleType = MenuItemToggleType.Radio, IsChecked = m == SatraState.I.Mode };
                var mm = m;
                mi.Click += async (_, _) => { try { await c.SetModeAsync(mm); } catch (Exception ex) { Logging.SaveLog("tray", ex); } };
                _conn.Menu.Items.Add(mi);
            }
        }
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
