using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ServiceLib.Satra;

/// <summary>
/// Privileged TUN helper (like OpenVPN's service): installed once as root (Linux systemd) or LocalSystem (Windows service).
/// The unprivileged app sends only a sing-box TUN config; the helper validates/sanitizes it, runs the packaged sing-box
/// from a fixed path, checks connectivity (auto-rollback within ~10 s), and tears down routes/DNS on stop, on client
/// disconnect/crash and on helper exit. No arbitrary commands are accepted.
/// Protocol: one JSON line per request: {"op":"ping"} | {"op":"start","config":"..."} | {"op":"stop"}; reply one JSON line.
/// </summary>
public static class TunHelper
{
    public const int ProtocolVersion = 1;
    public const string SocketPath = "/run/satran/helper.sock";
    public const string PipeName = "satran-helper";
    public const string AllowFile = "/etc/satran/helper.allow";
    public static readonly string[] CheckUrls = ["http://cp.cloudflare.com/generate_204", "http://www.gstatic.com/generate_204"];
    public static readonly string[] PrivateRanges = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16", "100.64.0.0/10", "fe80::/10", "fd00::/8"];

    // ---------------- validation (pure, unit-testable) ----------------
    private static readonly HashSet<string> AllowedOutbounds = ["socks", "http", "direct", "block", "dns", "selector", "urltest"];

    /// <summary>Returns the sanitized config to run as root, or throws <see cref="InvalidDataException"/>.</summary>
    public static string Sanitize(string json, string runDir)
    {
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("config too large");
        var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("not an object");

        var inbounds = root["inbounds"] as JsonArray ?? throw new InvalidDataException("no inbounds");
        var tunCount = 0;
        foreach (var ib in inbounds)
        {
            var t = (string?)ib?["type"];
            if (t != "tun") throw new InvalidDataException($"inbound '{t}' not allowed");
            tunCount++;
            var tun = (JsonObject)ib!;
            var ex = tun["route_exclude_address"] as JsonArray ?? new JsonArray();
            var have = ex.Select(x => (string?)x).ToHashSet();
            foreach (var r in PrivateRanges) if (!have.Contains(r)) ex.Add(r);
            tun["route_exclude_address"] = ex;
            var name = (string?)tun["interface_name"];
            if (name != null && !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_\\-]{1,15}$")) throw new InvalidDataException("bad interface_name");
        }
        if (tunCount != 1) throw new InvalidDataException("exactly one tun inbound required");

        foreach (var ob in root["outbounds"] as JsonArray ?? [])
        {
            var t = (string?)ob?["type"];
            if (t == null || !AllowedOutbounds.Contains(t)) throw new InvalidDataException($"outbound '{t}' not allowed");
            if (t is "socks" or "http")
            {
                var s = (string?)ob!["server"];
                if (s is not ("127.0.0.1" or "::1" or "localhost")) throw new InvalidDataException("proxy outbound must be loopback");
            }
        }
        if (root["endpoints"] is JsonArray eps && eps.Count > 0) throw new InvalidDataException("endpoints not allowed");

        if (root["log"] is JsonObject log) log.Remove("output");

        if (root["experimental"] is JsonObject exp)
        {
            foreach (var k in exp.Select(p => p.Key).ToList())
                if (k is not ("cache_file" or "clash_api")) exp.Remove(k);
            if (exp["cache_file"] is JsonObject cf) { cf["path"] = Path.Combine(runDir, "cache.db"); }
            if (exp["clash_api"] is JsonObject ca)
            {
                var ctl = (string?)ca["external_controller"];
                var keep = new JsonObject();
                if (ctl != null && (ctl.StartsWith("127.0.0.1:") || ctl.StartsWith("[::1]:"))) keep["external_controller"] = ctl;
                if (ca["secret"] is JsonNode sec) keep["secret"] = sec.DeepClone();
                exp["clash_api"] = keep;
            }
        }

        if (root["route"] is JsonObject route && route["rule_set"] is JsonArray rs)
        {
            foreach (var r in rs)
            {
                if ((string?)r?["type"] != "local") continue;
                var p = (string?)r!["path"] ?? "";
                if (!(p.EndsWith(".srs") || p.EndsWith(".json")) || p.Contains("..") || !Path.IsPathRooted(p)) throw new InvalidDataException("bad rule_set path");
            }
        }
        return root.ToJsonString();
    }

