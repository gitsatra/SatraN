using System.Net;
using ServiceLib.Satra;

namespace ServiceLib.Tests.Satra;

public class ConfigAuditTests
{
    private static void Ok(bool c, string m = "check failed") { if (!c) throw new Exception(m); }

    [Test]
    public void Score_ignores_unscored_and_unknown_items()
    {
        Ok(ConfigAudit.Score([(10, AuditState.Pass), (10, AuditState.Fail)]) == 50);
        Ok(ConfigAudit.Score([(10, AuditState.Pass), (0, AuditState.Fail)]) == 100, "kill switch / Tor (max 0) must not count");
        Ok(ConfigAudit.Score([(10, AuditState.Pass), (30, AuditState.Unknown)]) == 100, "unknown must not punish");
        Ok(ConfigAudit.Score([(10, AuditState.Pass), (10, null)]) == 100, "pending not counted");
        Ok(ConfigAudit.Score([(10, AuditState.Warn)]) == 50);
        Ok(ConfigAudit.Score([]) == 0);
    }

    [Test]
    public void Tor_list_parses_ips_only()
    {
        var s = ConfigAudit.ParseTorList("1.2.3.4\n# comment\n\n 5.6.7.8 \nnot-an-ip\n2001:db8::1\n");
        Ok(s.Contains("1.2.3.4") && s.Contains("5.6.7.8"), "ipv4 missing");
        Ok(!s.Contains("not-an-ip") && !s.Contains("# comment"), "junk parsed");
    }

    [Test]
    public void Poisoned_answers_are_detected()
    {
        Ok(ConfigAudit.IsPoisoned(IPAddress.Parse("10.10.34.35")));
        Ok(ConfigAudit.IsPoisoned(IPAddress.Parse("127.0.0.1")));
        Ok(ConfigAudit.IsPoisoned(IPAddress.Parse("172.20.0.1")));
        Ok(!ConfigAudit.IsPoisoned(IPAddress.Parse("142.250.1.1")));
        Ok(!ConfigAudit.IsPoisoned(IPAddress.Parse("172.32.0.1")));
    }

    [Test]
    public void Insecure_tls_fails_cert_and_reality_passes()
    {
        var bad = new ProfileItem { ConfigType = EConfigType.VLESS, StreamSecurity = "tls", Port = 443, Address = "a.example" };
        bad.AllowInsecure = Global.StringTrue;
        Ok(ConfigAudit.Run(bad).First(i => i.Key == "sc_cert").State == AuditState.Fail, "insecure not flagged");
        var good = new ProfileItem { ConfigType = EConfigType.VLESS, StreamSecurity = "reality", Port = 443, Address = "a.example" };
        Ok(ConfigAudit.Run(good).First(i => i.Key == "sc_cert").State == AuditState.Pass);
    }
}
