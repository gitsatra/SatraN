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
        UpRate = s.ProxyUp;
        DownRate = s.ProxyDown;
        if (Phase == ConnPhase.On)
        {
            SessionUp += s.ProxyUp;
            SessionDown += s.ProxyDown;
            var d = SatraState.I.Today();
            d.Up += s.ProxyUp;
            d.Down += s.ProxyDown;
            AccountLocal(s.ProxyUp + s.ProxyDown);
        }
        History.Enqueue(s.ProxyUp + s.ProxyDown);
        while (History.Count > 60) History.Dequeue();
        Speed?.Invoke(s.ProxyUp, s.ProxyDown);
    }

    private int _ticks;
    private void Tick()
    {
        if (Phase == ConnPhase.On) SatraState.I.Today().Seconds++;
        if (++_ticks % 30 == 0) SatraState.Save();
        Changed?.Invoke();
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
            switch (SatraState.I.Mode)
            {
                case "tun": Status.EnableTun = true; break;
                case "socks": _socksOn = true; Status.SystemProxySelected = (int)ESysProxyType.ForcedClear; break;
                default: Status.SystemProxySelected = (int)ESysProxyType.ForcedChange; break;
            }
            await Task.Delay(400);
            var r = await Status.TestServerAvailability();
            if (r is { Time: > 0 })
            {
                SessionUp = SessionDown = 0;
                Since = DateTime.Now;
                if (Active != null) NetBadges.ForgetExit([Active.IndexId]); // re-check exit IP on connect
                Set(ConnPhase.On);
                if (Active != null && r.GetValidIp() is { Length: > 0 }) _ = RefreshActiveExitAsync();
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
            var webProxy = new System.Net.WebProxy($"socks5://{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
            var ip = await ConnectionHandler.GetIPInfo(webProxy);
            if (ip is { } info && info.Country.Length == 2)
            {
                SatraState.I.Geo[a.IndexId] = new ExitGeo { Cc = info.Country.ToUpperInvariant(), Ip = info.Ip ?? "", At = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                SatraState.Save();
                Changed?.Invoke();
            }
        }
        catch { }
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
        var before = (await AppManager.Instance.SubItems())?.Count ?? 0;
        await Main.AddServerViaClipboardAsync(text);
        var subs = await AppManager.Instance.SubItems() ?? [];
        if (subs.Count > before)
        {
            // a subscription was added: fetch its servers immediately and make it the active group
            var s = subs.OrderByDescending(x => x.Sort).First();
            await Main.UpdateSubscriptionProcess(s.Id, false);
            _ = SubInfo.RefreshAsync(s);
        }
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
        NetBadges.ForgetExit(Profiles.ProfileItems.Where(p => subId == null || p.Subid == subId).Select(p => p.IndexId).ToList());
        SatraState.Save();
        Changed?.Invoke();
    }

    private async Task AutoUpdateSubsAsync(bool startup = false)
    {
        try
        {
            if (SatraState.I.AutoUpdate && (await AppManager.Instance.SubItems())?.Count > 0) await UpdateSubsAsync();
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
