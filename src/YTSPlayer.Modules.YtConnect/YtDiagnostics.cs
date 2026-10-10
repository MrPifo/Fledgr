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
        var status = host == null ? "aus (Leerlauf)" : source.MemoryLow ? "aktiv, Sparmodus" : "aktiv";
        var lines = new List<DiagnosticLine> { new("Web-Ansicht", status) };
        if (host != null)
            lines.Add(new DiagnosticLine("Zufall (App)", (source.Shuffle ? "an" : "aus") + $" · Warteschlange {source.QueueCount} Titel"));
        if (host != null)
            lines.Add(new DiagnosticLine("Mag ich (Seite)", source.LikeRaw.Length > 0 ? source.LikeRaw : "noch keine Meldung"));
        if (host != null)
            lines.Add(new DiagnosticLine("Dauer", $"Stream {source.StreamDuration:F0} s · YT-Leiste {(source.BarDuration > 0 ? source.BarDuration.ToString("F0") : "nicht gefunden")} s"));
        if (host != null)
            lines.Add(new DiagnosticLine("Player-Ereignisse", $"fremde Seeks {source.Seeks} · Puffern {source.Waits} · Neuladen {source.Loads} · Song-Klicks {source.SongClicks}"));
        if (host != null)
            lines.Add(new DiagnosticLine("Lautstärke-Eingriffe der Seite", $"{source.GuardHits}" + (source.GuardLast.Length > 0 ? $" (zuletzt wollte sie {source.GuardLast})" : "") + $" · Stumm-Wechsel {source.MuteChanges}"));
        if (host != null)
            lines.Add(new DiagnosticLine("Netzwerk", $"Anfragen {host.Requests} · blockiert {host.Blocked} · geladen {host.Bytes / 1048576.0:F1} MB"));
        return lines;
    }
}
