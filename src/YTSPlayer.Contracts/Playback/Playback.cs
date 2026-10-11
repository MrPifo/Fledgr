using YTSPlayer.Contracts.Media;

namespace YTSPlayer.Contracts.Playback;

/// <summary>Was eine Quelle kann. Die UI blendet fehlende Funktionen aus.</summary>
[Flags]
public enum PlaybackCapabilities
{
    None = 0,
    Seek = 1,
    Next = 2,
    Previous = 4,
    Volume = 8,
    Shuffle = 16,
    RadioMix = 32,
    Like = 64,
    TrackMenu = 128,
    Artist = 256,
    Lyrics = 512,
    AddToPlaylist = 1024,
}

/// <summary>Ein Songtext. <see cref="Source"/> ist die Quelle (z. B. „Quelle: LyricFind“), wenn bekannt.</summary>
public sealed record LyricsResult(string Text, string? Source);

/// <summary>Optional für Quellen, die zum laufenden Titel einen Songtext liefern können (Tags oder Datei bei lokaler Musik, YouTube Music bei der Web-Ansicht).</summary>
public interface ILyricsSource
{
    /// <summary>Der Songtext des laufenden Titels oder null, wenn es keinen gibt.</summary>
    Task<LyricsResult?> GetLyricsAsync(CancellationToken ct);
}

/// <summary>Optional für Quellen, die zum Künstler des laufenden Titels eine eigene Seite öffnen können (Klick auf den Künstlernamen).</summary>
public interface IArtistSource
{
    /// <summary>Zeigt die Seite des Künstlers des laufenden Titels.</summary>
    Task OpenArtistAsync();
}

/// <summary>Zustand der Wiedergabe. Wird bei jeder Änderung neu erzeugt.</summary>
public sealed record PlaybackState(
    MediaItem? Item,
    string Title,
    string Artist,
    string Album,
    string? CoverUrl,
    byte[]? CoverBytes,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    double Volume,
    string? ExternalUrl = null,
    string? ExternalLabel = null,
    bool Shuffle = false,
    bool RadioMix = false,
    int Year = 0,
    bool Liked = false,
    bool Disliked = false)
{
    public static readonly PlaybackState Empty = new(null, "", "", "", null, null, false, TimeSpan.Zero, TimeSpan.Zero, 1);
}

/// <summary>
/// Eine Quelle, die Audio abspielt (lokale Datei, YT-Seite, Radio-Stream).
/// <see cref="StateChanged"/> darf von jedem Thread kommen.
/// </summary>
public interface IPlaybackSource : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    PlaybackCapabilities Capabilities { get; }
    PlaybackState State { get; }
    event Action<PlaybackState>? StateChanged;

    /// <summary>Lädt den Eintrag und startet die Wiedergabe. Quellen mit eigener Warteschlange (YT) ignorieren <paramref name="queue"/>.</summary>
    Task StartAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default);

    Task PlayAsync();
    Task PauseAsync();
    Task SeekAsync(TimeSpan position);
    Task SetVolumeAsync(double volume);
    Task NextAsync();
    Task PreviousAsync();

    /// <summary>Stoppt und gibt Ressourcen frei.</summary>
    Task StopAsync();
}

/// <summary>Optional für Quellen mit Zufallswiedergabe. Der Zustand erscheint auch in <see cref="PlaybackState.Shuffle"/>.</summary>
public interface IShuffleSource
{
    /// <summary>Ist die Zufallswiedergabe an? Bei Quellen, die den Zustand selbst nicht kennen, der zuletzt gesetzte Wert.</summary>
    bool Shuffle { get; }

    /// <summary>Wahr, wenn die App den gemerkten Wert beim Aktivieren der Quelle wieder setzen soll (nicht bei Quellen, die ihn selbst behalten, z. B. YouTube).</summary>
    bool AppliesSavedShuffle { get; }

    Task SetShuffleAsync(bool on);
}

/// <summary>
/// Optional für Quellen, die den laufenden Titel bewerten können („Gefällt mir“ / „Gefällt mir nicht“).
/// Der Zustand erscheint in <see cref="PlaybackState.Liked"/> und <see cref="PlaybackState.Disliked"/> (nie beide zugleich).
/// </summary>
public interface ILikeSource
{
    /// <summary>Schaltet „Gefällt mir“ des laufenden Titels um.</summary>
    Task ToggleLikeAsync();

