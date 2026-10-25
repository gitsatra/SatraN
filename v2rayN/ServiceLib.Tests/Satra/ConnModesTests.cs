using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class ConnModesTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }

    [Test]
    public void Default_is_ports_only_and_unknown_normalizes_to_it()
    {
        Ok(ConnModes.Default == ConnModes.Ports);
        Ok(ConnModes.Normalize(null) == ConnModes.Ports);
        Ok(ConnModes.Normalize("weird") == ConnModes.Ports);
        Ok(ConnModes.Normalize("tun") == ConnModes.Tun);
    }

    [Test]
    public void Only_ports_and_system_proxy_switch_live()
    {
        Ok(ConnModes.CanSwitchLive(ConnModes.Ports, ConnModes.SystemProxy));
        Ok(ConnModes.CanSwitchLive(ConnModes.SystemProxy, ConnModes.Ports));
        Ok(!ConnModes.CanSwitchLive(ConnModes.Ports, ConnModes.Tun));
        Ok(!ConnModes.CanSwitchLive(ConnModes.Tun, ConnModes.SystemProxy));
        Ok(!ConnModes.CanSwitchLive(ConnModes.Ports, ConnModes.Ports));
    }

    [Test]
    public void Quick_picks_are_best_ping_first_without_active()
    {
        var items = new[] { ("a", 300), ("b", -1), ("c", 90), ("d", 0), ("e", 150), ("f", 120) };
        var r = ConnModes.QuickPicks(items, x => x.Item1, x => x.Item2, "f");
        Ok(string.Join(",", r.Select(x => x.Item1)) == "c,e,a", string.Join(",", r.Select(x => x.Item1)));
        var none = ConnModes.QuickPicks(new[] { ("x", 0), ("y", -1) }, x => x.Item1, x => x.Item2, null);
        Ok(string.Join(",", none.Select(x => x.Item1)) == "x,y");
    }
}
