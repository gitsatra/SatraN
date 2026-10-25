using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Device identity headers (satra-spec v1) – a random install id, never a hardware id.</summary>
public static class Identity
{
    public static string Os => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    public const string Version = "1.7.3";
    public static string App => $"SatraN/{Version}";

    public static void Apply()
    {
        var s = SatraState.I;
        var h = SatraIdentity.Headers;
        h["X-HWID"] = s.Hwid;
        h["X-Device-OS"] = Os;
        h["X-Ver-OS"] = Environment.OSVersion.Version.ToString();
        h["X-Device-Model"] = s.DeviceName;
    }

    public static void AddTo(HttpRequestMessage req)
    {
        foreach (var kv in SatraIdentity.Headers)
        {
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
        req.Headers.TryAddWithoutValidation("User-Agent", $"{App} ({Os}; {RuntimeInformation.OSArchitecture})");
    }
}
