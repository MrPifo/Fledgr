namespace YTSPlayer.Contracts.Playback;

/// <summary>Ein Eintrag im globalen Verlauf: was zuletzt lief, aus welcher Quelle und wann.</summary>
public sealed class HistoryEntry
{
    public string SourceId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string? CoverUrl { get; set; }

    /// <summary>Länge des Titels in Sekunden (0 = unbekannt, z. B. Radio). Wird nachgetragen, sobald die Quelle sie kennt.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Zeitpunkt des Starts (UTC).</summary>
    public DateTime PlayedUtc { get; set; }
}

/// <summary>Der Verlauf über alle Quellen: neueste zuerst, jeder Titel höchstens einmal.</summary>
public interface IPlayHistory
{
    IReadOnlyList<HistoryEntry> Entries { get; }

    /// <summary>Der Verlauf hat sich geändert (Aufruf auf dem UI-Thread).</summary>
    event Action? Changed;

    /// <summary>Löscht alle Einträge.</summary>
    void Clear();
}
