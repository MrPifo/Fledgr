using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Services;

/// <summary>Inhalt von history.json (Abschnitt „history“).</summary>
public sealed class HistoryData
{
    public List<HistoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// Der globale Verlauf. Hört auf den Player und trägt jeden Titel ein, sobald er wirklich spielt (neueste zuerst, ohne Doppelte).
/// Radio zählt als ein Eintrag je Sender, nicht je Titel im Stream. Gespeichert wird bei jeder Änderung (die Datei ist klein).
/// </summary>
public sealed class PlayHistory : IPlayHistory
{
    public const int Max = 200;

    readonly ISettingsStore _store;
    readonly HistoryData _data;
    string _lastKey = "";

    public PlayHistory(ISettingsStore store, IPlayer player)
    {
        _store = store;
        _data = store.Load<HistoryData>("history");
        player.StateChanged += OnState;
    }

    public IReadOnlyList<HistoryEntry> Entries => _data.Entries;

    public event Action? Changed;

    public void Clear()
    {
        _data.Entries.Clear();
        _lastKey = "";
        Save();
        Changed?.Invoke();
    }

    void OnState(PlaybackState st)
    {
        // Erst wenn der Titel wirklich läuft; Meldungen ohne Titel (Seite lädt) und Pausen ändern den Verlauf nicht.
        if (st.Item is not { } item || !st.IsPlaying || st.Title.Length == 0) return;
        var key = item.SourceId + "|" + item.Id;
        if (key == _lastKey)
        {
            // Die Länge ist oft erst nach dem Start bekannt (YouTube): einmal nachtragen.
            if (st.Duration.TotalSeconds > 0 && _data.Entries.Count > 0 && _data.Entries[0] is { DurationSeconds: 0 } first)
            {
                first.DurationSeconds = st.Duration.TotalSeconds;
                Save();
                Changed?.Invoke();
            }
            return;
        }
        _lastKey = key;

        var radio = item.SourceId == "radio";
        var entry = new HistoryEntry
        {
            SourceId = item.SourceId,
            ItemId = item.Id,
            Title = radio ? item.Title : st.Title,
            Artist = radio ? item.Artist : st.Artist,
            Album = radio ? "" : st.Album,
            CoverUrl = st.CoverUrl ?? item.CoverUrl,
            DurationSeconds = radio ? 0 : st.Duration.TotalSeconds,
            PlayedUtc = DateTime.UtcNow,
        };
        Add(entry);
    }

    /// <summary>Trägt einen Eintrag vorn ein (ein gleicher älterer Eintrag fällt weg).</summary>
    public void Add(HistoryEntry entry)
    {
        _data.Entries.RemoveAll(e => e.SourceId == entry.SourceId && e.ItemId == entry.ItemId);
        _data.Entries.Insert(0, entry);
        if (_data.Entries.Count > Max) _data.Entries.RemoveRange(Max, _data.Entries.Count - Max);
        Save();
        Changed?.Invoke();
    }

    void Save()
    {
        try { _store.Save("history", _data); }
        catch (Exception)
        {
            // Speichern darf die Wiedergabe nie stören.
        }
    }
}
