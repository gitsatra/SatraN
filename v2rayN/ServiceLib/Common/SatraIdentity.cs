namespace ServiceLib.Common;

/// <summary>
/// SatraN device identity (satra-spec v1). Sent on every subscription fetch so 3x-ui's HWID
/// feature and Satra Hub can recognise this install. Set by the desktop app at startup.
/// </summary>
public static class SatraIdentity
{
    public static Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static void MergeInto(Dictionary<string, string> target)
    {
        foreach (var kv in Headers)
        {
            target.TryAdd(kv.Key, kv.Value);
        }
    }
}
