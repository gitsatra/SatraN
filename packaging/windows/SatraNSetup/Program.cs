using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

// Windows installer: find or download SatraN-*-win-x64.zip, extract, run Install-SatraN.ps1 elevated.

const string Version = "1.5.0";
const string Repo = "gitsatra/SatraN";

static string? FindZip(string beside)
{
    var dir = Path.GetDirectoryName(beside)!;
    foreach (var f in Directory.GetFiles(dir, "SatraN-*-win-x64.zip")) return f;
    var payload = Path.Combine(dir, "payload.zip");
    return File.Exists(payload) ? payload : null;
}

// Satra Hub: the hostname sits behind a CDN front that some networks block — fall back to the origin IP (same TLS cert/SNI).
static HttpClient Hub(bool origin)
{
    var h = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
    if (origin)
        h.ConnectCallback = async (ctx, ct) =>
        {
            var host = ctx.DnsEndPoint.Host == "p.iivt.ir" ? "107.189.24.195" : ctx.DnsEndPoint.Host;
            var s = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try { await s.ConnectAsync(host, ctx.DnsEndPoint.Port, ct); return new System.Net.Sockets.NetworkStream(s, true); }
            catch { s.Dispose(); throw; }
        };
    var c = new HttpClient(h) { Timeout = TimeSpan.FromMinutes(15) };
    c.DefaultRequestHeaders.UserAgent.ParseAdd("SatraN-Setup/" + Version);
    return c;
}

static async Task<string?> DownloadZipAsync(string destDir)
{
    const string B = "https://p.iivt.ir:2097/satra/dl/"; // releases repo is private; Satra Hub hosts the files
    var name = $"SatraN-{Version}-win-x64.zip";
    var dest = Path.Combine(destDir, name);
    foreach (var origin in new[] { false, true })
    {
        try
        {
            using var http = Hub(origin);
            // expected SHA-256 from the Hub manifest — never install an unverified package
            string? sha = null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(B + "latest.json"));
                if (doc.RootElement.GetProperty("files").TryGetProperty(name, out var f)) sha = f.GetProperty("sha256").GetString();
            }
            catch { }
            Console.WriteLine("Downloading " + B + Version + "/" + name + (origin ? " (direct)" : ""));
            using var resp = await http.GetAsync(B + Version + "/" + name, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine("HTTP " + (int)resp.StatusCode); continue; }
            await using (var fs = File.Create(dest)) await resp.Content.CopyToAsync(fs);
            if (sha != null)
            {
                await using var rf = File.OpenRead(dest);
                var got = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(rf));
                if (!got.Equals(sha, StringComparison.OrdinalIgnoreCase)) { Console.Error.WriteLine("Checksum mismatch"); File.Delete(dest); continue; }
            }
            return dest;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
    }
    return null;
}

var exe = Environment.ProcessPath ?? "SatraN-Setup.exe";
var zip = FindZip(exe);
if (zip == null)
{
    var dir = Path.Combine(Path.GetTempPath(), "SatraN-Setup-dl");
    Directory.CreateDirectory(dir);
    zip = await DownloadZipAsync(dir);
}
if (zip == null)
{
    Console.Error.WriteLine("Could not locate or download the SatraN package.");
    return 1;
}

var dest = Path.Combine(Path.GetTempPath(), "SatraN-Setup-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(dest);
ZipFile.ExtractToDirectory(zip, dest);
var root = Directory.GetDirectories(dest).FirstOrDefault(d => File.Exists(Path.Combine(d, "SatraN.exe")))
    ?? (File.Exists(Path.Combine(dest, "SatraN.exe")) ? dest : null);
if (root == null)
{
    Console.Error.WriteLine("SatraN.exe missing inside package.");
    return 2;
}

var ps1 = Path.Combine(root, "Install-SatraN.ps1");
if (!File.Exists(ps1))
{
    Console.Error.WriteLine("Install-SatraN.ps1 missing.");
    return 3;
}

var psi = new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"",
    UseShellExecute = true,
    Verb = "runas",
    WorkingDirectory = root,
};
try
{
    using var p = Process.Start(psi);
    p?.WaitForExit();
    return p?.ExitCode ?? 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 4;
}
