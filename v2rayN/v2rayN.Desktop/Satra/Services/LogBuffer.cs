using System.Collections.Concurrent;

namespace v2rayN.Desktop.Satra.Services;

/// <summary>Ring buffer of everything the cores and the app print (same stream the classic message view used).</summary>
public static class LogBuffer
{
    public enum Lvl { Debug, Info, Warn, Error }
    public sealed record Line(DateTime At, Lvl Level, string Text);

    private const int Max = 6000;
    private static readonly ConcurrentQueue<Line> _pending = new();
    private static readonly List<Line> _all = new();
    private static readonly Lock _gate = new();
    private static int _started;
    public static long Version { get; private set; }

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        AppEvents.SendMsgViewRequested.AsObservable().Subscribe(Add);
    }

    public static void Add(string? msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;
        foreach (var raw in msg.Replace("\r", "").Split('\n'))
        {
            var t = raw.TrimEnd();
            if (t.Length == 0) continue;
            _pending.Enqueue(new Line(DateTime.Now, Parse(t), t));
        }
    }

    public static Lvl Parse(string t)
    {
        static bool Has(string s, string k) => s.Contains(k, StringComparison.OrdinalIgnoreCase);
        if (Has(t, "[Error]") || Has(t, " ERROR") || Has(t, "FATAL") || Has(t, "panic:") || t.StartsWith("ERROR")) return Lvl.Error;
        if (Has(t, "[Warning]") || Has(t, " WARN") || t.StartsWith("WARN")) return Lvl.Warn;
        if (Has(t, "[Debug]") || Has(t, " DEBUG") || Has(t, " TRACE") || t.StartsWith("DEBUG")) return Lvl.Debug;
        return Lvl.Info;
    }

    /// <summary>Moves pending lines into the buffer; returns the newly added ones (UI thread calls this on a timer).</summary>
    public static List<Line> Drain()
    {
        var got = new List<Line>();
        while (got.Count < 2000 && _pending.TryDequeue(out var l)) got.Add(l);
        if (got.Count == 0) return got;
        lock (_gate)
        {
            _all.AddRange(got);
            if (_all.Count > Max) _all.RemoveRange(0, _all.Count - Max);
            Version++;
        }
        return got;
    }

    public static List<Line> Snapshot() { lock (_gate) return [.. _all]; }

    public static void Clear() { lock (_gate) { _all.Clear(); Version++; } while (_pending.TryDequeue(out _)) { } }
}
