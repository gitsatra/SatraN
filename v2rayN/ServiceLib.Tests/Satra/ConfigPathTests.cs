using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class ConfigPathTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }

    private class Inner { public int Mtu { get; set; } = 9000; public bool? Fake { get; set; } public List<string>? Ex { get; set; } public string Name { get; set; } = "a"; }
    private class Root { public Inner? Tun { get; set; } = new(); public Inner? Missing { get; set; } public List<Inner> Ins { get; set; } = [new()]; }

    [Test]
    public void Get_set_by_path_with_types_and_ranges()
    {
        var r = new Root();
        Ok(ConfigPath.Get(r, "Tun.Mtu") == "9000");
        Ok(ConfigPath.KindOf(r, "Tun.Mtu") == ConfigPath.Kind.Number);
        Ok(ConfigPath.KindOf(r, "Tun.Fake") == ConfigPath.Kind.Bool);
        Ok(ConfigPath.KindOf(r, "Tun.Ex") == ConfigPath.Kind.List);
        Ok(ConfigPath.KindOf(r, "Tun.Nope") == ConfigPath.Kind.Unsupported);
        Ok(ConfigPath.Set(r, "Tun.Mtu", "1500", 1280, 9000) && r.Tun!.Mtu == 1500);
        Ok(!ConfigPath.Set(r, "Tun.Mtu", "100", 1280, 9000) && r.Tun.Mtu == 1500, "range");
        Ok(!ConfigPath.Set(r, "Tun.Mtu", "abc") && !ConfigPath.Set(r, "Tun.Mtu", "1.5"));
        Ok(ConfigPath.Set(r, "Tun.Fake", "true") && r.Tun.Fake == true && ConfigPath.GetBool(r, "Tun.Fake"));
        Ok(ConfigPath.Set(r, "Tun.Ex", "1.1.1.1, 10.0.0.0/8,,") && r.Tun.Ex!.Count == 2 && ConfigPath.Get(r, "Tun.Ex") == "1.1.1.1, 10.0.0.0/8");
        Ok(ConfigPath.Set(r, "Missing.Name", "x") && r.Missing!.Name == "x", "creates missing section");
        Ok(ConfigPath.Set(r, "Ins[0].Mtu", "1400") && r.Ins[0].Mtu == 1400);
        Ok(ConfigPath.Get(r, "Ins[3].Mtu") == "" && !ConfigPath.Set(r, "Ins[3].Mtu", "1"));
    }
}
