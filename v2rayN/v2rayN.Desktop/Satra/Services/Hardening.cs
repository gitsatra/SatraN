namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Secure, "just works" defaults applied once per hardening revision (users can still change them in Advanced).
/// - DNS over HTTPS for remote lookups, binding-query blocking, no system hosts leaking into the core.
/// - Sniffing (http, tls, quic) in route-only mode: domain rules work, but connections keep their real
///   destination IP. This is what Telegram (IP-only MTProto) and games/voice need to connect reliably.
/// - UDP on for the local inbound; XUDP/QUIC through mux is allowed instead of rejected.
/// - Chrome uTLS fingerprint by default; TUN with strict route and no IPv6 (prevents IPv6 leaks).
/// - Local data files readable only by the current user.
/// </summary>
public static class Hardening
{
    public const int Revision = 2;

    public static async Task ApplyAsync()
    {
        var st = SatraState.I;
        var cfg = AppManager.Instance.Config;
        if (st.Hardened < Revision)
        {
            try
            {
                cfg.SimpleDNSItem ??= ConfigHandler.InitBuiltinSimpleDNS();
                var d = cfg.SimpleDNSItem;
                if (string.IsNullOrEmpty(d.RemoteDNS) || !d.RemoteDNS.StartsWith("https://")) d.RemoteDNS = "https://cloudflare-dns.com/dns-query";
                d.BlockBindingQuery = true;
                d.UseSystemHosts = false;
                foreach (var ib in cfg.Inbound ?? [])
                {
                    ib.UdpEnabled = true;
                    ib.SniffingEnabled = true;
                    ib.RouteOnly = true;
                    ib.DestOverride = ["http", "tls", "quic"];
                }
                if (cfg.Mux4RayItem != null) cfg.Mux4RayItem.XudpProxyUDP443 = "allow";
                if (string.IsNullOrEmpty(cfg.CoreBasicItem.DefFingerprint)) cfg.CoreBasicItem.DefFingerprint = "chrome";
                // VPN mode = sing-box TUN: auto-route + strict route, IPv4 AND IPv6 captured (no v6 leak), DNS hijacked
                cfg.TunModeItem.AutoRoute = true;
                cfg.TunModeItem.StrictRoute = true;
                cfg.TunModeItem.EnableIPv6Address = true;
                if (string.IsNullOrEmpty(cfg.TunModeItem.Stack)) cfg.TunModeItem.Stack = "mixed";
                if (st.Hardened < 2) st.Mode = "tun"; // VPN (whole device) is the default mode
                if (OperatingSystem.IsWindows()) EnsureHotkey(cfg);
                await ConfigHandler.SaveConfig(cfg);
                st.Hardened = Revision;
                SatraState.Save();
                if (SatraCore.I != null) await SatraCore.I.Main.Reload(); // regenerate the core config with the new defaults
            }
            catch (Exception ex) { Logging.SaveLog("satra hardening", ex); }
        }
        foreach (var f in new[] { "guiNConfig.json", "guiNDB.db", "satra.json" }) SecureStore.Restrict(Utils.GetConfigPath(f));
    }

    /// Ctrl+Alt+S toggles the connection from anywhere (Windows global hotkey).
    private static void EnsureHotkey(Config cfg)
    {
        cfg.GlobalHotkeys ??= [];
        if (cfg.GlobalHotkeys.Any(k => k.EGlobalHotkey == EGlobalHotkey.SatraToggle)) return;
        cfg.GlobalHotkeys.Add(new KeyEventItem { EGlobalHotkey = EGlobalHotkey.SatraToggle, Control = true, Alt = true, KeyCode = (int)Avalonia.Input.Key.S });
    }
}
