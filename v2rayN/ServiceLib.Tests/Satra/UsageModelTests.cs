using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class UsageModelTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }

    [Test]
    public void App_hosts_and_up_down_accumulate()
    {
        var d = new Dictionary<string, long>();
        UsageModel.AddApp(d, "Telegram", "telegram.org", 100, 900);
        UsageModel.AddApp(d, "Telegram", "telegram.org", 0, 1000);
        UsageModel.AddApp(d, "Telegram", "cdn.t.me", 10, 0);
        UsageModel.AddApp(d, "", "x.com", 5, 5);
        var t = UsageModel.App([d], "Telegram");
        Ok(t.Up == 110 && t.Down == 1900, $"{t}");
        var h = UsageModel.Hosts([d], "Telegram");
        Ok(h.Count == 2 && h[0].Host == "telegram.org" && h[0].Total == 2000 && h[0].Up == 100 && h[0].Down == 1900);
        Ok(d["d:x.com"] == 10 && !d.ContainsKey("a:"));
        Ok(UsageModel.Hosts([d], "Tele").Count == 0, "prefix of another app must not match");
    }

    [Test]
    public void Legacy_totals_count_as_down()
    {
        var d = new Dictionary<string, long> { ["a:Old"] = 500 };
        var t = UsageModel.App([d], "Old");
        Ok(t.Up == 0 && t.Down == 500);
    }

    [Test]
    public void Prune_is_per_family()
    {
        var d = new Dictionary<string, long>();
        for (var i = 0; i < 20; i++) { d["a:" + i] = i; d["u:" + i] = i; }
        UsageModel.Prune(d, 10);
        Ok(d.Keys.Count(k => k[0] == 'a') == 10 && d.ContainsKey("a:19") && !d.ContainsKey("a:0"));
        Ok(d.Keys.Count(k => k[0] == 'u') == 10);
    }

    [Test]
    public void Desktop_entries_parse_and_match()
    {
        var tg = UsageModel.ParseDesktop("[Desktop Entry]\nType=Application\nName=Telegram Desktop\nName[fa]=تلگرام\nExec=env BAMF=1 /usr/bin/telegram-desktop -- %u\nIcon=telegram\nStartupWMClass=TelegramDesktop\n[Desktop Action x]\nName=Other\n", "a", "fa")!;
        Ok(tg.Name == "تلگرام" && tg.Icon == "telegram", tg.ToString());
        Ok(UsageModel.ExecBinary(tg.Exec) == "telegram-desktop");
        var ff = UsageModel.ParseDesktop("[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox %u\nIcon=firefox\n")!;
        var fp = UsageModel.ParseDesktop("[Desktop Entry]\nType=Application\nName=Discord\nExec=/usr/bin/flatpak run --branch=stable com.discordapp.Discord\nIcon=com.discordapp.Discord\n")!;
        Ok(UsageModel.ParseDesktop("[Desktop Entry]\nType=Link\nName=x\nExec=y\n") == null);
        var gc = UsageModel.ParseDesktop("[Desktop Entry]\nType=Application\nName=Google Chrome\nExec=/usr/bin/google-chrome-stable %U\nIcon=google-chrome\n")!;
        var all = new[] { tg, ff, fp, gc };
        Ok(UsageModel.Match("chrome", all) == gc, "chrome");
        Ok(UsageModel.Match("firefox", all) == ff);
        Ok(UsageModel.Match("/usr/lib/firefox/firefox", all) == ff);
        Ok(UsageModel.Match("TelegramDesktop", all) == tg);
        Ok(UsageModel.Match("telegram-desktop", all) == tg);
        Ok(UsageModel.Match("Discord", all) == fp);
        Ok(UsageModel.Match("curl", all) == null);
        Ok(UsageModel.Pretty("telegram-desktop") == "Telegram Desktop" && UsageModel.Pretty("Code.exe") == "Code");
    }
}
