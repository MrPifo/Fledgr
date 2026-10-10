using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Shell.Services;

namespace YTSPlayer.Shell.Services;

/// <summary>Misst Speicher und CPU der App und aller Prozesse, die Module melden.</summary>
public sealed class ProcessMonitor(IProcessStats stats, PluginHost host)
{
    readonly Dictionary<int, (long Cpu, long Tick)> _prev = [];

    public ProcessSample Sample()
    {
        var rows = new List<ProcessRow>();
        var groups = new Dictionary<string, double>();
        foreach (var c in host.Diagnostics.ToList())
        {
            double sum = 0;
            foreach (var p in SafeProcesses(c))
                if (Add(rows, c.Group, p.Name, p.Pid) is { } r) sum += r.Mb;
            groups[c.Group] = groups.GetValueOrDefault(c.Group) + sum;
        }
        var self = Add(rows, "App", "Host (Fledgr)", Environment.ProcessId);
        var live = rows.Select(r => r.Pid).ToHashSet();
        foreach (var k in _prev.Keys.Where(k => !live.Contains(k)).ToList()) _prev.Remove(k);
        return new ProcessSample(rows, self?.Mb ?? 0, groups);
    }

    /// <summary>Leert den Arbeitssatz der App und aller gemeldeten Prozesse. Gibt zurück, bei wie vielen Prozessen das gelang.</summary>
    public int TrimAll()
    {
        var ok = stats.Trim(Environment.ProcessId) ? 1 : 0;
        foreach (var c in host.Diagnostics.ToList())
            foreach (var p in SafeProcesses(c))
                if (stats.Trim(p.Pid)) ok++;
        return ok;
    }

    /// <summary>Ein fehlerhaftes Modul darf die Diagnose nicht zum Absturz bringen.</summary>
    static IReadOnlyList<ProcessRef> SafeProcesses(IDiagnosticsContributor c)
    {
        try { return c.GetProcesses(); }
        catch (Exception) { return []; }
    }

    ProcessRow? Add(List<ProcessRow> rows, string group, string name, int pid)
    {
        var bytes = stats.PrivateBytes(pid);
        if (bytes < 0) return null;
        var cpu = stats.CpuTicks(pid);
        var now = Environment.TickCount64;
        double pct = 0;
        if (cpu >= 0)
        {
            if (_prev.TryGetValue(pid, out var p) && now > p.Tick)
                pct = (cpu - p.Cpu) / 10000.0 / (now - p.Tick) / Environment.ProcessorCount * 100;
            _prev[pid] = (cpu, now);
        }
        var row = new ProcessRow(group, name, pid, bytes / 1048576.0, Math.Max(0, pct));
        rows.Add(row);
        return row;
    }
}
