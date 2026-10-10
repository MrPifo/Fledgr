using System.Diagnostics;

namespace YTSPlayer.Contracts.Diagnostics;

/// <summary>Eine laufende Messung. Dispose beendet sie.</summary>
public sealed class PerfScope : IDisposable
{
    readonly PerfTracker _owner;
    readonly string _name;
    readonly long _start = Stopwatch.GetTimestamp();
    bool _failed;
    bool _done;

    internal PerfScope(PerfTracker owner, string name)
    {
        _owner = owner;
        _name = name;
    }

    /// <summary>Markiert die Operation als fehlgeschlagen.</summary>
    public void Fail() => _failed = true;

    public void Dispose()
    {
        if (_done) return;
        _done = true;
        _owner.Finish(_name, Stopwatch.GetElapsedTime(_start), _failed);
    }
}

/// <summary>Thread-sichere Umsetzung von <see cref="IPerfTracker"/>.</summary>
public sealed class PerfTracker : IPerfTracker
{
    sealed class Stat
    {
        public int Count, Failures, Running;
        public TimeSpan Last, Total, Max;
    }

    readonly Dictionary<string, Stat> _stats = [];
    readonly object _lock = new();

    public PerfScope Begin(string operation)
    {
        lock (_lock)
        {
            if (!_stats.TryGetValue(operation, out var s)) _stats[operation] = s = new Stat();
            s.Running++;
        }
        return new PerfScope(this, operation);
    }

    internal void Finish(string name, TimeSpan elapsed, bool failed)
    {
        if (failed) AppLog.Warn("Operation", $"{name} fehlgeschlagen nach {elapsed.TotalMilliseconds:F0} ms");
        lock (_lock)
        {
            var s = _stats[name];
            s.Running = Math.Max(0, s.Running - 1);
            s.Count++;
            if (failed) s.Failures++;
            s.Last = elapsed;
            s.Total += elapsed;
            if (elapsed > s.Max) s.Max = elapsed;
        }
    }

    public IReadOnlyList<PerfEntry> Snapshot()
    {
        lock (_lock)
        {
            return _stats
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new PerfEntry(kv.Key, kv.Value.Count, kv.Value.Failures, kv.Value.Last,
                    kv.Value.Count > 0 ? kv.Value.Total / kv.Value.Count : TimeSpan.Zero, kv.Value.Max, kv.Value.Running))
                .ToList();
        }
    }
}
