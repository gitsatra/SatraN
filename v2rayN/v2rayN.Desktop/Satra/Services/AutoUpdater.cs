namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// App-open update flow (no polling loop): ~15 s after start, check Satra Hub for a newer SatraN; when
/// AppUpdateMode = 1 the package is downloaded in the background (SHA-256 checked) and Home/Settings show
/// "Update to X — Restart". Geo/rule files (and cores that don't need admin rights, only while disconnected)
/// are refreshed at most once per 12 h, each validated with rollback by <see cref="AssetUpdates"/>.
/// A 12 h re-check runs only while the app stays open.
/// </summary>
public static class AutoUpdater
{
    public enum St { Idle, Checking, Available, Downloading, Ready, Installing, Error, UpToDate }
    public static St State { get; private set; } = St.Idle;
    public static SatraAppUpdate.CheckResult? Found { get; private set; }
    public static string? ReadyPath { get; private set; }
    public static double Progress { get; private set; }
    public static string? Error { get; private set; }
    public static List<AssetUpdates.Kind> PendingCores { get; } = [];
    public static event Action? Changed;
    private static int _busy;

    public static void Start()
    {
        DispatcherTimer.RunOnce(() => _ = RunAsync(false), TimeSpan.FromSeconds(15));
        DispatcherTimer.Run(() => { _ = RunAsync(false); return true; }, TimeSpan.FromHours(12));
    }

    private static void Set(St s) { State = s; Dispatcher.UIThread.Post(() => Changed?.Invoke()); }

    public static async Task RunAsync(bool manual)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            await CheckAppAsync(manual);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (SatraState.I.AutoAssets && (manual || now - SatraState.I.LastAssetCheck > 12 * 3600))
            {
                SatraState.I.LastAssetCheck = now;
                SatraState.Save();
                await AssetsAsync();
            }
        }
        catch (Exception ex) { Logging.SaveLog("satra autoupdate", ex); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private static async Task CheckAppAsync(bool manual)
    {
        if (State is St.Downloading or St.Ready or St.Installing) return;
        Set(St.Checking);
        var r = await SatraAppUpdate.CheckAsync();
        SatraState.I.LastAppCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!r.HasUpdate)
        {
            Found = null;
            Error = r.Version == null ? L.S("asset_err_check") : null;
            Set(r.Version == null ? St.Error : St.UpToDate);
            return;
        }
        Found = r;
        Set(St.Available);
        if (SatraState.I.AppUpdateMode == 1) await DownloadAsync();
    }

    public static async Task DownloadAsync()
    {
        var u = Found;
        if (u?.AssetUrl == null || u.AssetName == null || State is St.Downloading or St.Ready) return;
        Set(St.Downloading);
        var last = DateTime.MinValue;
        var prog = new Progress<(long got, long? total)>(p =>
        {
            if (p.total is not > 0) return;
            Progress = (double)p.got / p.total.Value;
            if ((DateTime.Now - last).TotalMilliseconds > 250) { last = DateTime.Now; Dispatcher.UIThread.Post(() => Changed?.Invoke()); }
        });
        var (ok, path, err) = await SatraAppUpdate.DownloadAsync(u.AssetUrl, u.AssetName, prog, default, u.Sha256);
        if (!ok || path == null) { Error = err ?? L.S("asset_err_download"); Set(St.Error); return; }
        ReadyPath = path;
        Progress = 1;
        Set(St.Ready);
    }

    /// <summary>Downloads if needed, then installs (apt via polkit on .deb, self-swap for portable, Setup on Windows) and restarts.</summary>
    public static async Task InstallAsync()
    {
        if (State != St.Ready) await DownloadAsync();
        if (State != St.Ready || ReadyPath == null) return;
        Set(St.Installing);
        var err = await SatraAppUpdate.InstallAsync(ReadyPath);
        if (err != null) { Error = err; Set(St.Ready); SatraCore.I.Notify(err); }
    }

    private static async Task AssetsAsync()
    {
        PendingCores.Clear();
        foreach (var k in new[] { AssetUpdates.Kind.Geo, AssetUpdates.Kind.Rules, AssetUpdates.Kind.Xray, AssetUpdates.Kind.SingBox })
        {
            try
            {
                var info = await AssetUpdates.InfoAsync(k, true);
                if (!AssetUpdates.IsNewer(info)) continue;
                var isCore = k is AssetUpdates.Kind.Xray or AssetUpdates.Kind.SingBox;
                // cores: never interrupt a live connection and never pop an admin prompt on our own
                if (isCore && (SatraCore.I.Phase != ConnPhase.Off || AssetUpdates.CoreNeedsRoot(k))) { PendingCores.Add(k); continue; }
                var e = await AssetUpdates.UpdateAsync(k, null);
                Logging.SaveLog($"satra auto asset {k}: {e ?? "ok " + info.Latest}");
            }
            catch (Exception ex) { Logging.SaveLog("satra auto asset " + k, ex); }
        }
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }
}
