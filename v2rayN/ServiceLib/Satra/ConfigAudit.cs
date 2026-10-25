using System.Net;

namespace ServiceLib.Satra;

public enum AuditState { Pass, Warn, Fail, Unknown }

/// <summary>Offline checks of one config's transport/encryption settings (Security page, "Encryption" section).</summary>
public static class ConfigAudit
{
    public sealed record Item(string Key, AuditState State, string Detail);

    private static readonly string[] WeakSs = ["rc4", "rc4-md5", "aes-128-cfb", "aes-192-cfb", "aes-256-cfb", "aes-128-ctr", "aes-192-ctr", "aes-256-ctr", "bf-cfb", "camellia-128-cfb", "camellia-256-cfb", "chacha20", "chacha20-ietf", "salsa20", "table", "none", "plain"];
    private static readonly int[] CommonTlsPorts = [443, 8443, 2053, 2083, 2087, 2096, 9443, 4443, 10443];
    private static readonly string[] Fps = ["chrome", "firefox", "safari", "edge", "ios", "android", "random", "randomized", "360", "qq"];

    public static bool IsTls(ProfileItem p) => (p.StreamSecurity ?? "") is "tls" or "reality";
    private static bool ProtoEncrypted(ProfileItem p) => p.ConfigType is EConfigType.Shadowsocks or EConfigType.WireGuard or EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.Anytls;

    public static List<Item> Run(ProfileItem p)
    {
        var sec = p.StreamSecurity ?? "";
        var ex = p.GetProtocolExtra();
        var net = string.IsNullOrEmpty(p.Network) ? "tcp" : p.Network;
        var list = new List<Item>();

        // certificate verification
        if (sec == "reality") list.Add(new("sc_cert", AuditState.Pass, "REALITY · public key pinned"));
        else if (sec == "tls") list.Add(p.GetAllowInsecure() ? new("sc_cert", AuditState.Fail, "allowInsecure = true") : new("sc_cert", AuditState.Pass, "verify on"));
        else if (p.ConfigType is EConfigType.Hysteria2 or EConfigType.TUIC or EConfigType.Anytls)
            list.Add(p.GetAllowInsecure() ? new("sc_cert", AuditState.Fail, "insecure = true") : new("sc_cert", AuditState.Pass, "verify on"));
        else list.Add(new("sc_cert", ProtoEncrypted(p) ? AuditState.Pass : AuditState.Warn, ProtoEncrypted(p) ? "n/a" : "no TLS"));

        // TLS fingerprint (uTLS)
        var fp = (p.Fingerprint ?? "").Trim().ToLowerInvariant();
        if (!IsTls(p)) list.Add(new("sc_fp", ProtoEncrypted(p) ? AuditState.Pass : AuditState.Warn, "n/a"));
        else if (fp.Length == 0) list.Add(new("sc_fp", sec == "reality" ? AuditState.Warn : AuditState.Warn, "Go default (detectable)"));
        else list.Add(new("sc_fp", Fps.Contains(fp) ? AuditState.Pass : AuditState.Warn, fp));

        // SNI
        if (!IsTls(p)) list.Add(new("sc_sni", ProtoEncrypted(p) ? AuditState.Pass : AuditState.Warn, "n/a"));
        else
        {
            var sni = string.IsNullOrWhiteSpace(p.Sni) ? p.Address : p.Sni;
            list.Add(IPAddress.TryParse(sni, out _) ? new("sc_sni", AuditState.Warn, "IP " + sni) : new("sc_sni", AuditState.Pass, sni ?? ""));
        }

        // cipher / protocol strength
        list.Add(Cipher(p, ex));

        // VLESS flow (vision) for REALITY / raw TCP TLS
        if (p.ConfigType == EConfigType.VLESS && sec == "reality" && net is "tcp" or "raw")
            list.Add((ex.Flow ?? "").Contains("vision") ? new("sc_flow", AuditState.Pass, ex.Flow!) : new("sc_flow", AuditState.Warn, "no flow"));
        else list.Add(new("sc_flow", AuditState.Pass, "n/a"));

        // port
        if (IsTls(p)) list.Add(CommonTlsPorts.Contains(p.Port) ? new("sc_port", AuditState.Pass, p.Port.ToString()) : new("sc_port", AuditState.Warn, p.Port.ToString()));
        else if (!ProtoEncrypted(p) && p.Port == 80) list.Add(new("sc_port", AuditState.Warn, "80 (plain HTTP)"));
        else list.Add(new("sc_port", AuditState.Pass, p.Port.ToString()));

        // ALPN
        var alpn = p.GetAlpn() ?? [];
        if (sec != "tls") list.Add(new("sc_alpn", AuditState.Pass, "n/a"));
        else list.Add(alpn.Count > 0 ? new("sc_alpn", AuditState.Pass, string.Join(",", alpn)) : new("sc_alpn", AuditState.Warn, "default"));

        // transport camouflage
        if (sec == "reality") list.Add(new("sc_transport", AuditState.Pass, "REALITY · " + net));
        else if (sec == "tls") list.Add(new("sc_transport", AuditState.Pass, "TLS · " + net));
        else if (ProtoEncrypted(p)) list.Add(new("sc_transport", AuditState.Pass, p.ConfigType.ToString()));
        else list.Add(new("sc_transport", net is "ws" or "httpupgrade" or "xhttp" or "grpc" ? AuditState.Warn : AuditState.Fail, net + " · no TLS"));
        return list;
    }

