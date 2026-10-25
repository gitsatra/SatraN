using System.Diagnostics;
using System.IO.Compression;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>v2rayN parity: backup/restore everything to one file, scan a QR code from the screen.</summary>
public static class Parity
{
    // ---------- backup / restore (same zip layout as v2rayN: guiConfigs/…, so v2rayN backups restore too) ----------
    public static async Task<string?> BackupAsync(string file)
    {
        try
        {
            await ConfigHandler.SaveConfig(AppManager.Instance.Config);
            SatraState.Save();
            var dir = Utils.GetConfigPath();
            await Task.Run(() =>
            {
                if (File.Exists(file)) File.Delete(file);
                using var z = ZipFile.Open(file, ZipArchiveMode.Create);
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(dir, f);
                    if (rel.StartsWith("configTest", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lock") || rel.Contains(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    // SQLite may hold the db open: copy through a shared read stream
                    using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var dst = z.CreateEntry(Path.Combine("guiConfigs", rel).Replace('\\', '/'), CompressionLevel.Optimal).Open();
                    src.CopyTo(dst);
                }
            });
            return null;
        }
        catch (Exception ex) { Logging.SaveLog("satra backup", ex); return ex.Message; }
    }

    /// <summary>Validates the zip, keeps an automatic backup of the current state, extracts, restarts the app.</summary>
    public static async Task<string?> RestoreAsync(string file)
    {
        try
        {
            using (var z = ZipFile.OpenRead(file))
                if (!z.Entries.Any(e => e.FullName.StartsWith("guiConfigs/") && e.Name.EndsWith(".json")))
                    return "invalid";
            var auto = Utils.GetBackupPath($"satran-before-restore-{DateTime.Now:yyyyMMddHHmmss}.zip");
            var err = await BackupAsync(auto);
            if (err != null) return err;
            await SatraCore.I.DisconnectAsync();
            await AppManager.Instance.AppExitAsync(false);
            await ServiceLib.Helper.SQLiteHelper.Instance.DisposeDbConnectionAsync();
            var to = Utils.GetConfigPath();
            using (var z = ZipFile.OpenRead(file))
                foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("guiConfigs/") && e.Name.Length > 0))
                {
                    var dest = Path.GetFullPath(Path.Combine(to, e.FullName["guiConfigs/".Length..]));
                    if (!dest.StartsWith(Path.GetFullPath(to))) continue; // zip-slip guard
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest, true);
                }
            Relaunch();
            return null;
        }
        catch (Exception ex) { Logging.SaveLog("satra restore", ex); return ex.Message; }
    }

    public static void Relaunch()
    {
        var exe = Environment.ProcessPath!;
        if (Utils.IsWindows())
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 >nul & start \"\" \"{exe}\"") { CreateNoWindow = true, UseShellExecute = false });
        else
            Process.Start(new ProcessStartInfo("/bin/sh", ["-c", $"sleep 2; exec \"{exe}\" >/dev/null 2>&1 &"]) { UseShellExecute = false });
        AppManager.Instance.Shutdown(true);
    }

    // ---------- scan QR from screen ----------
    /// <summary>Captures the screen (our window hidden), returns the decoded QR text or null.</summary>
    public static async Task<string?> ScanScreenAsync()
    {
        var w = SatraShell.I.Window;
        var png = Path.Combine(Path.GetTempPath(), $"satran-scan-{Environment.ProcessId}.png");
        var prev = w.WindowState;
        try
        {
            w.WindowState = WindowState.Minimized;
            await Task.Delay(450);
            var ok = await CaptureAsync(png);
            return ok ? await Task.Run(() => QRCodeUtils.ParseBarcode(png)) : null;
        }
        finally
        {
            w.WindowState = prev == WindowState.Minimized ? WindowState.Normal : prev;
            w.Activate();
            try { File.Delete(png); } catch { }
        }
    }

    private static async Task<bool> CaptureAsync(string png)
    {
        if (OperatingSystem.IsWindows())
            return await Task.Run(() =>
            {
                // full virtual screen (all monitors) via GDI
                int x = GetSystemMetrics(76), y = GetSystemMetrics(77), cw = GetSystemMetrics(78), ch = GetSystemMetrics(79);
                using var bmp = new System.Drawing.Bitmap(cw, ch);
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, bmp.Size);
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                return File.Exists(png);
            });
        if (OperatingSystem.IsMacOS()) return await Run("screencapture", ["-x", png]) && File.Exists(png);
        // Linux: XDG portal first (works on GNOME/KDE Wayland; may ask once), then common tools.
        if (await PortalAsync(png)) return true;
        string[][] tools =
        [
            ["gnome-screenshot", "-f", png], ["spectacle", "-b", "-n", "-f", "-o", png], ["grim", png],
            ["import", "-window", "root", png], ["scrot", "-o", png],
        ];
        foreach (var t in tools)
            if (await Run(t[0], t[1..]) && File.Exists(png) && new FileInfo(png).Length > 0) return true;
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);

    private static async Task<bool> Run(string exe, string[] args, int timeoutMs = 15000)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p == null) return false;
            using var cts = new CancellationTokenSource(timeoutMs);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>org.freedesktop.portal.Screenshot via gdbus: listen for the Response signal, copy the returned file.</summary>
    private static async Task<bool> PortalAsync(string png)
    {
        Process? mon = null;
        try
        {
            var token = "satran" + Environment.TickCount64;
            // gdbus prints through stdio: piped, it is block-buffered and the Response line never arrives → force line buffering
            var stdbuf = File.Exists("/usr/bin/stdbuf");
            string[] monArgs = ["monitor", "--session", "--dest", "org.freedesktop.portal.Desktop"];
            mon = Process.Start(new ProcessStartInfo(stdbuf ? "stdbuf" : "gdbus", stdbuf ? ["-oL", "gdbus", .. monArgs] : monArgs) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (mon == null) return false;
            var started = DateTime.Now.AddSeconds(-1);
            await Task.Delay(300);
            if (!await Run("gdbus", ["call", "--session", "--dest", "org.freedesktop.portal.Desktop", "--object-path", "/org/freedesktop/portal/desktop",
                    "--method", "org.freedesktop.portal.Screenshot.Screenshot", "", $"{{'handle_token': <'{token}'>, 'interactive': <false>}}"])) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // the first time GNOME may ask the user to allow
            var read = mon.StandardOutput.ReadLineAsync(cts.Token).AsTask();
            while (!cts.IsCancellationRequested)
            {
                var done = await Task.WhenAny(read, Task.Delay(500, CancellationToken.None));
                string? src = null;
                if (done == read)
                {
                    var line = await read;
                    if (line == null) break;
                    read = mon.StandardOutput.ReadLineAsync(cts.Token).AsTask();
                    if (!line.Contains(token) || !line.Contains("Response")) continue;
                    var i = line.IndexOf("file://", StringComparison.Ordinal);
                    if (i < 0) return false; // denied / cancelled
                    src = Uri.UnescapeDataString(new Uri(line[i..line.IndexOf('\'', i)]).LocalPath);
                }
                else src = NewShot(started); // safety net if the signal line is still not seen
                if (src == null) continue;
                await Task.Delay(200);
                File.Copy(src, png, true);
                try { File.Delete(src); } catch { } // don't leave the capture in ~/Pictures
                return true;
            }
        }
        catch { }
        finally { try { mon?.Kill(); } catch { } mon?.Dispose(); }
        return false;
    }

    /// <summary>A screenshot file the portal just saved (GNOME: ~/Pictures[/Screenshots]/Screenshot*.png).</summary>
    private static string? NewShot(DateTime since)
    {
        var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pics)) pics = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        foreach (var d in new[] { pics, Path.Combine(pics, "Screenshots") })
        {
            if (!Directory.Exists(d)) continue;
            var f = new DirectoryInfo(d).GetFiles("Screenshot*.png").Where(x => x.LastWriteTime >= since && x.Length > 0)
                .OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
            if (f != null && DateTime.Now - f.LastWriteTime > TimeSpan.FromMilliseconds(400)) return f.FullName;
        }
        return null;
    }
}
