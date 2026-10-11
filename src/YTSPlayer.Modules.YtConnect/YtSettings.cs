using YTSPlayer.Contracts.Localization;
namespace YTSPlayer.Modules.YtConnect;

/// <summary>Eine Playlist aus der YT-Music-Bibliothek.</summary>
public sealed record PlaylistInfo(string Id, string Title, string Sub) : System.ComponentModel.INotifyPropertyChanged
{
    bool _playingHere;
    bool _playerRunning;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    void Set(ref bool field, bool value, string name)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    /// <summary>Der laufende Titel steckt in dieser Playlist (die Zeile wird hervorgehoben und zeigt die Balken).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PlayingHere { get => _playingHere; private set => Set(ref _playingHere, value, nameof(PlayingHere)); }

    /// <summary>Der Titel läuft gerade (nicht pausiert): die Balken bewegen sich.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PlayerRunning { get => _playerRunning; private set => Set(ref _playerRunning, value, nameof(PlayerRunning)); }

    /// <summary>Setzt beide Zustände (nur die Anzeige, nicht Teil der gespeicherten Daten).</summary>
    public void SetPlaying(bool here, bool running)
    {
        PlayingHere = here;
        PlayerRunning = here && running;
    }

    /// <summary>Untertitel für die Anzeige. Die feste „Gefällt mir“-Zeile trägt intern einen Marker, der hier durch den übersetzten Text ersetzt wird.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySub => Id == YtLibrary.LikedId && Sub == YtLibrary.LikedSub ? Loc.T("yt.liked.sub") : Sub;
}

/// <summary>
/// Ein Titel in einer Playlist oder ein Suchtreffer. <paramref name="Thumb"/> ist die Adresse des Vorschaubilds, <paramref name="SetId"/> die Eintrags-Id
/// in der Playlist (zum Entfernen, kann leer sein). Nur bei Suchtreffern: <paramref name="Kind"/> ist leer (Titel), „artist“, „album“ oder „playlist“,
/// <paramref name="Ref"/> die Playlist-Id (Album, Playlist) bzw. der Name (Künstler). <paramref name="ArtistId"/> ist die Kennung (UC…) des Künstlers eines Titels, wenn bekannt.
/// </summary>
public sealed record TrackInfo(string Id, string Title, string Artist, string Duration, string Thumb, string SetId = "", string Kind = "", string Ref = "", string ArtistId = "");

/// <summary>Was auf der YT-Seite zuletzt offen war (Abschnitt „yt-view“): geöffnete Playlist und Scroll-Stellen.</summary>
public sealed class YtViewState
{
    public string OpenedId { get; set; } = "";
    public string OpenedTitle { get; set; } = "";
    public string OpenedSub { get; set; } = "";

    /// <summary>Scroll-Stelle der Titelliste (Pixel von oben).</summary>
    public double TracksOffset { get; set; }

    /// <summary>Scroll-Stelle der Playlist-Liste (Pixel von oben).</summary>
    public double ListOffset { get; set; }

    /// <summary>Zuletzt gesuchte Begriffe, neueste zuerst (höchstens 20).</summary>
    public List<string> SearchHistory { get; set; } = [];

    /// <summary>Zeigt die YT-Seite die Startseite (Empfehlungen) statt der Playlists?</summary>
    public bool HomeMode { get; set; }
}

/// <summary>Gespeicherte Playlists (Abschnitt „yt-playlists“). <see cref="CheckedUtc"/> = letzte erfolgreiche Prüfung.</summary>
public sealed class YtPlaylistCache
{
    /// <summary>Stand des Formats. Ältere Stände können falsche Titel enthalten und werden neu geprüft.</summary>
    public int Version { get; set; }
    public List<PlaylistInfo> Items { get; set; } = [];
    public DateTime CheckedUtc { get; set; }
}

/// <summary>Zuletzt geladene Startseite (Abschnitt „yt-home“): nur die erste Seite ohne Stimmungsfilter, damit sie beim Start sofort da ist.</summary>
public sealed class YtHomeCache
{
    public List<ArtistSectionData> Sections { get; set; } = [];
    public List<HomeChip> Chips { get; set; } = [];
    public DateTime SavedUtc { get; set; }
}

/// <summary>Gespeicherte Titel einer Playlist (Abschnitt „yt-pl-…“).</summary>
public sealed class YtTracksCache
{
    public List<TrackInfo> Items { get; set; } = [];
    public DateTime CheckedUtc { get; set; }
}

/// <summary>Zuletzt angezeigtes Profil (Abschnitt „yt-profile“). Enthält nur Anzeige-Daten, nie Passwort oder Token.</summary>
public sealed class YtProfileCache
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public byte[]? Avatar { get; set; }
    public bool LoggedIn { get; set; }
}

/// <summary>Einstellungen des YT-Moduls (Abschnitt „yt“). Das sind die RAM-Hebel der Web-Ansicht.</summary>
public sealed class YtSettings
{
    /// <summary>Aktuelle Lautstärke (0–1). Wird nicht gespeichert, nur zum Start der Web-Ansicht gebraucht.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double Gain { get; set; } = 1;

    public bool BlockImages { get; set; } = true;
    public bool BlockStyles { get; set; } = true;
    public bool BlockFonts { get; set; } = true;
    public bool SongMode { get; set; } = true;
    public bool OffscreenInsteadOfHide { get; set; } = true;
    public bool DisableGpu { get; set; } = true;
    public bool InProcessGpu { get; set; } = true;
    public bool TrimWhenIdle { get; set; } = true;
    public bool LimitV8 { get; set; } = true;
    public bool ProcessPerSite { get; set; }
    public bool SiteIsolationOff { get; set; }
    public int IdleLowSeconds { get; set; } = 30;
    public int IdleDisposeMinutes { get; set; } = 5;
    public string LastUrl { get; set; } = "";
    public double LastPos { get; set; }
}