    // ---------------- client (unprivileged app) ----------------
    private static Stream? _session;
    private static StreamReader? _rd;

    private static Stream? Connect(int timeoutMs)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                p.Connect(timeoutMs);
                return p;
            }
            if (!File.Exists(SocketPath)) return null;
            var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            return new NetworkStream(s, true);
        }
        catch { return null; }
    }

    private static async Task<JsonObject?> Call(Stream s, StreamReader rd, JsonObject req, int timeoutMs)
    {
        var line = System.Text.Encoding.UTF8.GetBytes(req.ToJsonString() + "\n");
        await s.WriteAsync(line);
        await s.FlushAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        var resp = await rd.ReadLineAsync(cts.Token);
        return resp == null ? null : JsonNode.Parse(resp) as JsonObject;
    }

    /// <summary>True when an installed helper answers and accepts this user.</summary>
    public static async Task<bool> AvailableAsync()
    {
        if (Environment.GetEnvironmentVariable("SATRA_NO_HELPER") == "1") return false;
        using var s = Connect(400);
        if (s == null) return false;
        try
        {
            using var rd = new StreamReader(s, leaveOpen: true);
            var r = await Call(s, rd, new JsonObject { ["op"] = "ping" }, 2000);
            return (bool?)r?["ok"] == true;
        }
        catch { return false; }
    }

    /// <summary>Starts TUN via the helper. The session stays open; closing it (or the app dying) tears TUN down.</summary>
    public static async Task<(bool ok, string? err)> StartAsync(string config)
    {
        await StopAsync();
        var gen = Volatile.Read(ref _gen);
        var s = Connect(1000);
        if (s == null) return (false, "helper unavailable");
        var rd = new StreamReader(s, leaveOpen: true);
        try
        {
            var r = await Call(s, rd, new JsonObject { ["op"] = "start", ["config"] = config }, 25000);
            if ((bool?)r?["ok"] == true)
            {
                if (Volatile.Read(ref _gen) != gen)
                {
                    // user disconnected (or another start began) while the helper was bringing TUN up:
                    // never leave a tunnel running with no core behind it.
                    try { await s.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"op\":\"stop\"}\n")); await s.FlushAsync(); await rd.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(6)); } catch { }
                    s.Dispose();
                    return (false, "cancelled");
                }
                _session = s; _rd = rd;
                _monitor = Task.Run(async () =>
                {
                    try { while (await rd.ReadLineAsync() is { } l && !l.Contains("\"ok\"")) { } } catch { }
                    if (ReferenceEquals(_session, s)) { _session = null; _rd = null; s.Dispose(); SessionLost?.Invoke(); }
                });
                return (true, null);
            }
            s.Dispose();
            return (false, (string?)r?["err"] ?? "no reply");
        }
        catch (Exception ex) { s.Dispose(); return (false, ex.Message); }
    }

    public static bool Active => _session != null;
    private static Task? _monitor;
    /// <summary>Raised when the helper ended the session on its own (helper restarted, core died); TUN is already torn down.</summary>
    public static Action? SessionLost;

    private static int _gen;
    /// <summary>Invalidates any start still in flight (its tunnel is stopped as soon as it comes up).</summary>
    public static void CancelPending() => Interlocked.Increment(ref _gen);

    public static async Task StopAsync()
    {
        CancelPending();
        var s = _session; var m = _monitor;
        _session = null; _rd = null;
        if (s == null) return;
        try
        {
            await s.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"op\":\"stop\"}\n"));
            await s.FlushAsync();
            if (m != null) await Task.WhenAny(m, Task.Delay(6000));
        }
        catch { }
        s.Dispose();
    }

    /// <summary>Connectivity check used for auto-rollback: any generate_204 answering 204 within the deadline.</summary>
    public static async Task<bool> ConnectivityOkAsync(TimeSpan deadline)
    {
        using var h = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(4) }) { Timeout = TimeSpan.FromSeconds(4) };
        var end = DateTime.UtcNow + deadline;
        while (DateTime.UtcNow < end)
        {
            foreach (var u in CheckUrls)
            {
                try { using var r = await h.GetAsync(u); if ((int)r.StatusCode == 204) return true; } catch { }
                if (DateTime.UtcNow >= end) break;
            }
            await Task.Delay(500);
        }
        return false;
    }

    // ---------------- server (root / LocalSystem) ----------------
    private static Process? _core;
    private static Stream? _owner;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string RunDir => OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SatraN", "helper") : "/run/satran";
    private static string CoreExe => OperatingSystem.IsWindows() ? Path.Combine(AppContext.BaseDirectory, "bin", "sing_box", "sing-box.exe") : "/opt/satran/bin/sing_box/sing-box";
    private static string LogPath => Path.Combine(RunDir, "core.log");
    public static Action<string> Log = m => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} {m}");

    public static int RunServer(CancellationToken stop)
    {
        Directory.CreateDirectory(RunDir);
        Cleanup();
        stop.Register(() => { StopCore().Wait(); });
        try
        {
            if (OperatingSystem.IsWindows()) ServeWindows(stop).Wait();
            else ServeUnix(stop).Wait();
        }
        catch (AggregateException) when (stop.IsCancellationRequested) { }
        StopCore().Wait();
        return 0;
    }

    private static async Task ServeUnix(CancellationToken stop)
    {
        File.Delete(SocketPath);
        using var ls = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        ls.Bind(new UnixDomainSocketEndPoint(SocketPath));
        File.SetUnixFileMode(SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        ls.Listen(8);
        Log("listening on " + SocketPath);
        while (!stop.IsCancellationRequested)
        {
            var c = await ls.AcceptAsync(stop);
            var uid = PeerUid(c);
            if (!UidAllowed(uid)) { Log($"rejected uid {uid}"); c.Dispose(); continue; }
            _ = Task.Run(() => Session(new NetworkStream(c, true), $"uid {uid}"));
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task ServeWindows(CancellationToken stop)
    {
        var sec = new PipeSecurity();
        sec.AddAccessRule(new PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        sec.AddAccessRule(new PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        sec.AddAccessRule(new PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, System.Security.AccessControl.AccessControlType.Allow));
        sec.AddAccessRule(new PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Deny));
        while (!stop.IsCancellationRequested)
        {
            var p = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, sec);
            await p.WaitForConnectionAsync(stop);
            _ = Task.Run(() => Session(p, "pipe client"));
        }
    }

    private static async Task Session(Stream s, string who)
    {
        var owns = false;
        try
        {
            using var rd = new StreamReader(s, leaveOpen: true);
            while (true)
            {
                var line = await rd.ReadLineAsync();
                if (line == null) break;
                JsonObject resp;
                try
                {
                    var req = JsonNode.Parse(line) as JsonObject;
                    switch ((string?)req?["op"])
                    {
                        case "ping": resp = new JsonObject { ["ok"] = true, ["v"] = ProtocolVersion }; break;
                        case "start":
                            var (ok, err) = await StartCore((string?)req!["config"] ?? "");
                            owns = ok;
                            if (ok) _owner = s;
                            resp = new JsonObject { ["ok"] = ok, ["err"] = err };
                            Log($"{who} start -> {(ok ? "ok" : err)}");
                            break;
                        case "stop":
                            await StopCore(); owns = false;
                            resp = new JsonObject { ["ok"] = true };
                            Log($"{who} stop");
                            break;
                        default: resp = new JsonObject { ["ok"] = false, ["err"] = "unknown op" }; break;
                    }
                }
                catch (Exception ex) { resp = new JsonObject { ["ok"] = false, ["err"] = ex.Message }; }
                await s.WriteAsync(System.Text.Encoding.UTF8.GetBytes(resp.ToJsonString() + "\n"));
                await s.FlushAsync();
            }
        }
        catch { }
        finally
        {
            // client went away (disconnect, crash, kill): never leave the network broken
            if (owns && ReferenceEquals(_owner, s)) { _owner = null; Log($"{who} gone -> teardown"); await StopCore(); }
            s.Dispose();
        }
    }

    private static async Task<(bool, string?)> StartCore(string config)
    {
        await Gate.WaitAsync();
        try
        {
            await StopCoreLocked();
            var clean = Sanitize(config, RunDir);
            var cfg = Path.Combine(RunDir, "tun.json");
            File.WriteAllText(cfg, clean);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(cfg, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var psi = new ProcessStartInfo(CoreExe) { WorkingDirectory = RunDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var a in new[] { "run", "-c", cfg, "-D", RunDir, "--disable-color" }) psi.ArgumentList.Add(a);
            var log = new StreamWriter(LogPath, false) { AutoFlush = true };
            var p = Process.Start(psi)!;
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            _core = p;
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                if (!ReferenceEquals(_core, p)) return;
                Log("core exited unexpectedly -> teardown");
                var own = _owner;
                _ = Task.Run(async () =>
                {
                    await Gate.WaitAsync();
                    try { if (ReferenceEquals(_core, p)) await StopCoreLocked(); } finally { Gate.Release(); }
                    if (ReferenceEquals(_owner, own)) { _owner = null; try { own?.Dispose(); } catch { } }
                });
            };
            await Task.Delay(1200);
            if (p.HasExited) { var tail = Tail(); await StopCoreLocked(); return (false, "core exited: " + tail); }
            if (!await ConnectivityOkAsync(TimeSpan.FromSeconds(10)))
            {
                var tail = Tail();
                await StopCoreLocked();
                return (false, "rollback: no connectivity within 10 s (" + tail + ")");
            }
            return (true, null);
        }
        catch (InvalidDataException ex) { return (false, "rejected: " + ex.Message); }
        finally { Gate.Release(); }
    }

    private static string Tail()
    {
        try { var l = File.ReadAllLines(LogPath); return string.Join(" | ", l.TakeLast(3)); } catch { return ""; }
    }

    private static async Task StopCore() { await Gate.WaitAsync(); try { await StopCoreLocked(); } finally { Gate.Release(); } }

    [DllImport("libc", EntryPoint = "kill")] private static extern int SysKill(int pid, int sig);

    private static async Task StopCoreLocked()
    {
        var p = _core; _core = null;
        if (p != null)
        {
            try
            {
                if (!p.HasExited)
                {
                    if (!OperatingSystem.IsWindows()) SysKill(p.Id, 15);
                    else p.Kill(true);
                    for (var i = 0; i < 30 && !p.HasExited; i++) await Task.Delay(100);
                    if (!p.HasExited) p.Kill(true);
                }
            }
            catch { }
            p.Dispose();
        }
        Cleanup();
    }

    /// <summary>Removes anything a crashed core could have left behind (Linux: sing-box auto_route rules, tun link, resolved DNS).</summary>
    private static void Cleanup()
    {
        if (OperatingSystem.IsWindows()) return;
        var ip = File.Exists("/usr/sbin/ip") ? "/usr/sbin/ip" : "/sbin/ip";
        foreach (var fam in new[] { "-4", "-6" })
            for (var pref = 9000; pref <= 9010; pref++)
                for (var k = 0; k < 4 && Run(ip, fam, "rule", "del", "pref", pref.ToString()) == 0; k++) { }
        if (Directory.Exists("/sys/class/net/singbox_tun")) Run(ip, "link", "del", "singbox_tun");
    }

    private static int Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(3000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    // ---- Linux peer credentials / ACL ----
    private static int PeerUid(Socket s)
    {
        try
        {
            var buf = new byte[12]; // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
            s.GetRawSocketOption(1 /*SOL_SOCKET*/, 17 /*SO_PEERCRED*/, buf);
            return BitConverter.ToInt32(buf, 4);
        }
        catch { return -1; }
    }

    public static bool UidAllowed(int uid, string allowFile = AllowFile, string passwd = "/etc/passwd", string group = "/etc/group")
    {
        if (uid == 0) return true;
        if (uid < 0) return false;
        string? name = null;
        try { name = File.ReadLines(passwd).Select(l => l.Split(':')).FirstOrDefault(f => f.Length > 2 && f[2] == uid.ToString())?[0]; } catch { }
        try
        {
            foreach (var raw in File.ReadLines(allowFile))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith('#')) continue;
                if (l == uid.ToString() || (name != null && l == name)) return true;
            }
        }
        catch { }
        try
        {
            var g = File.ReadLines(group).Select(l => l.Split(':')).FirstOrDefault(f => f.Length > 3 && f[0] == "satran");
            if (g != null && name != null && g[3].Split(',').Contains(name)) return true;
        }
        catch { }
        return false;
    }
}
