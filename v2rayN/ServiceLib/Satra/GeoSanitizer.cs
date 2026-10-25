using System.Text.RegularExpressions;

namespace ServiceLib.Satra;

/// <summary>
/// Satra: an Xray config that names a geosite/geoip code missing from the installed .dat files makes Xray exit
/// at start ("failed to check code IR from geosite.dat"), so nothing connects in any mode. Routing templates from
/// other rule sources (e.g. Chocolate4U "geosite:ir") hit this after the geo files are replaced. Before writing an
/// Xray config, map a missing code to its "category-" twin when present, else drop the entry (logged).
/// </summary>
public static class GeoSanitizer
{
    private static readonly Dictionary<string, (DateTime mtime, HashSet<string> codes)> _cache = new();
    private static readonly Regex _ref = new("\"(geosite|geoip):(!?)([A-Za-z0-9_.!@:-]+?)(@[A-Za-z0-9_!-]+)?\"", RegexOptions.Compiled);

    public static HashSet<string>? Codes(string datPath)
    {
        try
        {
            if (!File.Exists(datPath)) return null;
            var mt = File.GetLastWriteTimeUtc(datPath);
            lock (_cache)
                if (_cache.TryGetValue(datPath, out var c) && c.mtime == mt) return c.codes;
            var set = ReadCodes(File.ReadAllBytes(datPath));
            lock (_cache) _cache[datPath] = (mt, set);
            return set;
        }
        catch (Exception ex) { Logging.SaveLog("GeoSanitizer read " + datPath, ex); return null; }
    }

    /// <summary>Top-level entries of a GeoSiteList/GeoIPList protobuf: field 1 (len) each, whose own field 1 is the code.</summary>
    public static HashSet<string> ReadCodes(byte[] d)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < d.Length)
        {
            var tag = d[i++];
            var len = (int)Varint(d, ref i);
            if (tag != 0x0a) { if ((tag & 7) == 2) { i += len; continue; } break; }
            var end = i + len;
            if (end > d.Length) break;
            if (d[i] == 0x0a)
            {
                var j = i + 1;
                var cl = (int)Varint(d, ref j);
                if (j + cl <= end) set.Add(System.Text.Encoding.UTF8.GetString(d, j, cl));
            }
            i = end;
        }
        return set;
    }

    private static ulong Varint(byte[] d, ref int i)
    {
        ulong v = 0; var s = 0;
        while (i < d.Length) { var b = d[i++]; v |= (ulong)(b & 0x7f) << s; s += 7; if (b < 0x80) break; }
        return v;
    }

    /// <summary>Rewrites JSON text; <paramref name="site"/>/<paramref name="ip"/> null = file unknown, leave as is.</summary>
    public static string Sanitize(string json, HashSet<string>? site, HashSet<string>? ip, List<string>? changes = null)
    {
        if (site == null && ip == null) return json;
        var drop = false;
        var outp = _ref.Replace(json, m =>
        {
            var set = m.Groups[1].Value == "geosite" ? site : ip;
            var code = m.Groups[3].Value;
            if (set == null || set.Contains(code)) return m.Value;
            var alt = "category-" + code;
            if (set.Contains(alt))
            {
                changes?.Add($"{m.Groups[1].Value}:{code} -> {alt}");
                return $"\"{m.Groups[1].Value}:{m.Groups[2].Value}{alt}{m.Groups[4].Value}\"";
            }
            changes?.Add($"{m.Groups[1].Value}:{code} dropped");
            drop = true;
            return "\"__satra_drop__\"";
        });
        if (!drop) return outp;
        // remove dropped entries from their arrays (", x" / "x, " / sole element)
        outp = Regex.Replace(outp, "\\s*\"__satra_drop__\"\\s*,", "");
        outp = Regex.Replace(outp, ",\\s*\"__satra_drop__\"", "");
        outp = Regex.Replace(outp, "\"__satra_drop__\"", "\"regexp:^$satra-never$\"");
        return outp;
    }

    public static string SanitizeXray(string json)
    {
        var changes = new List<string>();
        var r = Sanitize(json, Codes(Utils.GetBinPath("geosite.dat")), Codes(Utils.GetBinPath("geoip.dat")), changes);
        if (changes.Count > 0) Logging.SaveLog("GeoSanitizer: " + string.Join("; ", changes.Distinct()));
        return r;
    }
}
