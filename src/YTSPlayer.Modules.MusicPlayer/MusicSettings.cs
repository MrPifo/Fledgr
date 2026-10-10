namespace YTSPlayer.Modules.MusicPlayer;

/// <summary>Eine eigene Playlist aus lokalen Dateien.</summary>
public sealed class PlaylistDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Paths { get; set; } = [];
}

/// <summary>Gespeicherte Daten des Musik-Moduls (Abschnitt „music“).</summary>
public sealed class MusicSettings
{
    public List<string> Folders { get; set; } = [];
    public List<PlaylistDef> Playlists { get; set; } = [];
    public List<string> Favorites { get; set; } = [];

    /// <summary>Beim Auswählen eines Titels sofort vorhören (für Sound-Bibliotheken).</summary>
    public bool AutoPreview { get; set; }

    /// <summary>Beim Scan Titel, Interpret und Album aus den Tags lesen, damit die Suche sie findet (kostet etwas Zeit und Speicher).</summary>
    public bool SearchTags { get; set; } = true;
}
