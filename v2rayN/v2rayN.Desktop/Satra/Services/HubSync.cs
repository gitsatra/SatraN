namespace v2rayN.Desktop.Satra.Services;

/// <summary>Registers this computer with Satra Hub when a subscription advertises one, and switches the
/// subscription to its per-device link (so usage and limits can be per device).</summary>
public static class HubSync
{
    public static async Task EnsureRegisteredAsync(SubItem sub)
    {
        var st = SatraState.I;
        if (!st.Subs.TryGetValue(sub.Id, out var meta) || string.IsNullOrEmpty(meta.HubBase)) return;
        if (st.Hub.TryGetValue(sub.Id, out var reg) && reg.Token.Length > 0) return;
        var parent = HubClient.SubIdFromUrl(sub.Url);
        if (parent == null) return;
        try
        {
            var r = await new HubClient(meta.HubBase).Register(parent, sub.Url);
            if (r.Token.Length == 0 || r.DeviceSubUrl.Length == 0) return;
            st.Hub[sub.Id] = r;
            sub.Url = r.DeviceSubUrl;
            await ConfigHandler.AddSubItem(AppManager.Instance.Config, sub);
            SatraState.Save();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra hub register", ex);
        }
    }

    public static HubClient? ClientFor(string subId) =>
        SatraState.I.Hub.TryGetValue(subId, out var r) && r.Token.Length > 0 ? new HubClient(r.HubBase, r.Token) : null;
}
