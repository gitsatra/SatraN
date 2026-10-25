#if SATRA_TESTCTL
// Test-only remote control (compiled only with -p:DefineConstants=SATRA_TESTCTL; never in releases).
namespace v2rayN.Desktop.Satra.Services;

public static class TestCtl
{
    public static void Install(SatraCore c)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SatraN");
        Directory.CreateDirectory(dir);
        var inF = Path.Combine(dir, "ctl.in"); var outF = Path.Combine(dir, "ctl.out");
        DispatcherTimer.Run(() =>
        {
            try
            {
                if (File.Exists(inF))
                {
                    var cmd = File.ReadAllText(inF).Trim(); File.Delete(inF);
                    var a = cmd.Split(' ', 2);
                    switch (a[0])
                    {
                        case "connect": _ = c.ConnectAsync(); break;
                        case "disconnect": _ = c.DisconnectAsync(); break;
                        case "use": _ = c.UseAsync(c.Profiles.ProfileItems[int.Parse(a[1])]); break;
                        case "add": _ = c.AddFromClipboardAsync(File.ReadAllText(a[1])); break;
                        case "del": _ = Task.Run(async () => { var pi = await AppManager.Instance.GetProfileItem(c.Profiles.ProfileItems[int.Parse(a[1])].IndexId); if (pi != null) await ServiceLib.Handler.ConfigHandler.RemoveServers(AppManager.Instance.Config, new() { pi }); }); break;
                        case "smart": SatraState.I.SmartConnect = a[1] == "on"; SatraState.Save(); break;
                        case "mode": SatraState.I.Mode = a[1]; SatraState.Save(); break;
                    }
                }
                var cm = ServiceLib.Manager.CoreManager.Instance;
                File.WriteAllText(outF, $"{DateTime.Now:HH:mm:ss} phase={c.Phase} active={c.Profiles.ProfileItems.IndexOf(c.Active!)} n={c.Profiles.ProfileItems.Count} tun={AppManager.Instance.Config.TunModeItem.EnableTun} core={cm.CoreRunning} tunrun={cm.TunRunning} mode={SatraState.I.Mode}\n");
            }
            catch (Exception e) { try { File.AppendAllText(outF, "ERR " + e.Message + "\n"); } catch { } }
            return true;
        }, TimeSpan.FromMilliseconds(500));
    }
}
#endif