    /// <summary>Schaltet „Gefällt mir nicht“ des laufenden Titels um.</summary>
    Task ToggleDislikeAsync();
}

/// <summary>Optional für Quellen, die zu einem Titel einen Radio-Mix (Titel in ähnlichem Stil) starten können.</summary>
public interface IRadioMixSource
{
    /// <summary>Startet den Radio-Mix zum aktuellen Titel.</summary>
    Task StartRadioMixAsync();
}

/// <summary>
/// Gewichtete Zufallsreihenfolge. Jeder Titel hat ein Alter: wie viele Titel seit seinem letzten Spielen gelaufen sind.
/// Das Gewicht ist das Alter. Der gerade gelaufene Titel hat 0 (kommt nie direkt wieder), mit jedem weiteren Titel steigt es um 1,
/// und je länger ein Titel nicht dran war, desto wahrscheinlicher wird er. Dazu gehört ein Schritt zurück über den Verlauf.
/// </summary>
public sealed class ShuffleOrder(Random? random = null)
{
    const int MaxHistory = 1000;

    readonly Random _random = random ?? Random.Shared;
    readonly List<int> _history = [];
    int[] _age = [];
    int _pos = -1;

    /// <summary>Der aktuelle Titel (Index in der Warteschlange) oder -1.</summary>
    public int Current => _pos >= 0 && _pos < _history.Count ? _history[_pos] : -1;

    /// <summary>Beginnt neu mit <paramref name="first"/> als aktuellem Titel (bei -1 ein zufälliger). Alle anderen starten gleich gewichtet.</summary>
    public void Start(int count, int first)
    {
        _age = new int[count];
        Array.Fill(_age, count);
        _history.Clear();
        _pos = -1;
        if (count == 0) return;
        Play(first >= 0 && first < count ? first : _random.Next(count));
    }

    /// <summary>Der nächste Titel (nach einem Schritt zurück zuerst der schon gespielte). -1 bei leerer Liste.</summary>
    public int Next()
    {
        if (_age.Length == 0) return -1;
        if (_pos + 1 < _history.Count) return _history[++_pos];
        Play(Pick());
        return Current;
    }

    /// <summary>Der vorige Titel im Verlauf oder -1, wenn man am Anfang ist (dann den Titel neu starten).</summary>
    public int Previous() => _pos > 0 ? _history[--_pos] : -1;

    void Play(int index)
    {
        for (var i = 0; i < _age.Length; i++) _age[i]++;
        _age[index] = 0;
        _history.Add(index);
        if (_history.Count > MaxHistory) _history.RemoveRange(0, MaxHistory / 2);
        _pos = _history.Count - 1;
    }

    int Pick()
    {
        long total = 0;
        foreach (var a in _age) total += a;
        if (total <= 0) return _random.Next(_age.Length);
        var r = _random.NextInt64(total);
        for (var i = 0; i < _age.Length; i++)
        {
            r -= _age[i];
            if (r < 0) return i;
        }
        return _age.Length - 1;
    }
}

/// <summary>Optional für Quellen, die direkt an einer Stelle losspielen können (Wiederherstellen nach einem Neustart, ohne vorher bei 0 zu beginnen).</summary>
public interface IStartAtSource
{
    /// <summary>Lädt den Eintrag und startet die Wiedergabe bei <paramref name="position"/>. Quellen mit eigener Warteschlange bekommen die wiederhergestellte in <paramref name="queue"/>.</summary>
    Task StartAtAsync(MediaItem item, TimeSpan position, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default);
}

/// <summary>Optional für Quellen mit eigener Warteschlange (lokale Musik). Die App merkt sie sich für den nächsten Start.</summary>
public interface IQueueSource
{
    /// <summary>Die aktuelle Warteschlange in Abspiel-Reihenfolge.</summary>
    IReadOnlyList<MediaItem> Queue { get; }
}

/// <summary>Die eine Player-Schnittstelle für die ganze App. Die Shell setzt sie um.</summary>
public interface IPlayer
{
    PlaybackState State { get; }
    PlaybackCapabilities Capabilities { get; }
    string? ActiveSourceId { get; }
    double Volume { get; }

    /// <summary>Kommt immer auf dem UI-Thread.</summary>
    event Action<PlaybackState>? StateChanged;

    /// <summary>Quelle gewechselt oder Fähigkeiten geändert.</summary>
    event Action? SourceChanged;

    Task PlayAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default);
    Task TogglePlayAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
    Task SetVolumeAsync(double volume);
    Task StopAsync();
}
