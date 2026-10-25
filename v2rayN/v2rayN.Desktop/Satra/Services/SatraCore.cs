using System.Net.NetworkInformation;

namespace v2rayN.Desktop.Satra.Services;

public enum ConnPhase { Off, Connecting, On, Disconnecting, Failed }

/// <summary>
/// Glue between the Satra UI and v2rayN's view models. "Connected" means traffic is routed through the
/// core: system proxy set (or TUN on). Also keeps the usage ledger and enforces local limits.
/// </summary>
public class SatraCore : ReactiveObject
{
    public static SatraCore I { get; private set; } = null!;
    public MainWindowViewModel Main { get; }
    public ProfilesViewModel Profiles => Main.ProfilesViewModel;
    public StatusBarViewModel Status => Main.StatusBarViewModel;
    private Config Cfg => AppManager.Instance.Config;

    public event Action? Changed;
    /// <summary>Once per second (session timer etc.); cheap UI updates only.</summary>
    public event Action? Ticked;
    public event Action<double, double>? Speed; // up, down bytes/s
    public event Action<string>? Toast;

    public ConnPhase Phase { get; private set; }
    public DateTime? Since { get; private set; }
    public long SessionUp { get; private set; }
    public long SessionDown { get; private set; }
    public double UpRate { get; private set; }
    public double DownRate { get; private set; }
    public readonly Queue<double> History = new();

    public SatraCore(MainWindowViewModel main)
    {
        I = this;
        Main = main;
        Phase = ConnPhase.Off; // startup always clean (Program.cs cleared proxy/TUN)
        // v1.8: every launch starts in "Ports only" (like v2rayN): the core runs on the selected config and
        // 127.0.0.1 ports just work; system proxy / TUN are explicit, separate choices on Home.
        SatraState.I.Mode = ConnModes.Default;
        if (Phase == ConnPhase.On) Since = DateTime.Now;
        AppEvents.DispatcherStatisticsRequested.AsObservable().ObserveOn(RxSchedulers.MainThreadScheduler).Subscribe(OnStats);
        // Ping/speed rows update themselves via SpeedTestUpdated; do not RaiseChanged (that rebuilds pages).
        CoreConfigHandler.MuxPref = n => SatraState.I.Mux.TryGetValue(Pages.ServersPage.Key(n), out var v) ? v : null;
        CoreConfigHandler.ModePref = () => ServiceLib.Satra.ProfileModes.Normalize(SatraState.I.Profile);
        CoreManager.StartGate = () => Phase is ConnPhase.Connecting or ConnPhase.On;
        CoreManager.TunFailed = e => Avalonia.Threading.Dispatcher.UIThread.Post(async () => { Logging.SaveLog("TUN failed -> disconnect (network left intact): " + e); if (Phase is not (ConnPhase.Connecting or ConnPhase.On)) return; await DisconnectAsync(); Toast?.Invoke(L.S("tun_failed")); Set(ConnPhase.Failed); });
        ServiceLib.Satra.TunHelper.SessionLost = () => Avalonia.Threading.Dispatcher.UIThread.Post(async () => { if (Phase != ConnPhase.On) return; Logging.SaveLog("TUN helper session lost -> reconnect"); await DisconnectAsync(); await Task.Delay(2500); await ConnectAsync(); });
        try { foreach (var f in Directory.EnumerateFiles(Utils.GetBinConfigPath(), "configTest*.json")) if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-10)) File.Delete(f); } catch { }
        DispatcherTimer.Run(() => { Tick(); return true; }, TimeSpan.FromSeconds(1));
        DispatcherTimer.Run(() => { _ = AutoUpdateSubsAsync(); return true; }, TimeSpan.FromMinutes(15));
        SubFallback.Install();
        // Home/tray render before the server list loads at startup; refresh once the active server actually appears/changes.
        if (main.ProfilesViewModel?.ProfileItems is System.Collections.Specialized.INotifyCollectionChanged pc)
        {
            string? lastActive = null;
            pc.CollectionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var id = Active?.IndexId;
                if (id != null && !_autoStarted) { _autoStarted = true; _ = AutoStartAsync(); }
                if (id == lastActive) return;
                lastActive = id;
                Changed?.Invoke();
            });
        }
        DispatcherTimer.RunOnce(() => _ = AutoUpdateSubsAsync(true), TimeSpan.FromSeconds(4));
        // list may already be loaded (no CollectionChanged): start once the UI is up
        DispatcherTimer.RunOnce(() => { if (!_autoStarted && Active != null) { _autoStarted = true; _ = AutoStartAsync(); } }, TimeSpan.FromSeconds(1.5));
