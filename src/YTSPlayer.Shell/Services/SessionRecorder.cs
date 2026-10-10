using Avalonia.Threading;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Merkt sich laufend, welche Seite offen ist und welcher Titel an welcher Stelle spielt, und stellt das nach einem Neustart wieder her.
/// Gespeichert wird sofort bei Titel- oder Play/Pause-Wechsel, sonst alle 10 Sekunden und beim Beenden.
/// Erst nach <see cref="Arm"/> wird aufgezeichnet, damit der alte Stand nicht beim Start überschrieben wird.
/// </summary>
public sealed class SessionRecorder
{
    readonly ISettingsStore _store;
    readonly PlayerService _player;
    readonly SessionState _state;
    readonly DispatcherTimer _timer;
    bool _armed;
    bool _frozen;
    bool _dirty;

    public SessionRecorder(ISettingsStore store, PlayerService player)
    {
        _store = store;
        _player = player;
        _state = store.Load<SessionState>("session");
        Restorable = new SessionState { PageId = _state.PageId, Track = _state.Track, Queue = [.. _state.Queue] };
        player.StateChanged += OnState;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => { if (_dirty) Save(); };
    }

    /// <summary>Der Stand vom letzten Beenden (Kopie, ändert sich nicht mehr).</summary>
    public SessionState Restorable { get; }

    /// <summary>Beginnt mit dem Aufzeichnen (nach dem Wiederherstellen).</summary>
    public void Arm()
    {
        _armed = true;
        _timer.Start();
    }

    /// <summary>Merkt die geöffnete Seite.</summary>
    public void SetPage(string pageId)
    {
        if (!_armed || _frozen || _state.PageId == pageId) return;
        _state.PageId = pageId;
        Save();
    }

    /// <summary>Speichert den letzten Stand und hört auf, weiter aufzuzeichnen (beim Beenden, bevor die Quellen stoppen).</summary>
    public void Flush()
    {
        if (_armed && !_frozen) Capture(_player.State, force: true);
        _frozen = true;
        _timer.Stop();
        if (_armed) Save();
    }

    /// <summary>Stellt den Titel aus dem gespeicherten Stand wieder her. Fehlt die Quelle (Plugin aus), passiert nichts.</summary>
    public Task RestoreTrackAsync()
    {
        if (Restorable.Track is not { ItemId.Length: > 0 } t) return Task.CompletedTask;
        var item = new MediaItem(t.SourceId, t.ItemId, t.Title, t.Artist, t.Album, t.CoverUrl);
        var queue = Restorable.Queue.Count > 0
            ? Restorable.Queue.Select(q => new MediaItem(t.SourceId, q.ItemId, q.Title, q.Artist, q.Album)).ToList()
            : null;
        return _player.RestoreAsync(item, TimeSpan.FromSeconds(t.PositionSeconds), TimeSpan.FromSeconds(t.DurationSeconds), t.WasPlaying, queue);
    }

    const int MaxQueue = 1000;
    string _queueKey = "";

    /// <summary>Merkt die Warteschlange, wenn die aktive Quelle eine hat (lokale Musik). Nur bei Änderung neu aufgebaut.</summary>
    void CaptureQueue()
    {
        if (_player.ActiveSource is not IQueueSource qs)
        {
            // Nur wenn wirklich eine andere Quelle aktiv ist. Ein wiederhergestellter, noch nicht geladener Titel (keine aktive Quelle) behält seine Liste.
            if (_player.ActiveSource != null && _state.Queue.Count > 0) { _state.Queue = []; _queueKey = ""; _dirty = true; }
            return;
        }
        var q = qs.Queue;
        var key = q.Count + "|" + (q.Count > 0 ? q[0].Id + "|" + q[^1].Id : "");
        if (key == _queueKey) return;
        _queueKey = key;
        // Ist die Liste länger als das Limit, bleibt der Teil ab dem laufenden Titel.
        var cur = _player.State.Item?.Id;
        var from = q.Count > MaxQueue ? Math.Max(0, q.ToList().FindIndex(x => x.Id == cur)) : 0;
        _state.Queue = [.. q.Skip(from).Take(MaxQueue).Select(x => new SessionQueueEntry { ItemId = x.Id, Title = x.Title, Artist = x.Artist, Album = x.Album })];
        _dirty = true;
    }

    void OnState(PlaybackState st)
    {
        if (!_armed || _frozen) return;
        Capture(st, force: false);
    }

    void Capture(PlaybackState st, bool force)
    {
        if (st.Item == null)
        {
            // Quelle wurde beendet (z. B. Abmelden): nichts mehr wiederherstellen.
            if (_state.Track != null && !force)
            {
                _state.Track = null;
                Save();
            }
            return;
        }
        // Meldungen ohne Titel (z. B. während die Web-Ansicht eine andere Seite liest) ändern den gemerkten Titel nicht.
        if (st.Title.Length == 0) return;
        var t = _state.Track;
        var changed = t == null || t.ItemId != st.Item.Id || t.SourceId != st.Item.SourceId || t.WasPlaying != st.IsPlaying;
        t ??= _state.Track = new SessionTrack();
        t.SourceId = st.Item.SourceId;
        t.ItemId = st.Item.Id;
        t.Title = st.Title;
        t.Artist = st.Artist;
        t.Album = st.Album;
        t.CoverUrl = st.CoverUrl ?? st.Item.CoverUrl;
        t.PositionSeconds = st.Position.TotalSeconds;
        t.DurationSeconds = st.Duration.TotalSeconds;
        t.WasPlaying = st.IsPlaying;
        CaptureQueue();
        _dirty = true;
        if (changed || force) Save();
    }

    void Save()
    {
        _dirty = false;
        try { _store.Save("session", _state); }
        catch (Exception)
        {
            // Speichern darf die Wiedergabe nie stören.
        }
    }
}
