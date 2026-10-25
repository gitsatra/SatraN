namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// Secure, "just works" defaults applied once per hardening revision (users can still change them in Advanced).
/// </summary>
public static class Hardening
{
    public const int Revision = 5;

    /// <summary>SatraNG Android SOCKS port — desktop must match so shared docs/QR/hotspot stay consistent.</summary>
    public const int SatraSocksPort = 28888;

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
                    // Align with Android SatraNG (28888); migrate stock v2rayN 10808
                    if (ib.LocalPort is 0 or 10808 or 10809)
                        ib.LocalPort = SatraSocksPort;
                }
                if (cfg.Mux4RayItem != null) cfg.Mux4RayItem.XudpProxyUDP443 = "allow";
                if (string.IsNullOrEmpty(cfg.CoreBasicItem.DefFingerprint)) cfg.CoreBasicItem.DefFingerprint = "chrome";
                cfg.TunModeItem.AutoRoute = true;
                cfg.TunModeItem.StrictRoute = true;
                cfg.TunModeItem.EnableIPv6Address = true;
                // TUN = sing-box TUN front + the node's own core (xray) behind it, so every config that works
                // in proxy mode also works in VPN mode (sing-box alone fails e.g. some REALITY servers).
                cfg.TunModeItem.EnableLegacyProtect = true;
                // Measured in a netns (vless, loopback): mixed/9000 ≈ 1.6 GB/s vs 1500 ≈ 0.33 GB/s, gvisor ≈ 0.9 GB/s.
                cfg.TunModeItem.Stack = "mixed";
                if (cfg.TunModeItem.Mtu is <= 1500) cfg.TunModeItem.Mtu = 9000;
                // Direct (local) sites: resolve with the region's domestic DNS, not v2rayN's Chinese default.
                if (string.IsNullOrEmpty(d.DirectDNS) || d.DirectDNS.Contains("119.29.29.29") && st.Routing != "china")
                    d.DirectDNS = LocalRules.DirectDns(st.Routing);
                st.Mode = ServiceLib.Satra.ConnModes.Default; // v1.8: Ports only is the default, like v2rayN
                if (OperatingSystem.IsWindows()) EnsureHotkey(cfg);
                await ConfigHandler.SaveConfig(cfg);
                st.Hardened = Revision;
                SatraState.Save();
                if (SatraCore.I != null) await SatraCore.I.Main.Reload();
            }
            catch (Exception ex) { Logging.SaveLog("satra hardening", ex); }
        }
        // Keep Mode defaulted to tun if somehow empty
        if (!ServiceLib.Satra.ConnModes.IsValid(st.Mode)) { st.Mode = ServiceLib.Satra.ConnModes.Default; SatraState.Save(); }
        // Always enforce the dual-core TUN chain (Linux: xray bound to the physical NIC; Windows: process_path bypass).
        if (!cfg.TunModeItem.EnableLegacyProtect)
        {
            cfg.TunModeItem.EnableLegacyProtect = true;
            _ = ConfigHandler.SaveConfig(cfg);
        }
        foreach (var f in new[] { "guiNConfig.json", "guiNDB.db", "satra.json" }) SecureStore.Restrict(Utils.GetConfigPath(f));
    }

    private static void EnsureHotkey(Config cfg)
    {
        cfg.GlobalHotkeys ??= [];
        if (cfg.GlobalHotkeys.Any(k => k.EGlobalHotkey == EGlobalHotkey.SatraToggle)) return;
        cfg.GlobalHotkeys.Add(new KeyEventItem { EGlobalHotkey = EGlobalHotkey.SatraToggle, Control = true, Alt = true, KeyCode = (int)Avalonia.Input.Key.S });
    }
}