#if SATRA_TESTCTL
        TestCtl.Install(this);
#endif
    }

    private bool _autoStarted;
    /// <summary>App open: start the core on the selected config in Ports-only mode, no Connect press needed.</summary>
    private async Task AutoStartAsync()
    {
        if (!SatraState.I.AutoStart || Phase != ConnPhase.Off) return;
        Logging.SaveLog("satra: auto-start (ports only)");
        await ConnectAsync(pick: false);
    }

    /// <summary>Switch how traffic reaches the core (ports / system proxy / TUN). Off -> turns on in that mode.</summary>
    /// <summary>v1.9 state model: picking a mode only selects it. Nothing connects until the user presses the
    /// power button (only the 'Start with the app' Ports auto-start on launch connects by itself). If something is
    /// running in another mode it is stopped cleanly, so the OS never keeps a proxy/tunnel the user did not ask for.</summary>
    public async Task SetModeAsync(string mode)
    {
        if (!ConnModes.IsValid(mode)) return;
        var was = SatraState.I.Mode;
        if (was == mode) return;
        SatraState.I.Mode = mode;
        // Fortress needs the whole system; outside TUN it steps down to Secure (closest protection that still applies)
        if (!ServiceLib.Satra.ProfileModes.Available(SatraState.I.Profile, mode == ConnModes.Tun)) SatraState.I.Profile = ServiceLib.Satra.ProfileModes.Secure;
        SatraState.Save();
        Logging.SaveLog($"Satra mode {was} -> {mode} (selected, waiting for connect)");
        if (Phase is ConnPhase.On or ConnPhase.Connecting or ConnPhase.Failed) await DisconnectAsync();
        ModeAwaitingConnect = true;
        Changed?.Invoke();
    }

    /// <summary>Pick a connection profile (Normal/Game/Secure/...). Never connects by itself; when already connected
    /// the core restarts with the new overlay (same as switching a server). Fortress is refused outside whole-device mode.</summary>
    public async Task<bool> SetProfileAsync(string id)
    {
        id = ServiceLib.Satra.ProfileModes.Normalize(id);
        if (!ServiceLib.Satra.ProfileModes.Available(id, SatraState.I.Mode == ConnModes.Tun)) return false;
        if (SatraState.I.Profile == id) return true;
        SatraState.I.Profile = id;
        SatraState.Save();
        Logging.SaveLog($"Satra profile -> {id}");
        ProfileChanged?.Invoke(id);
        if (Phase == ConnPhase.On) { await DisconnectAsync(); await ConnectAsync(pick: false); }
        Changed?.Invoke();
        return true;
    }
    public event Action<string>? ProfileChanged;

    /// <summary>True after the user picked a mode and has not pressed connect since (Home shows "Press to start …").</summary>
    public bool ModeAwaitingConnect { get; set; }

    public bool IsRouted => Cfg.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange || Cfg.TunModeItem.EnableTun || (SatraState.I.Mode == "socks" && _socksOn);
    private bool _socksOn;

    /// <summary>Linux/macOS TUN iface created by sing-box (Windows uses wintun differently).</summary>
    private static bool TunIfaceUp()
    {
        if (OperatingSystem.IsWindows()) return true; // core running is enough; wintun name varies
        try
        {
            foreach (var name in new[] { "singbox_tun", "satra-tun" })
            {
                var ni = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                // Linux TUN often reports OperationalStatus.Unknown (not Up) even when
                // ip link shows LOWER_UP — treat anything except Down/NotPresent as live.
                if (ni != null && ni.OperationalStatus is not (OperationalStatus.Down or OperationalStatus.NotPresent))
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    public ProfileItemModel? Active => Profiles.ProfileItems.FirstOrDefault(p => p.IndexId == Cfg.IndexId);

    private void Set(ConnPhase p)
    {
        if (Phase != p) Logging.SaveLog($"Satra phase {Phase} -> {p} (mode {SatraState.I.Mode})");
        Phase = p;
        Changed?.Invoke();
    }

    private void OnStats(ServerSpeedItem s)
    {
        // the stats services report KiB per tick (1 s)
        var up = s.ProxyUp * 1024;
        var down = s.ProxyDown * 1024;
        UpRate = up;
        DownRate = down;
        _lastStat = DateTime.Now;
        if (Phase == ConnPhase.On)
        {
            SessionUp += up;
            SessionDown += down;
            var d = SatraState.I.Today();
            d.Up += up;
            d.Down += down;
            UsageHistory.AddConfig(Cfg.IndexId, up, down);
            AccountLocal(up + down);
        }
        History.Enqueue(up + down);
        while (History.Count > 60) History.Dequeue();
        Speed?.Invoke(up, down);
    }

    private DateTime _lastStat;
    private int _ticks;
    private void Tick()
    {
        if ((DateTime.Now - _lastStat).TotalSeconds > 2.5 && (UpRate > 0 || DownRate > 0))
        {
            UpRate = DownRate = 0;
            History.Enqueue(0);
            while (History.Count > 60) History.Dequeue();
            Speed?.Invoke(0, 0);
        }
        if (Phase == ConnPhase.On) SatraState.I.Today().Seconds++;
        if (++_ticks % 30 == 0 && Phase == ConnPhase.On) SatraState.Save(); // nothing changes while Off
        if (_ticks % 5 == 0) { _ = WatchCoreAsync(); if (Phase == ConnPhase.On) _ = UsageHistory.PollAsync(); }
        Ticked?.Invoke();
    }

    private int _restarts;
    private DateTime _restartWindow = DateTime.Now;
    private bool _watching;
    /// Core crash recovery: if xray dies while we are routed, restart it (max 3 per 10 min), else disconnect cleanly.
    private async Task WatchCoreAsync()
    {
        if (_watching || Phase != ConnPhase.On || Cfg.TunModeItem.EnableTun) return;
        if (CoreManager.Instance.CoreRunning) return;
        _watching = true;
        try
        {
            if ((DateTime.Now - _restartWindow).TotalMinutes > 10) { _restarts = 0; _restartWindow = DateTime.Now; }
            if (++_restarts <= 3)
            {
                Logging.SaveLog("satra: core exited, restarting");
                await Main.Reload();
                await Task.Delay(1500);
                if (CoreManager.Instance.CoreRunning) { Toast?.Invoke(L.S("core_restarted")); return; }
            }
            Toast?.Invoke(L.S("core_failed"));
            // kill switch: keep the system pointed at the (dead) tunnel/proxy so nothing leaks outside the VPN
            if (SatraState.I.KillSwitch) { Set(ConnPhase.Failed); return; }
            await DisconnectAsync();
        }
        catch (Exception ex) { Logging.SaveLog("satra watch", ex); }
        finally { _watching = false; }
    }

    private bool _userOff;
    public async Task ToggleAsync()
    {
        if (Phase is ConnPhase.Connecting or ConnPhase.Disconnecting) return;
        if (Phase == ConnPhase.On) { _userOff = true; await DisconnectAsync(); }
        else { _userOff = false; await ConnectAsync(); }
    }

    public Task ConnectAsync() => ConnectAsync(true);

    public async Task ConnectAsync(bool pick)
    {
        ModeAwaitingConnect = false;
        if (Active == null && Profiles.ProfileItems.Count == 0)
        {
            Toast?.Invoke(L.S("no_server"));
            return;
        }
        var gen = ++_connGen;
        Set(ConnPhase.Connecting);
        try
        {
            if (pick && SatraState.I.SmartConnect && Profiles.ProfileItems.Count > 1)
            {
                await SmartPickAsync(quiet: true);
                if (gen != _connGen) return; // Off pressed while picking
            }
            // Mutually exclusive modes: real VPN (TUN) vs system proxy vs listen-only proxy
            switch (SatraState.I.Mode)
            {
                case "tun":
                    if (OperatingSystem.IsLinux() && !await TunPrivilege.EnsureAsync())
                    {
                        Toast?.Invoke(L.S("vpn_denied"));
                        Set(ConnPhase.Failed);
                        return;
                    }
                    Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
                    // Bypass StatusBar toggle path entirely — never prompts for a password.
                    if (!await Status.ApplyTunModeAsync(true))
                    {
                        Toast?.Invoke(L.S("vpn_denied"));
                        Set(ConnPhase.Failed);
                        return;
                    }
                    // Off pressed while TUN was being enabled: undo, so no stale TUN flag survives
                    if (gen != _connGen) { await Status.ApplyTunModeAsync(false); return; }
                    break;
                case "socks":
                    if (Cfg.TunModeItem.EnableTun) await Status.ApplyTunModeAsync(false);
                    _socksOn = true;
                    Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
                    if (!CoreManager.Instance.CoreRunning) await Main.Reload(); // nothing else triggers a core start here
                    break;
                default:
                    if (Cfg.TunModeItem.EnableTun) await Status.ApplyTunModeAsync(false);
                    Status.SystemProxySelected = (int)ESysProxyType.ForcedChange;
                    break;
            }
            // Wait until the chosen mode is actually engaged (core reload is async)
            var engaged = false;
            var waitMs = SatraState.I.Mode == "tun" ? 80 : 25; // TUN start + DNS can take longer
            for (var i = 0; i < waitMs; i++)
            {
                await Task.Delay(200);
                if (gen != _connGen) return; // user pressed Off meanwhile: never reload/re-enable behind their back
                // TUN start (helper + connectivity check) takes seconds; an early second reload would restart it mid-way
                if (i == (SatraState.I.Mode == "tun" ? 50 : 4) && !CoreManager.Instance.CoreRunning) await Main.Reload();
                engaged = SatraState.I.Mode switch
                {
                    "tun" => Cfg.TunModeItem.EnableTun && CoreManager.Instance.CoreRunning && CoreManager.Instance.TunRunning && TunIfaceUp(),
                    "proxy" => Cfg.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange && CoreManager.Instance.CoreRunning,
                    _ => CoreManager.Instance.CoreRunning,
                };
                if (engaged) break;
            }
            // TUN: iface+core is the contract. Google generate_204 often times out under
            // auto_route DNS race and must not tear down a working tunnel.
            if (gen != _connGen) return;
            AvailabilityCheckResult? r = null;
            bool ok;
            if (SatraState.I.Mode == "tun")
            {
                ok = engaged && TunIfaceUp() && CoreManager.Instance.CoreRunning && CoreManager.Instance.TunRunning;
                if (ok)
                {
                    try
                    {
                        r = await Status.TestServerAvailability().WaitAsync(TimeSpan.FromSeconds(4));
                    }
                    catch (TimeoutException) { /* exit IP optional */ }
                    catch (OperationCanceledException) { /* exit IP optional */ }
                }
            }
            else if (SatraState.I.Mode == ConnModes.Ports)
            {
                // Ports only: the ports are open as soon as the core runs. A slow/blocked test URL must not
                // tear down a working proxy (that was "never connects / errors" in 1.7.x); report it instead.
                ok = engaged;
                if (ok) _ = Task.Run(async () =>
                {
                    var t = await Status.TestServerAvailability();
                    if (t is not { Time: > 0 } && IsCurrentConnect(gen)) Avalonia.Threading.Dispatcher.UIThread.Post(() => { ServerSilent = true; Toast?.Invoke(L.S("srv_no_answer")); Changed?.Invoke(); });
                });
            }
            else
            {
                r = engaged ? await Status.TestServerAvailability() : null;
                ok = engaged && r is { Time: > 0 };
            }
            if (gen != _connGen) return; // cancelled during the availability test: DisconnectAsync owns cleanup
            if (ok)
            {
                ServerSilent = false;
                SessionUp = SessionDown = 0;
                Since = DateTime.Now;
                Set(ConnPhase.On);
                _ = RefreshActiveExitAsync();
            }
            else
            {
                Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
                if (Cfg.TunModeItem.EnableTun) await Status.ApplyTunModeAsync(false);
                _socksOn = false;
                await CoreManager.Instance.CoreStop();
                LastError = await DiagnoseAsync();
                if (SatraState.I.Mode != ConnModes.Ports && gen == _connGen)
                {
                    // System proxy / TUN could not be engaged: fall back to Ports only so the user is never left with nothing
                    var failed = SatraState.I.Mode;
                    SatraState.I.Mode = ConnModes.Ports;
                    Toast?.Invoke(L.F("mode_fell_back", L.S(ConnModes.Label(failed))));
                    Set(ConnPhase.Off);
                    await ConnectAsync(pick: false);
                    return;
                }
                if (LastError != null) Toast?.Invoke(LastError);
                Set(ConnPhase.Failed);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra connect", ex);
            Set(ConnPhase.Failed);
        }
    }

    /// <summary>True when the core runs but the server did not answer the reachability test.</summary>
    public bool ServerSilent { get; private set; }
    /// <summary>Human reason of the last failure (core refused the config, port busy…), null if unknown.</summary>
    public string? LastError { get; private set; }

    /// <summary>Ask the core why it refused to start (xray -test), so a failure is never silent.</summary>
    private static async Task<string?> DiagnoseAsync()
    {
        try
        {
            var cfg = Utils.GetBinConfigPath(Global.CoreConfigFileName);
            var exe = CoreInfoManager.Instance.GetCoreExecFile(CoreInfoManager.Instance.GetCoreInfo(ECoreType.Xray), out _);
            if (!File.Exists(cfg) || string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            if (!File.ReadAllText(cfg).Contains("\"protocol\"")) return null; // sing-box config: xray -test does not apply
            var psi = new System.Diagnostics.ProcessStartInfo(exe, ["run", "-test", "-c", cfg]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.Environment[Global.XrayLocalAsset] = Utils.GetBinPath("");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync() + await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            if (p.ExitCode == 0) return null;
            var line = o.Split('\n').LastOrDefault(l => l.Contains("Failed", StringComparison.OrdinalIgnoreCase)) ?? "";
            Logging.SaveLog("satra diagnose: " + line);
            return L.S("core_refused") + CoreReason(line);
        }
        catch (Exception ex) { Logging.SaveLog("satra diagnose", ex); return null; }
    }

    /// Known core failures in the UI language; the raw English line stays in the log (and is shown only in English UI).
    internal static string CoreReason(string line)
    {
        if (line.Length == 0) return "";
        var l = line.ToLowerInvariant();
        string? k = l.Contains("address already in use") || l.Contains("only one usage of each socket") ? "core_err_port"
            : l.Contains("geosite") || l.Contains("geoip") || l.Contains(".dat") ? "core_err_geo"
            : l.Contains("failed to parse") || l.Contains("invalid") || l.Contains("unknown") || l.Contains("json") ? "core_err_config"
            : l.Contains("permission denied") || l.Contains("operation not permitted") ? "core_err_perm"
            : null;
        if (k != null) return " · " + L.S(k);
        return L.Lang == "en" ? " · " + line[(line.LastIndexOf('>') + 1)..].Trim() : "";
    }

    private async Task RefreshActiveExitAsync()
    {
        try
        {
            var a = Active;
            if (a == null) return;
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            var r = await ExitProbe.LookupAsync(port);
            if (r is { } v)
            {
                // Tor exit badge on Home: cached list (6 h), refilled through the proxy only when stale
                try { await TorExits.GetAsync(port, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(25)); } catch { }
                lock (SatraState.I.Geo) SatraState.I.Geo[NetBadges.GeoKey(a)] = new ExitGeo { Cc = v.cc, Ip = v.ip, At = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                SatraState.Save();
                Changed?.Invoke();
            }
        }
        catch { }
    }

    public void RaiseChanged() => Changed?.Invoke();

    public async Task ReconnectAsync()
    {
        await DisconnectAsync();
        await ConnectAsync();
    }

    private int _connGen;
    /// <summary>True while connect attempt <paramref name="gen"/> is still the current one (not cancelled by Off).</summary>
    internal bool IsCurrentConnect(int gen) => gen == _connGen;

    public async Task DisconnectAsync()
    {
        _connGen++;
        Set(ConnPhase.Disconnecting);
        _socksOn = false;
        if (Cfg.TunModeItem.EnableTun) await Status.ApplyTunModeAsync(false);
        Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
        await Task.Delay(450);
        // OFF means OFF in every mode: stop the core so no local port stays open and nothing runs in the background
        await CoreManager.Instance.CoreStop();
        Since = null;
        Set(ConnPhase.Off);
    }

    /// <summary>Real-ping every server of the current group, then switch to the lowest stable delay.</summary>
    public async Task<ProfileItemModel?> SmartPickAsync(bool quiet = false)
    {
        var items = Profiles.ProfileItems.ToList();
        if (items.Count == 0) return null;
        int D(ProfileItemModel p) => PingEngine.I.Ms(p.IndexId);
        if (items.Count(p => D(p) > 0) < Math.Min(3, items.Count))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(quiet ? 10 : 20));
            try { await PingEngine.I.PingAllAsync(items).WaitAsync(cts.Token); } catch (OperationCanceledException) { }
        }
        var best = items.Where(p => D(p) > 0).OrderBy(D).FirstOrDefault();
        if (best != null && best.IndexId != Cfg.IndexId)
        {
            await Profiles.SetDefaultServer(best.IndexId);
        }
        return best;
    }

    public async Task RunTestAsync(ESpeedActionType kind, IList<ProfileItemModel>? only = null)
    {
        Profiles.SelectedProfiles = only?.ToList() ?? Profiles.ProfileItems.ToList();
        await Profiles.ServerSpeedtest(kind);
    }

    public async Task UseAsync(ProfileItemModel p)
    {
        await Profiles.SetDefaultServer(p.IndexId);
        Changed?.Invoke();
        // running (any mode): the core reloads on the new config; refresh the exit IP shown on Home
        if (Phase == ConnPhase.On) _ = Task.Run(async () => { await Task.Delay(1500); await RefreshActiveExitAsync(); });
        else if (Phase is ConnPhase.Failed or ConnPhase.Off && SatraState.I.Mode == ConnModes.Ports && SatraState.I.AutoStart && _autoStarted && !_userOff) await ConnectAsync(pick: false);
    }

    public async Task<bool> AddFromClipboardAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast?.Invoke(L.S("nothing_clip"));
            return false;
        }
        var t = text.Trim();
        var looksValid = t.Contains("://") || t.StartsWith('{') || (t.Length >= 16 && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z0-9+/=_\-\r\n]+$"));
        if (!looksValid)
        {
            Toast?.Invoke(L.S("invalid_link"));
            return false;
        }
        var before = (await AppManager.Instance.SubItems())?.Count ?? 0;
        var beforeP = (await AppManager.Instance.ProfileItems(""))?.Count ?? 0;
        await Main.AddServerViaClipboardAsync(text);
        var subs = await AppManager.Instance.SubItems() ?? [];
        if (subs.Count == before && ((await AppManager.Instance.ProfileItems(""))?.Count ?? 0) == beforeP)
        {
            Toast?.Invoke(L.S("invalid_link"));
            return false;
        }
        if (subs.Count > before)
        {
            // a subscription was added: fetch its servers immediately and make it the active group
            var s = subs.OrderByDescending(x => x.Sort).First();
            await Main.UpdateSubscriptionProcess(s.Id, false);
            _ = SubInfo.RefreshAsync(s);
        }
        _ = Task.Run(() => ExitProbe.ProbeAsync()); // learn real exit countries for the new configs
        Toast?.Invoke(L.S("added"));
        Changed?.Invoke();
        return true;
    }

    public async Task UpdateSubsAsync(string? subId = null)
    {
        SubFallback.Install();
        SubFallback.LastVia = null;
        await Main.UpdateSubscriptionProcess(subId ?? "", false);
        var rescued = SubFallback.LastVia != null;
        foreach (var s in await AppManager.Instance.SubItems() ?? [])
        {
            if (subId == null || s.Id == subId)
            {
                await SubInfo.RefreshAsync(s);
                // fetched through a working config: the direct header probe failing is expected — stay calm
                if (rescued && SatraState.I.Subs.TryGetValue(s.Id, out var rm) && rm.Error != null)
                {
                    rm.Error = null;
                    rm.Fetched = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                await HubSync.EnsureRegisteredAsync(s);
                // fetched fine but nothing usable inside → bad format
                if (SatraState.I.Subs.TryGetValue(s.Id, out var sm) && sm.Error == null && s.Url.StartsWith("http")
                    && !Profiles.ProfileItems.Any(p => p.Subid == s.Id))
                {
                    sm.Error = L.S("sub_err_format");
                    sm.ErrorAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }
        }
        // exit IPs are re-learned after a subscription update
        SatraState.Save();
        Changed?.Invoke();
        // re-measure exit IPs after a subscription update (old flags stay visible until replaced)
        // only configs with no known exit (new ones) — re-probing every server on each update wasted CPU/data
        _ = Task.Run(() => ExitProbe.ProbeAsync());
    }

    /// <summary>Per-subscription schedule: each sub's own interval, else 6 h when global auto-update is on. Only due subs are fetched.</summary>
    private async Task AutoUpdateSubsAsync(bool startup = false)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var due = new List<SubItem>();
            foreach (var s in await AppManager.Instance.SubItems() ?? [])
            {
                if (!s.Enabled) continue;
                var mins = s.AutoUpdateInterval > 0 ? s.AutoUpdateInterval : SatraState.I.AutoUpdate ? 360 : 0;
                if (mins <= 0) continue;
                var last = SatraState.I.Subs.TryGetValue(s.Id, out var m) ? m.Fetched : 0;
                if (now - last >= mins * 60L) due.Add(s);
            }
            foreach (var s in due) await UpdateSubsAsync(s.Id);
            if (due.Count == 0 && startup) _ = Task.Run(() => ExitProbe.ProbeAsync());
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra auto update", ex);
        }
    }

    public void Notify(string msg) => Toast?.Invoke(msg);

    // ---------------- local limits ----------------
    private static string PeriodKey(string period) => period switch
    {
        "weekly" => System.Globalization.ISOWeek.GetWeekOfYear(DateTime.Now).ToString() + DateTime.Now.Year,
        "monthly" => DateTime.Now.ToString("yyyy-MM"),
        "total" => "total",
        _ => DateTime.Now.ToString("yyyy-MM-dd"),
    };

    /// <summary>Local "all traffic" limits count proxied bytes. Site/program rules are enforced as blocks
    /// in the routing table once exhausted (quota 0 = always blocked).</summary>
    private void AccountLocal(long bytes)
    {
        var changed = false;
        foreach (var l in SatraState.I.Limits.Where(x => x.Enabled))
        {
            var k = PeriodKey(l.Period);
            if (l.PeriodKey != k)
            {
                l.PeriodKey = k;
                l.UsedBytes = 0;
                l.TopupBytes = 0;
                if (l.Exhausted) { l.Exhausted = false; changed = true; }
            }
            if (l.Type == "all") l.UsedBytes += bytes;
            var ex = l.UsedBytes >= l.QuotaBytes + l.TopupBytes;
            if (ex && !l.Exhausted)
            {
                l.Exhausted = true;
                changed = true;
                Toast?.Invoke(L.F("limit_reached", l.Name));
            }
        }
        if (changed) _ = ApplyLocalLimitsAsync();
    }

    public async Task ApplyLocalLimitsAsync()
    {
        var all = SatraState.I.Limits.Where(l => l.Enabled).ToList();
        if (all.Any(l => l.Type == "all" && l.Exhausted) && Phase == ConnPhase.On)
        {
            await DisconnectAsync();
        }
        await LocalRules.SyncAsync(all.Where(l => l.Type != "all" && (l.Exhausted || l.QuotaBytes == 0)).ToList());
        SatraState.Save();
        Changed?.Invoke();
    }
}