    public static Item Cipher(ProfileItem p, ProtocolExtraItem ex)
    {
        switch (p.ConfigType)
        {
            case EConfigType.VMess:
            {
                var s = (ex.VmessSecurity ?? "auto").ToLowerInvariant();
                if (s is "none" or "zero") return new("sc_cipher", IsTls(p) ? AuditState.Warn : AuditState.Fail, "vmess " + s);
                if (int.TryParse(ex.AlterId, out var aid) && aid > 0) return new("sc_cipher", AuditState.Warn, $"vmess alterId {aid} (legacy MD5)");
                return new("sc_cipher", AuditState.Pass, "vmess AEAD " + s);
            }
            case EConfigType.Shadowsocks:
            {
                var m = (ex.SsMethod ?? "").ToLowerInvariant();
                if (WeakSs.Contains(m)) return new("sc_cipher", AuditState.Fail, m);
                return new("sc_cipher", AuditState.Pass, m.Length == 0 ? "aead" : m);
            }
            case EConfigType.VLESS:
            case EConfigType.Trojan:
                return IsTls(p) ? new("sc_cipher", AuditState.Pass, (p.StreamSecurity ?? "").ToUpperInvariant()) : new("sc_cipher", AuditState.Fail, "plain " + p.ConfigType);
            case EConfigType.SOCKS:
            case EConfigType.HTTP:
                return new("sc_cipher", IsTls(p) ? AuditState.Warn : AuditState.Fail, p.ConfigType + (IsTls(p) ? " over TLS" : " plain"));
            default:
                return new("sc_cipher", AuditState.Pass, p.ConfigType.ToString());
        }
    }

    /// <summary>Parses check.torproject.org/torbulkexitlist (one IPv4/IPv6 per line).</summary>
    public static HashSet<string> ParseTorList(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && IPAddress.TryParse(l, out _)).ToHashSet();

    /// <summary>0–100 from earned/max over scored checks (max 0 = not scored, e.g. kill switch, Tor info).</summary>
    public static int Score(IEnumerable<(int max, AuditState? st)> items)
    {
        double got = 0, max = 0;
        foreach (var (m, st) in items)
        {
            if (m <= 0 || st == null || st == AuditState.Unknown) continue; // couldn't test → neither rewarded nor punished
            max += m;
            got += m * Factor(st.Value);
        }
        return max <= 0 ? 0 : (int)Math.Round(got / max * 100);
    }

    public static double Factor(AuditState s) => s switch { AuditState.Pass => 1.0, AuditState.Warn => 0.5, AuditState.Unknown => 0.6, _ => 0 };

    /// <summary>Iran's DNS-poisoning sinkhole range (10.10.34.0/24) and other bogus answers.</summary>
    public static bool IsPoisoned(IPAddress a)
    {
        var b = a.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] is 0 or 10 or 127 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] < 32);
    }
}
