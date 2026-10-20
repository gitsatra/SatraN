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
        Phase = IsRouted ? ConnPhase.On : ConnPhase.Off;
        if (Phase == ConnPhase.On) Since = DateTime.Now;
        AppEvents.DispatcherStatisticsRequested.AsObservable().ObserveOn(RxSchedulers.MainThreadScheduler).Subscribe(OnStats);
        // Ping/speed rows update themselves via SpeedTestUpdated; do not RaiseChanged (that rebuilds pages).
        DispatcherTimer.Run(() => { Tick(); return true; }, TimeSpan.FromSeconds(1));
        DispatcherTimer.Run(() => { _ = AutoUpdateSubsAsync(); return true; }, TimeSpan.FromMinutes(15));
        DispatcherTimer.RunOnce(() => _ = AutoUpdateSubsAsync(true), TimeSpan.FromSeconds(4));
    }

    public bool IsRouted => Cfg.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange || Cfg.TunModeItem.EnableTun || (SatraState.I.Mode == "socks" && _socksOn);
    private bool _socksOn;

    public ProfileItemModel? Active => Profiles.ProfileItems.FirstOrDefault(p => p.IndexId == Cfg.IndexId);

    private void Set(ConnPhase p)
    {
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
        if (++_ticks % 30 == 0) SatraState.Save();
        if (_ticks % 5 == 0) _ = WatchCoreAsync();
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

    public async Task ToggleAsync()
    {
        if (Phase is ConnPhase.Connecting or ConnPhase.Disconnecting) return;
        if (Phase == ConnPhase.On) await DisconnectAsync();
        else await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        if (Active == null && Profiles.ProfileItems.Count == 0)
        {
            Toast?.Invoke(L.S("no_server"));
            return;
        }
        Set(ConnPhase.Connecting);
        try
        {
            if (SatraState.I.SmartConnect && Profiles.ProfileItems.Count > 1)
            {
                await SmartPickAsync(quiet: true);
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
                    // TunPrivilege ensured CAP_NET_ADMIN; AllowEnableTun uses LinuxCaps (/usr/sbin/getcap) so no sudo prompt.
                    if (!Status.EnableTun) Status.EnableTun = true;
                    else Status.ReloadRequested.Publish();
                    break;
                case "socks":
                    if (Status.EnableTun) Status.EnableTun = false;
                    _socksOn = true;
                    Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
                    break;
                default:
                    if (Status.EnableTun) Status.EnableTun = false;
                    Status.SystemProxySelected = (int)ESysProxyType.ForcedChange;
                    break;
            }
            // Wait until the chosen mode is actually engaged (core reload is async)
            var engaged = false;
            for (var i = 0; i < 25; i++)
            {
                await Task.Delay(200);
                engaged = SatraState.I.Mode switch
                {
                    "tun" => Cfg.TunModeItem.EnableTun && CoreManager.Instance.CoreRunning,
                    "proxy" => Cfg.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange && CoreManager.Instance.CoreRunning,
                    _ => CoreManager.Instance.CoreRunning,
                };
                if (engaged) break;
            }
            var r = engaged ? await Status.TestServerAvailability() : null;
            if (r is { Time: > 0 } && engaged)
            {
                SessionUp = SessionDown = 0;
                Since = DateTime.Now;
                Set(ConnPhase.On);
                _ = RefreshActiveExitAsync();
            }
            else
            {
                Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
                if (Cfg.TunModeItem.EnableTun) Status.EnableTun = false;
                _socksOn = false;
                Set(ConnPhase.Failed);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra connect", ex);
            Set(ConnPhase.Failed);
        }
    }

    private async Task RefreshActiveExitAsync()
    {
        try
        {
            var a = Active;
            if (a == null) return;
            var r = await ExitProbe.LookupAsync(AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
            if (r is { } v)
            {
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

    public async Task DisconnectAsync()
    {
        Set(ConnPhase.Disconnecting);
        _socksOn = false;
        if (Cfg.TunModeItem.EnableTun) Status.EnableTun = false;
        Status.SystemProxySelected = (int)ESysProxyType.ForcedClear;
        await Task.Delay(450);
        Since = null;
        Set(ConnPhase.Off);
    }

    /// <summary>Real-ping every server of the current group, then switch to the lowest stable delay.</summary>
    public async Task<ProfileItemModel?> SmartPickAsync(bool quiet = false)
    {
        var items = Profiles.ProfileItems.ToList();
        if (items.Count == 0) return null;
        var fresh = items.Count(p => p.Delay > 0);
        if (fresh < Math.Min(3, items.Count))
        {
            await RunTestAsync(ESpeedActionType.Realping, items);
            await WaitForDelaysAsync(items, TimeSpan.FromSeconds(quiet ? 8 : 15));
        }
        var best = items.Where(p => p.Delay > 0).OrderBy(p => p.Delay).FirstOrDefault();
        if (best != null && best.IndexId != Cfg.IndexId)
        {
            await Profiles.SetDefaultServer(best.IndexId);
        }
        return best;
    }

    private static async Task WaitForDelaysAsync(List<ProfileItemModel> items, TimeSpan max)
    {
        var until = DateTime.Now + max;
        while (DateTime.Now < until)
        {
            await Task.Delay(500);
            if (items.All(p => p.Delay != 0)) break;
            if (items.Count(p => p.Delay > 0) >= Math.Min(5, items.Count) && DateTime.Now > until - max / 2) break;
        }
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
        await Main.UpdateSubscriptionProcess(subId ?? "", false);
        foreach (var s in await AppManager.Instance.SubItems() ?? [])
        {
            if (subId == null || s.Id == subId)
            {
                await SubInfo.RefreshAsync(s);
                await HubSync.EnsureRegisteredAsync(s);
            }
        }
        // exit IPs are re-learned after a subscription update
        SatraState.Save();
        Changed?.Invoke();
        // re-measure exit IPs after a subscription update (old flags stay visible until replaced)
        var again = Profiles.ProfileItems.Where(p => subId == null || p.Subid == subId).ToList();
        _ = Task.Run(() => ExitProbe.ProbeAsync(again));
    }

    private async Task AutoUpdateSubsAsync(bool startup = false)
    {
        try
        {
            if (SatraState.I.AutoUpdate && (await AppManager.Instance.SubItems())?.Count > 0) await UpdateSubsAsync();
            else if (startup) _ = Task.Run(() => ExitProbe.ProbeAsync());
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
