using YTSPlayer.Contracts.Diagnostics;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>Meldet Prozesse und Netzwerk-Zähler der Web-Ansicht an das Diagnose-Dashboard.</summary>
public sealed class YtDiagnostics(YtEngine engine, YtPlaybackSource source) : IDiagnosticsContributor
{
    public string Group => "YT";

    public IReadOnlyList<ProcessRef> GetProcesses() =>
        engine.Processes().Select(p => new ProcessRef(p.Pid, p.Name)).ToList();

    public IReadOnlyList<DiagnosticLine> GetLines()
    {
        var host = engine.IsRunning ? engine.Host : null;
        var status = host == null ? "off (idle)" : source.MemoryLow ? "active, low-memory mode" : "active";
        var lines = new List<DiagnosticLine> { new("Web view", status) };
        if (host != null)
            lines.Add(new DiagnosticLine("Shuffle (app)", (source.Shuffle ? "on" : "off") + $" · queue {source.QueueCount} tracks"));
        if (host != null)
            lines.Add(new DiagnosticLine("Like (page)", source.LikeRaw.Length > 0 ? source.LikeRaw : "no report yet"));
        if (host != null)
            lines.Add(new DiagnosticLine("Duration", $"stream {source.StreamDuration:F0} s · YT bar {(source.BarDuration > 0 ? source.BarDuration.ToString("F0") : "not found")} s"));
        if (host != null)
            lines.Add(new DiagnosticLine("Player events", $"foreign seeks {source.Seeks} · buffering {source.Waits} · reloads {source.Loads} · song clicks {source.SongClicks}"));
        if (host != null)
            lines.Add(new DiagnosticLine("Volume interventions by the page", $"{source.GuardHits}" + (source.GuardLast.Length > 0 ? $" (last wanted {source.GuardLast})" : "") + $" · mute changes {source.MuteChanges}"));
        if (host != null)
            lines.Add(new DiagnosticLine("Network", $"requests {host.Requests} · blocked {host.Blocked} · loaded {host.Bytes / 1048576.0:F1} MB"));
        return lines;
    }
}
