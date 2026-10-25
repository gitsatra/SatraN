namespace ServiceLib.Satra;

/// <summary>
/// Satra v1.9 usage keys inside one day's bucket (Dictionary&lt;string,long&gt;):
///  a:app / d:site totals (legacy), p:app up, q:app down, h:app\tsite total, j:app\tsite up,
///  u:cfg up, w:cfg down. Pure helpers so the logic is unit-tested.
/// </summary>
public static class UsageModel
{
    public const char Sep = '\t';
    public static string HostKey(string app, string site) => "h:" + app + Sep + site;
    public static string HostUpKey(string app, string site) => "j:" + app + Sep + site;

    public static void AddApp(Dictionary<string, long> day, string app, string site, long up, long down)
    {
        var t = up + down;
        if (t <= 0) return;
        if (app.Length > 0)
        {
            day["a:" + app] = day.GetValueOrDefault("a:" + app) + t;
            if (up > 0) day["p:" + app] = day.GetValueOrDefault("p:" + app) + up;
            if (down > 0) day["q:" + app] = day.GetValueOrDefault("q:" + app) + down;
            if (site.Length > 0)
            {
                day[HostKey(app, site)] = day.GetValueOrDefault(HostKey(app, site)) + t;
                if (up > 0) day[HostUpKey(app, site)] = day.GetValueOrDefault(HostUpKey(app, site)) + up;
            }
        }
        if (site.Length > 0) day["d:" + site] = day.GetValueOrDefault("d:" + site) + t;
    }

    public record AppTotals(long Up, long Down) { public long Total => Up + Down; }
    public record HostRow(string Host, long Total, long Up) { public long Down => Math.Max(0, Total - Up); }

    /// <summary>Up/down for one app over the given day buckets (apps recorded before v1.9 only have a total → counted as down).</summary>
    public static AppTotals App(IEnumerable<Dictionary<string, long>> days, string app)
    {
        long up = 0, down = 0, tot = 0;
        foreach (var d in days)
        {
            up += d.GetValueOrDefault("p:" + app);
            down += d.GetValueOrDefault("q:" + app);
            tot += d.GetValueOrDefault("a:" + app);
        }
        if (up + down < tot) down += tot - up - down;
        return new AppTotals(up, down);
    }

    public static List<HostRow> Hosts(IEnumerable<Dictionary<string, long>> days, string app, int take = 50)
    {
        var tot = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var up = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var pre = app + Sep;
        foreach (var d in days)
            foreach (var (k, v) in d)
            {
                if (k.Length < 3 || k[1] != ':' || (k[0] != 'h' && k[0] != 'j')) continue;
                if (!k.AsSpan(2).StartsWith(pre, StringComparison.OrdinalIgnoreCase)) continue;
                var host = k[(2 + pre.Length)..];
                var m = k[0] == 'h' ? tot : up;
                m[host] = m.GetValueOrDefault(host) + v;
            }
        return tot.OrderByDescending(kv => kv.Value).Take(take)
            .Select(kv => new HostRow(kv.Key, kv.Value, Math.Min(kv.Value, up.GetValueOrDefault(kv.Key)))).ToList();
    }

    /// <summary>Keeps a day bounded: at most <paramref name="max"/> entries per key family, dropping the smallest.</summary>
    public static void Prune(Dictionary<string, long> day, int max = 500)
    {
        foreach (var g in day.Keys.Where(k => k.Length > 1 && k[1] == ':').GroupBy(k => k[0]).ToList())
        {
            var cap = g.Key is 'h' or 'j' ? max * 4 : max;
            var n = g.Count();
            if (n <= cap) continue;
            foreach (var k in g.OrderBy(k => day[k]).Take(n - cap).ToList()) day.Remove(k);
        }
    }

    // ---------- Linux .desktop identity ----------
    public record DesktopEntry(string Name, string Exec, string Icon, string WmClass, string File);

    public static DesktopEntry? ParseDesktop(string text, string file = "", string? lang = null)
    {
        string name = "", locName = "", exec = "", icon = "", wm = "";
        var inMain = false;
        var noDisplayApp = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inMain = line == "[Desktop Entry]"; continue; }
            if (!inMain) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim();
            var v = line[(eq + 1)..].Trim();
            switch (k)
            {
                case "Name": name = v; break;
                case "Exec": exec = v; break;
                case "Icon": icon = v; break;
                case "StartupWMClass": wm = v; break;
                case "Type" when v != "Application": noDisplayApp = true; break;
                default:
                    if (lang != null && k == $"Name[{lang}]") locName = v;
                    break;
            }
        }
        if (noDisplayApp || exec.Length == 0) return null;
        return new DesktopEntry(locName.Length > 0 ? locName : name, exec, icon, wm, file);
    }

    /// <summary>The program a desktop Exec= line runs (skips env/flatpak wrappers and field codes), as a file name.</summary>
    public static string ExecBinary(string exec)
    {
        var toks = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('"')).ToList();
        var i = 0;
        if (i < toks.Count && Path.GetFileName(toks[i]) == "env")
        {
            i++;
            while (i < toks.Count && (toks[i].Contains('=') || toks[i].StartsWith('-'))) i++;
        }
        if (i < toks.Count && Path.GetFileName(toks[i]) is "flatpak" or "snap")
        {
            // flatpak run [--opts] org.telegram.desktop  → last dotted id segment
            var id = toks.Skip(i + 1).FirstOrDefault(t => !t.StartsWith('-') && t != "run");
            return id == null ? "" : id.Split('.').Last();
        }
        return i < toks.Count ? Path.GetFileName(toks[i]) : "";
    }

    private static string Norm(string s)
    {
        s = Path.GetFileName(s).ToLowerInvariant();
        if (s.EndsWith(".exe")) s = s[..^4];
        return s;
    }

    /// <summary>Best .desktop entry for a running process name/path: exact exec binary, then WM class, then a name prefix (e.g. Telegram ↔ telegram-desktop).</summary>
    public static DesktopEntry? Match(string process, IEnumerable<DesktopEntry> entries)
    {
        var p = Norm(process);
        if (p.Length == 0) return null;
        var list = entries as IList<DesktopEntry> ?? entries.ToList();
        return list.FirstOrDefault(e => Norm(ExecBinary(e.Exec)) == p)
            ?? list.FirstOrDefault(e => e.WmClass.Length > 0 && Norm(e.WmClass) == p)
            ?? list.FirstOrDefault(e => Norm(ExecBinary(e.Exec)) is { Length: >= 4 } b && (b.StartsWith(p + "-") || p.StartsWith(b + "-")))
            // chrome ↔ google-chrome-stable / Google-chrome (a whole dash-separated word, ≥4 chars)
            ?? (p.Length >= 4 ? list.FirstOrDefault(e => Norm(ExecBinary(e.Exec)).Split('-').Contains(p) || Norm(e.WmClass).Split('-').Contains(p)) : null);
    }

    /// <summary>Friendly fallback name from an executable: "telegram-desktop" → "Telegram Desktop", "Code.exe" → "Code".</summary>
    public static string Pretty(string process)
    {
        var s = Path.GetFileName(process);
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        var parts = s.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? s : string.Join(' ', parts.Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    }
}
