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
        if (res == null || !res.IsSuccessStatusCode) return null;
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
                    case "expire": m.Expire = n; break;
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

    /// <summary>Fraction of the subscription used up: the larger of traffic used and time elapsed.</summary>
    public static (double frac, string dataText, string timeText) Describe(SubMeta m)
    {
        var used = m.Up + m.Down;
        double fData = m.Total > 0 ? (double)used / m.Total : 0;
        double fTime = 0;
        string timeText;
        if (m.Expire > 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var left = m.Expire - now;
            var span = Math.Max(1, m.Expire - Math.Min(m.FirstSeen, now));
            fTime = 1 - (double)Math.Max(0, left) / span;
            timeText = left <= 0 ? L.S("expired") : L.F("days_left", Math.Ceiling(left / 86400.0));
        }
        else timeText = L.S("no_expiry");
        var dataText = m.Total > 0 ? $"{Ui.Bytes(Math.Max(0, m.Total - used))} {L.S("remaining")} · {Ui.Bytes(used)} / {Ui.Bytes(m.Total)}" : $"{Ui.Bytes(used)} · {L.S("unlimited")}";
        return (Math.Max(fData, fTime), dataText, timeText);
    }
}
