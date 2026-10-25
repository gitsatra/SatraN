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
            case "mode": SatraState.I.Mode = arg; SatraState.Save(); SatraShell.I.Go("home"); break;
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
