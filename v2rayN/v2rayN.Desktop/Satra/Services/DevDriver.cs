using ServiceLib.Handler.Fmt;
using System.Text.Json;
using Avalonia.Media.Imaging;
using v2rayN.Desktop.Satra.Pages;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// QA automation, inert unless SATRA_DRIVER=&lt;dir&gt; is set. Reads &lt;dir&gt;/cmd (one command per line), then deletes it.
/// Commands: go &lt;page&gt; | shot &lt;name&gt; | pingall | pingone &lt;n&gt; | speed | speedsrv &lt;n&gt; | connect | disconnect | pings | mode &lt;tun|proxy|socks&gt; | adv
/// </summary>
public static class DevDriver
{
    public static void Start()
    {
        var dir = Environment.GetEnvironmentVariable("SATRA_DRIVER");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var cmd = Path.Combine(dir, "cmd");
        new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, async (_, _) =>
        {
            if (!File.Exists(cmd)) return;
            var lines = File.ReadAllLines(cmd); File.Delete(cmd);
            foreach (var l in lines)
            {
                var a = l.Trim().Split(' ', 2);
                try { await Run(dir, a[0], a.Length > 1 ? a[1] : ""); File.AppendAllText(Path.Combine(dir, "log"), $"{DateTime.Now:HH:mm:ss.fff} ok {l}\n"); }
                catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "log"), $"{DateTime.Now:HH:mm:ss.fff} ERR {l}: {ex.Message}\n"); }
            }
        }).Start();
    }

    private static ProfileItemModel Nth(string n) => SatraCore.I.Profiles.ProfileItems[int.Parse(n)];

    private static async Task Run(string dir, string c, string arg)
    {
        switch (c)
        {
            case "go": SatraShell.I.Go(arg); break;
            case "lang": AppManager.Instance.Config.UiItem.CurrentLanguage = arg; await ConfigHandler.SaveConfig(AppManager.Instance.Config); if (SatraShell.I.Parent is ContentControl lcc) { var lw = SatraShell.I.Window; lcc.Content = null; lcc.Content = new SatraShell(lw); } break;
            case "shot":
                await Task.Delay(300);
                var w = SatraShell.I.Window;
                var px = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
                using (var bmp = new RenderTargetBitmap(px)) { bmp.Render(w); bmp.Save(Path.Combine(dir, arg + ".png")); }
                break;
            case "pingall": _ = PingEngine.I.PingAllAsync(SatraCore.I.Profiles.ProfileItems.ToList()); break;
            case "pingone": _ = PingEngine.I.PingOneAsync(Nth(arg)); break;
            case "pings":
                var d = SatraCore.I.Profiles.ProfileItems.Select(p => new { p.Remarks, p.Address, p.Port, R = PingEngine.I.Get(p.IndexId).Status.ToString(), Ms = PingEngine.I.Get(p.IndexId).Ms }).ToList();
                File.WriteAllText(Path.Combine(dir, $"pings-{arg}.json"), JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
                break;
            case "speed":
                var r = await SpeedEngine.RunAsync(null, (_, _) => { }, CancellationToken.None);
                File.AppendAllText(Path.Combine(dir, "speed.txt"), $"{DateTime.Now:HH:mm:ss} via={r.Via} mbps={r.Mbps:0.0} bytes={r.Bytes} err={r.Error}\n");
                break;
            case "speedsrv":
                var r2 = await SpeedEngine.RunAsync(Nth(arg), (_, _) => { }, CancellationToken.None);
                File.AppendAllText(Path.Combine(dir, "speed.txt"), $"{DateTime.Now:HH:mm:ss} srv={Nth(arg).Remarks} via={r2.Via} mbps={r2.Mbps:0.0} bytes={r2.Bytes} err={r2.Error}\n");
                break;
            case "connect": await SatraCore.I.ConnectAsync(); break;
            case "disconnect": await SatraCore.I.DisconnectAsync(); break;
            case "homeqa": File.AppendAllText(Path.Combine(dir, "homeqa.txt"), $"{DateTime.Now:HH:mm:ss.f} {arg} {HomePage.Last?.DevFacts()}\n"); break;
            case "setmode": await SatraCore.I.SetModeAsync(arg); break;
            case "secrun": SatraShell.I.Go("security"); await Task.Delay(400); if (SecurityPage.Last != null) await SecurityPage.Last.DevRun(); break;
            case "state": File.AppendAllText(Path.Combine(dir, "state.txt"), $"{DateTime.Now:HH:mm:ss} {SatraCore.I.Phase} {SatraState.I.Mode} {SatraCore.I.Profiles.ProfileItems.FirstOrDefault(p => p.IndexId == AppManager.Instance.Config.IndexId)?.Remarks}\n"); break;
            case "mode": SatraState.I.Mode = arg; SatraState.Save(); SatraShell.I.Go("home"); break;
            case "speedui": SatraShell.I.Go("servers"); await Task.Delay(300); ServersPage.Last?.DevSpeed(int.Parse(arg)); break;
            case "size":
                var wh = arg.Split('x'); var win = SatraShell.I.Window;
                win.WindowState = WindowState.Normal; win.Width = double.Parse(wh[0]); win.Height = double.Parse(wh[1]);
                await Task.Delay(500); break;
            case "editor": SatraShell.I.Go("servers"); await Task.Delay(300); ServerEditor.Open(arg.Length > 0 ? Nth(arg) : null); break;
            case "edset": { var kv = arg.Split(' ', 2); ServerEditor.DevSet?.Invoke(kv[0], kv.Length > 1 ? kv[1] : ""); break; }
            case "edsave": ServerEditor.DevSave?.Invoke(); await Task.Delay(800); break;
            case "ctx": SatraShell.I.Go("servers"); await Task.Delay(300); ServersPage.Last?.DevCtx(int.Parse(arg)); break;
            case "delpre":
                var cfgD = AppManager.Instance.Config;
                var dels = SatraCore.I.Profiles.ProfileItems.Where(p => p.Remarks.StartsWith(arg)).ToList();
                var items = new List<ProfileItem>();
                foreach (var x in dels) { var pi = await Task.Run(() => AppManager.Instance.GetProfileItem(x.IndexId)); if (pi != null) items.Add(pi); }
                await ConfigHandler.RemoveServers(cfgD, items);
                SatraCore.I.Profiles.RefreshServers(); break;
            case "pingname": _ = PingEngine.I.PingOneAsync(SatraCore.I.Profiles.ProfileItems.First(p => p.Remarks.Contains(arg))); break;
            case "use": await SatraCore.I.UseAsync(SatraCore.I.Profiles.ProfileItems.First(p => p.Remarks.Contains(arg))); break;
            case "subadd": await SatraCore.I.AddFromClipboardAsync(arg); break;
            case "subup": await SatraCore.I.UpdateSubsAsync(); File.AppendAllText(Path.Combine(dir, "subs.txt"), $"{DateTime.Now:HH:mm:ss} via={SubFallback.LastVia}\n"); break;
            case "subs":
                foreach (var sb in await AppManager.Instance.SubItems() ?? [])
                {
                    SatraState.I.Subs.TryGetValue(sb.Id, out var st);
                    var n = SatraCore.I.Profiles.ProfileItems.Count(p => p.Subid == sb.Id);
                    File.AppendAllText(Path.Combine(dir, "subs.txt"), $"{DateTime.Now:HH:mm:ss} {sb.Remarks} {sb.Url} n={n} err={st?.Error} fetched={st?.Fetched}\n");
                }
                break;
            case "qrgen":
                var pr = await Task.Run(() => AppManager.Instance.GetProfileItem(Nth(arg).IndexId));
                var link = FmtHandler.GetShareUri(pr!)!.Replace("#", "#QR-");
                File.WriteAllBytes(Path.Combine(dir, "qr.png"), QRCodeUtils.GenQRCode(link)!); break;
            case "backup": await SettingsPage.BackupAsync(arg); break;
            case "restore": await SettingsPage.RestoreAsync(arg); break;
            case "scanscreen": File.WriteAllText(Path.Combine(dir, "scan.txt"), await Parity.ScanScreenAsync() ?? "(none)"); break;
            case "mux": await ServersPage.ToggleMux(Nth(arg)); break;
            case "qrfile": await ServersPage.ImportQrFile(arg); break;
            case "exportall": await ServersPage.ExportAll(); File.WriteAllText(Path.Combine(dir, "export.txt"), await SatraShell.I.ClipboardText() ?? ""); break;
            case "dedup": await ServersPage.Dedup(); break;
            case "dumpall": File.WriteAllLines(Path.Combine(dir, "servers.txt"), SatraCore.I.Profiles.ProfileItems.Select(p => $"{p.ConfigType} {p.Remarks} {p.Address}:{p.Port}")); break;
            case "tray": File.WriteAllLines(Path.Combine(dir, "tray.txt"), SatraTray.Dump()); break;
            case "win":
                var ww = SatraShell.I.Window;
                if (arg == "min") ww.WindowState = WindowState.Minimized;
                else if (arg == "hide") ww.Hide();
                else { ww.Show(); ww.WindowState = WindowState.Normal; ww.Activate(); }
                break;
            case "dump": File.WriteAllLines(Path.Combine(dir, "servers.txt"), SatraCore.I.Profiles.ProfileItems.Select(p => $"{p.ConfigType} {p.Remarks} {p.Address}:{p.Port} {p.Network} {p.StreamSecurity}")); break;
            case "scat": SettingsPage.DevCat = arg; SatraShell.I.Go("settings"); break;
            case "upcheck": await AutoUpdater.RunAsync(true); break;
            case "upstate": File.AppendAllText(Path.Combine(dir, "up.txt"), $"{DateTime.Now:HH:mm:ss} {AutoUpdater.State} {AutoUpdater.Found?.Version} {AutoUpdater.Progress:0.00} {AutoUpdater.Error}\n"); break;
            case "click":
                // click a visible button by its text on the current page
                var btn = SatraShell.I.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && FindText(b)?.Contains(arg) == true);
                btn?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                if (btn == null) throw new Exception("button not found");
                break;
        }
    }

    private static string? FindText(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t));
}
