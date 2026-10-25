using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ServiceLib.Satra;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Friendly name + icon for a process seen in the core's connection table.
/// Windows: FileDescription/ProductName + the exe's own icon (ExtractIconEx → 32-bit DIB).
/// Linux: matching .desktop entry (Exec / StartupWMClass) → Name[lang] + themed icon file.
/// Everything is cached and resolved off the UI thread; failures fall back to a letter badge.
/// </summary>
public static class AppIdentity
{
    public record Info(string Name, Bitmap? Icon);
    private static readonly Dictionary<string, Info> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static List<UsageModel.DesktopEntry>? _desktop;
    private static string? _desktopLang;

    /// <summary>Last executable path per process name (persisted so history rows keep their icon after restart).</summary>
    public static void Remember(string name, string? path)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return;
        lock (SatraState.I.AppPaths)
            if (!SatraState.I.AppPaths.TryGetValue(name, out var p) || p != path)
            {
                if (SatraState.I.AppPaths.Count > 400) SatraState.I.AppPaths.Clear();
                SatraState.I.AppPaths[name] = path!;
            }
    }

    public static Info? Cached(string name) { lock (Cache) return Cache.GetValueOrDefault(name + "|" + L.Lang); }

    public static Task<Info> GetAsync(string name) => Task.Run(() =>
    {
        var key = name + "|" + L.Lang;
        lock (Cache) if (Cache.TryGetValue(key, out var c)) return c;
        Info info;
        try { info = Resolve(name); }
        catch (Exception ex) { Logging.SaveLog("satra appid", ex); info = new Info(UsageModel.Pretty(name), null); }
        lock (Cache) Cache[key] = info;
        return info;
    });

    private static Info Resolve(string name)
    {
        string? path;
        lock (SatraState.I.AppPaths) path = SatraState.I.AppPaths.GetValueOrDefault(name);
        if (OperatingSystem.IsWindows())
        {
            var nm = UsageModel.Pretty(name);
            Bitmap? icon = null;
            if (path != null && File.Exists(path))
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                var d = (vi.FileDescription ?? "").Trim();
                if (d.Length == 0 || d.Length > 40) d = (vi.ProductName ?? "").Trim();
                if (d.Length is > 0 and <= 40) nm = d;
                icon = WinIcon(path);
            }
            return new Info(nm, icon);
        }
        if (OperatingSystem.IsLinux())
        {
            var e = UsageModel.Match(path ?? name, Desktop()) ?? (path != null ? UsageModel.Match(name, Desktop()) : null);
            if (e != null) return new Info(e.Name.Length > 0 ? e.Name : UsageModel.Pretty(name), LinuxIcon(e.Icon));
        }
        return new Info(UsageModel.Pretty(name), null);
    }

    // ---------------- Linux ----------------
    private static List<UsageModel.DesktopEntry> Desktop()
    {
        if (_desktop != null && _desktopLang == L.Lang) return _desktop;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirs = new[]
        {
            Path.Combine(home, ".local/share/applications"), "/usr/share/applications", "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications", Path.Combine(home, ".local/share/flatpak/exports/share/applications"),
            "/var/lib/snapd/desktop/applications",
        };
        var list = new List<UsageModel.DesktopEntry>();
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(d, "*.desktop", SearchOption.AllDirectories).Take(2000))
                try { if (UsageModel.ParseDesktop(File.ReadAllText(f), f, L.Lang) is { } e) list.Add(e); } catch { }
        _desktopLang = L.Lang;
        return _desktop = list;
    }

    private static Bitmap? LinuxIcon(string icon)
    {
        if (icon.Length == 0) return null;
        string? file = null;
        if (Path.IsPathRooted(icon)) file = File.Exists(icon) ? icon : null;
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[] { Path.Combine(home, ".local/share/icons"), "/usr/share/icons", "/var/lib/flatpak/exports/share/icons", "/var/lib/snapd/desktop/icons" };
            foreach (var theme in new[] { "hicolor", "Yaru", "Adwaita", "breeze" })
                foreach (var size in new[] { "64x64", "48x48", "128x128", "96x96", "256x256", "32x32" })
                    foreach (var r in roots)
                    {
                        var p = Path.Combine(r, theme, size, "apps", icon + ".png");
                        if (File.Exists(p)) { file = p; goto found; }
                    }
            foreach (var p in new[] { $"/usr/share/pixmaps/{icon}.png", $"/var/lib/snapd/desktop/icons/{icon}.png" })
                if (File.Exists(p)) { file = p; break; }
            found:;
        }
        if (file == null || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
        using var s = File.OpenRead(file);
        return Bitmap.DecodeToWidth(s, 64);
    }

    // ---------------- Windows ----------------
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr h, out ICONINFO info);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public int x, y; public IntPtr hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    private static Bitmap? WinIcon(string path)
    {
        var large = new IntPtr[1];
        if (ExtractIconEx(path, 0, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;
        var h = large[0];
        try
        {
            if (!GetIconInfo(h, out var ii)) return null;
            try
            {
                if (ii.hbmColor == IntPtr.Zero || GetObject(ii.hbmColor, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
                int w = bm.bmWidth, hh = bm.bmHeight;
                var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -hh, biPlanes = 1, biBitCount = 32 };
                var px = new byte[w * hh * 4];
                var dc = GetDC(IntPtr.Zero);
                try { if (GetDIBits(dc, ii.hbmColor, 0, (uint)hh, px, ref bi, 0) == 0) return null; }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                var anyAlpha = false;
                for (var i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (var i = 3; i < px.Length; i += 4) px[i] = 255;
                // premultiply for Avalonia's Premul format
                for (var i = 0; i < px.Length; i += 4)
                {
                    var a = px[i + 3];
                    if (a == 255) continue;
                    px[i] = (byte)(px[i] * a / 255); px[i + 1] = (byte)(px[i + 1] * a / 255); px[i + 2] = (byte)(px[i + 2] * a / 255);
                }
                var wb = new WriteableBitmap(new PixelSize(w, hh), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var fb = wb.Lock())
                    for (var y = 0; y < hh; y++) Marshal.Copy(px, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
                return wb;
            }
            finally { if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor); if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask); }
        }
        finally { DestroyIcon(h); }
    }
}
