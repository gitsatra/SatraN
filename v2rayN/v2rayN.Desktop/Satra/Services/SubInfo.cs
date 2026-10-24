using System.Net.Http;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Reads the standard profile headers (Subscription-Userinfo, Profile-Title, Support-Url, Announce,
/// Sub-Expire-Button-Link, X-Satra-Hub …) that 3x-ui/Satra Hub/Marzban and others send.</summary>
public static class SubInfo
{
    public static string Decode(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        if (v.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(v[7..])); } catch { return ""; }
        }
        return v;
    }

    /// <summary>Panels may send expire as unix seconds or milliseconds (same rule as SatraNG Android).</summary>
    public static long ExpireUnixSeconds(long expire) =>
        expire > 10_000_000_000L ? expire / 1000L : expire;

    public static async Task<SubMeta?> RefreshAsync(SubItem sub)
    {
        if (string.IsNullOrWhiteSpace(sub.Url) || !sub.Url.StartsWith("http")) return null;
        var url = sub.Url;
        using var res = await Net.SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            Identity.AddTo(req);
            return req;
        });
        var st0 = SatraState.I;
        if (!st0.Subs.TryGetValue(sub.Id, out var m0)) st0.Subs[sub.Id] = m0 = new SubMeta { FirstSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        if (res == null || !res.IsSuccessStatusCode)
        {
            m0.Error = res != null ? HttpReason((int)res.StatusCode) : await NetReasonAsync(url);
            m0.ErrorAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SatraState.Save();
            return null;
        }
        m0.Error = null;
        string? H(string k) => res.Headers.TryGetValues(k, out var v) ? v.FirstOrDefault() : res.Content.Headers.TryGetValues(k, out var c) ? c.FirstOrDefault() : null;

        var st = SatraState.I;
        if (!st.Subs.TryGetValue(sub.Id, out var m))
        {
            st.Subs[sub.Id] = m = new SubMeta { FirstSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        }
        var info = H("Subscription-Userinfo");
        if (!string.IsNullOrEmpty(info))
        {
            foreach (var part in info.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2 || !long.TryParse(kv[1].Trim(), out var n)) continue;
                switch (kv[0].Trim().ToLowerInvariant())
                {
                    case "upload": m.Up = n; break;
                    case "download": m.Down = n; break;
                    case "total": m.Total = n; break;
                    case "expire": m.Expire = ExpireUnixSeconds(n); break;
                }
            }
        }
        m.Title = Decode(H("Profile-Title")) is { Length: > 0 } t ? t : m.Title;
        m.Announce = Decode(H("Announce"));
        m.SupportUrl = H("Support-Url");
        m.WebPage = H("Profile-Web-Page-Url");
        m.RenewUrl = H("X-Satra-Renew-Url") ?? H("Sub-Expire-Button-Link");
        m.HubBase = H("X-Satra-Hub");
        m.Fetched = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SatraState.Save();
        return m;
    }

    public static string HttpReason(int code) => code switch
    {
        401 or 403 => L.F("sub_err_403", code),
        404 or 410 => L.F("sub_err_404", code),
        429 => L.S("sub_err_429"),
        >= 500 => L.F("sub_err_5xx", code),
        _ => L.F("sub_err_http", code),
    };

    /// <summary>Distinguishes DNS failure vs. unreachable/timeout when no HTTP answer came back.</summary>
    public static async Task<string> NetReasonAsync(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var ips = await System.Net.Dns.GetHostAddressesAsync(host, cts.Token);
            if (ips.Length == 0) return L.S("sub_err_dns");
        }
        catch (UriFormatException) { return L.S("sub_err_url"); }
        catch { return L.S("sub_err_dns"); }
        return L.S("sub_err_timeout");
    }

    /// <summary>Fraction used (traffic or time). Data line emphasizes remaining like Android.</summary>
    public static (double frac, string dataText, string timeText) Describe(SubMeta m)
    {
        var used = Math.Max(0, m.Up + m.Down);
        double fData = m.Total > 0 ? Math.Clamp((double)used / m.Total, 0, 1) : 0;
        double fTime = 0;
        string timeText;
        if (m.Expire > 0)
        {
            var exp = ExpireUnixSeconds(m.Expire);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var left = exp - now;
            var first = m.FirstSeen > 0 ? Math.Min(m.FirstSeen, now) : now - 30L * 86400;
            var span = Math.Max(1, exp - first);
            fTime = Math.Clamp(1 - (double)Math.Max(0, left) / span, 0, 1);
            timeText = left <= 0 ? L.S("expired") : L.F("days_left", Math.Ceiling(left / 86400.0));
        }
        else timeText = L.S("no_expiry");

        string dataText;
        if (m.Total > 0)
        {
            var leftBytes = Math.Max(0, m.Total - used);
            // Same wording as SatraNG Android: "{left} left of {total}"
            dataText = L.F("quota_left", Ui.Bytes(leftBytes), Ui.Bytes(m.Total));
        }
        else if (used > 0) dataText = $"{Ui.Bytes(used)} {L.S("used")} · {L.S("unlimited")}";
        else dataText = L.S("unlimited");

        return (Math.Max(fData, fTime), dataText, timeText);
    }
}
