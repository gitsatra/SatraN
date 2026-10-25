using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

// Windows installer: find or download SatraN-*-win-x64.zip, extract, run Install-SatraN.ps1 elevated.

const string Version = "1.4.0";
const string Repo = "liamlope/satran";

static string? FindZip(string beside)
{
    var dir = Path.GetDirectoryName(beside)!;
    foreach (var f in Directory.GetFiles(dir, "SatraN-*-win-x64.zip")) return f;
    var payload = Path.Combine(dir, "payload.zip");
    return File.Exists(payload) ? payload : null;
}

static async Task<string?> DownloadZipAsync(string destDir)
{
    var name = $"SatraN-{Version}-win-x64.zip";
    var url = $"https://p.iivt.ir:2097/satra/dl/{Version}/{name}"; // releases repo is private; Satra Hub hosts the files
    var dest = Path.Combine(destDir, name);
    Console.WriteLine("Downloading " + url);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("SatraN-Setup/" + Version);
    using var resp = await http.GetAsync(url);
    if (!resp.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("Download failed: HTTP " + (int)resp.StatusCode);
        return null;
    }
    await using var fs = File.Create(dest);
    await resp.Content.CopyToAsync(fs);
    return dest;
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
