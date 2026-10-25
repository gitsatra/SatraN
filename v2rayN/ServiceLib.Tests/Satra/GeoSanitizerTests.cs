using System.Text.Json.Nodes;
using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class GeoSanitizerTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }
    private static HashSet<string> S(params string[] a) => new(a, StringComparer.OrdinalIgnoreCase);

    [Test]
    public void Missing_code_maps_to_category_twin_or_is_dropped()
    {
        var j = """{"routing":{"rules":[{"domain":["geosite:ir","geosite:private","domain:x.ir"]},{"domain":["geosite:nope"],"outboundTag":"d"},{"ip":["geoip:ir","geoip:zz"]}]}}""";
        var o = JsonNode.Parse(GeoSanitizer.Sanitize(j, S("CATEGORY-IR", "PRIVATE"), S("IR")))!;
        var r = o["routing"]!["rules"]!.AsArray();
        var d0 = r[0]!["domain"]!.AsArray().Select(x => (string?)x).ToList();
        Ok(d0.SequenceEqual(["geosite:category-ir", "geosite:private", "domain:x.ir"]), string.Join(",", d0));
        Ok(r[1]!["domain"]!.AsArray().Count == 1 && !((string?)r[1]!["domain"]![0])!.StartsWith("geosite:"));
        Ok(r[2]!["ip"]!.AsArray().Select(x => (string?)x).SequenceEqual(["geoip:ir"]));
    }

    [Test]
    public void Unknown_files_leave_config_untouched()
    {
        var j = """{"a":["geosite:ir"]}""";
        Ok(GeoSanitizer.Sanitize(j, null, null) == j);
    }

    [Test]
    public void Reads_codes_from_protobuf()
    {
        // GeoSiteList { entry { country_code: "IR" } entry { country_code: "CN" } }
        byte[] e(string c) { var b = System.Text.Encoding.UTF8.GetBytes(c); return [0x0a, (byte)(b.Length + 2), 0x0a, (byte)b.Length, .. b]; }
        var codes = GeoSanitizer.ReadCodes([.. e("IR"), .. e("CN")]);
        Ok(codes.Contains("ir") && codes.Contains("cn") && codes.Count == 2);
    }
}
