using System.Runtime.InteropServices;
using ServiceLib.Satra;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Entry for "SatraN --helper" (root systemd unit on Linux, LocalSystem service "SatraN Helper" on Windows).</summary>
internal static class HelperHost
{
    public const string WinServiceName = "SatraNHelper";
    private static readonly CancellationTokenSource Stop = new();

    public static int Run(string[] args)
    {
        if (args.Contains("--helper-install")) return WinInstall(true);
        if (args.Contains("--helper-uninstall")) return WinInstall(false);
        using var t = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; Stop.Cancel(); });
        using var i = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; Stop.Cancel(); });
        if (OperatingSystem.IsWindows() && !Environment.UserInteractive) return RunWindowsService();
        if (!OperatingSystem.IsWindows() && Environment.UserName != "root") { Console.Error.WriteLine("helper must run as root"); return 1; }
        return TunHelper.RunServer(Stop.Token);
    }

    // ---- Windows service plumbing (no extra packages) ----
    private delegate void ServiceMainFn(int argc, IntPtr argv);
    private delegate int HandlerEx(int control, int eventType, IntPtr eventData, IntPtr ctx);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TableEntry { public string? Name; public ServiceMainFn? Fn; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Status { public int Type, State, Accepted, Exit, SvcExit, Checkpoint, Wait; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool StartServiceCtrlDispatcherW(TableEntry[] t);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterServiceCtrlHandlerExW(string name, HandlerEx h, IntPtr ctx);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetServiceStatus(IntPtr h, ref Status s);

    private static IntPtr _h;
    private static ServiceMainFn? _main;
    private static HandlerEx? _handler;

    private static void SetState(int state)
    {
        var s = new Status { Type = 0x10, State = state, Accepted = state == 4 ? 0x1 | 0x4 : 0 };
        SetServiceStatus(_h, ref s);
    }

    private static int RunWindowsService()
    {
        _handler = (control, _, _, _) =>
        {
            if (control is 1 or 5) { SetState(3); Stop.Cancel(); } // STOP / SHUTDOWN
            return 0;
        };
        _main = (_, _) =>
        {
            _h = RegisterServiceCtrlHandlerExW(WinServiceName, _handler, IntPtr.Zero);
            SetState(4); // RUNNING
            TunHelper.Log = m => { try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SatraN", "helper", "helper.log"), $"{DateTime.Now:s} {m}\n"); } catch { } };
            TunHelper.RunServer(Stop.Token);
            SetState(1); // STOPPED
        };
        return StartServiceCtrlDispatcherW([new TableEntry { Name = WinServiceName, Fn = _main }, new TableEntry()]) ? 0 : Marshal.GetLastWin32Error();
    }

    /// <summary>Run elevated once by the installer: registers/removes the LocalSystem service.</summary>
    private static int WinInstall(bool install)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        var exe = Environment.ProcessPath!;
        int Sc(params string[] a)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("sc.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var x in a) psi.ArgumentList.Add(x);
            using var p = System.Diagnostics.Process.Start(psi)!; p.WaitForExit(); return p.ExitCode;
        }
        Sc("stop", WinServiceName);
        Sc("delete", WinServiceName);
        if (!install) return 0;
        var rc = Sc("create", WinServiceName, "binPath=", $"\"{exe}\" --helper", "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "SatraN Helper");
        Sc("description", WinServiceName, "SatraN VPN helper: starts the TUN core without a UAC prompt.");
        Sc("failure", WinServiceName, "reset=", "86400", "actions=", "restart/2000");
        return rc != 0 ? rc : Sc("start", WinServiceName);
    }
}
