namespace YTSPlayer.Modules.MusicPlayer.Library;

/// <summary>Gelesene Tags einer Datei. <see cref="M"/> = Änderungszeit der Datei (Ticks), damit nur geänderte Dateien neu gelesen werden.</summary>
public sealed class TagEntry
{
    public long M { get; set; }
    public string T { get; set; } = "";
    public string A { get; set; } = "";
    public string B { get; set; } = "";

    /// <summary>Titelnummer im Album (0 = unbekannt).</summary>
    public int N { get; set; }
}

/// <summary>Gespeicherte Tags aller Dateien (Abschnitt „music-tags“). Spart beim nächsten Scan das erneute Lesen.</summary>
public sealed class TagCache
{
    /// <summary>Format-Stand. Ältere Stände (ohne Titelnummer) werden neu gelesen.</summary>
    public int Version { get; set; }

    public Dictionary<string, TagEntry> Items { get; set; } = [];
}
