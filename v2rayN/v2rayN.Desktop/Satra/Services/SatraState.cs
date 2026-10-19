using System.Text.Json;
using System.Text.Json.Serialization;

namespace v2rayN.Desktop.Satra.Services;

public class HubReg
{
    public string HubBase { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Token { get; set; } = "";
    public string DeviceSubUrl { get; set; } = "";
    public string OriginalUrl { get; set; } = "";
    public string ParentSubId { get; set; } = "";
}

public class SubMeta
{
    public long Up { get; set; }
    public long Down { get; set; }
    public long Total { get; set; }
    public long Expire { get; set; }
    public long FirstSeen { get; set; }
    public string? Title { get; set; }
    public string? Announce { get; set; }
    public string? SupportUrl { get; set; }
    public string? RenewUrl { get; set; }
    public string? WebPage { get; set; }
    public string? HubBase { get; set; }
    public long Fetched { get; set; }
}

public class ExitGeo
{
    public string Cc { get; set; } = "";
    public string Ip { get; set; } = "";
    public long At { get; set; }
}

public class DayUsage
{
    public long Up { get; set; }
    public long Down { get; set; }
    public long Seconds { get; set; }
}

public class LocalLimit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>all | domain | process</summary>
    public string Type { get; set; } = "all";
    public List<string> Values { get; set; } = [];
    public long QuotaBytes { get; set; }
    public string Period { get; set; } = "daily";
    public long UsedBytes { get; set; }
    public long TopupBytes { get; set; }
    public string PeriodKey { get; set; } = "";
    public bool Exhausted { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>SatraN's own small state file (satra.json next to the v2rayN config).</summary>
public class SatraState
{
    public string Hwid { get; set; } = Guid.NewGuid().ToString();
    public string DeviceName { get; set; } = Environment.MachineName;
    public bool Advanced { get; set; }
    public bool SmartConnect { get; set; } = true;
    /// <summary>proxy = system proxy, tun = whole computer, socks = proxy only (apps connect to the local port).</summary>
    public string Mode { get; set; } = "proxy";
    public string Routing { get; set; } = "iran";
    public bool AutoUpdate { get; set; } = true;
    public int Hardened { get; set; }
    public List<string> Favorites { get; set; } = [];
    /// Last good latency per server, keyed by address:port:id so it survives subscription refreshes.
    public Dictionary<string, int> Pings { get; set; } = [];
    public Dictionary<string, HubReg> Hub { get; set; } = [];
    public Dictionary<string, SubMeta> Subs { get; set; } = [];
    public Dictionary<string, ExitGeo> Geo { get; set; } = [];
    public Dictionary<string, DayUsage> Days { get; set; } = [];
    public List<LocalLimit> Limits { get; set; } = [];

    [JsonIgnore] public static SatraState I { get; private set; } = new();
    private static string PathOf => Utils.GetConfigPath("satra.json");
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    private static readonly object Gate = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(PathOf))
            {
                I = JsonSerializer.Deserialize<SatraState>(SecureStore.Decrypt(File.ReadAllBytes(PathOf))) ?? new SatraState();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("satra state load", ex);
            I = new SatraState();
        }
        Save();
    }

    public static void Save()
    {
        lock (Gate)
        lock (I.Geo)
        {
            try
            {
                var tmp = PathOf + ".tmp";
                File.WriteAllBytes(tmp, SecureStore.Encrypt(JsonSerializer.Serialize(I, Opt)));
                SecureStore.Restrict(tmp);
                File.Move(tmp, PathOf, true);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("satra state save", ex);
            }
        }
    }

    public DayUsage Today()
    {
        var k = DateTime.Now.ToString("yyyy-MM-dd");
        if (!Days.TryGetValue(k, out var d))
        {
            Days[k] = d = new DayUsage();
            foreach (var old in Days.Keys.Where(x => string.CompareOrdinal(x, DateTime.Now.AddDays(-40).ToString("yyyy-MM-dd")) < 0).ToList())
            {
                Days.Remove(old);
            }
        }
        return d;
    }
}
