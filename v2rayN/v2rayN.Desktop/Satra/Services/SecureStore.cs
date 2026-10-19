using System.Security.Cryptography;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>
/// AES-256-GCM encryption for SatraN's local state (Hub device tokens, subscription links, usage).
/// The 256-bit key is random per install and kept outside the data folder, readable only by this user:
/// %LOCALAPPDATA%\SatraN\k (per-user profile ACL) on Windows, ~/.config/SatraN/k (0600) on Linux,
/// ~/Library/Application Support/SatraN/k (0600) on macOS.
/// </summary>
public static class SecureStore
{
    private static readonly byte[] Magic = "SATRA1"u8.ToArray();
    private static byte[]? _key;

    private static string KeyPath
    {
        get
        {
            var root = Environment.GetFolderPath(OperatingSystem.IsWindows() ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "SatraN", "k");
        }
    }

    private static byte[] Key()
    {
        if (_key != null) return _key;
        var p = KeyPath;
        if (File.Exists(p))
        {
            var k = File.ReadAllBytes(p);
            if (k.Length == 32) return _key = k;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var nk = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(p, nk);
        Restrict(p);
        return _key = nk;
    }

    public static void Restrict(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }

    public static bool IsEncrypted(byte[] data) => data.Length > Magic.Length + 28 && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static byte[] Encrypt(string text)
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(text);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(Key(), 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return [.. Magic, .. nonce, .. tag, .. cipher];
    }

    public static string Decrypt(byte[] data)
    {
        if (!IsEncrypted(data)) return System.Text.Encoding.UTF8.GetString(data); // legacy plain JSON, re-saved encrypted
        var o = Magic.Length;
        var nonce = data.AsSpan(o, 12);
        var tag = data.AsSpan(o + 12, 16);
        var cipher = data.AsSpan(o + 28);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(Key(), 16)) aes.Decrypt(nonce, cipher, tag, plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}
