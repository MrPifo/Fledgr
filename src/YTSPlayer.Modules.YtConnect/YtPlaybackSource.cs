using System.Text.Json;
using Avalonia.Threading;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Spielt YouTube Music über die versteckte Web-Ansicht. Steuert den Speicher-Sparmodus:
/// nach kurzer Ruhe „Low“ und Arbeitssatz leeren, nach langer Ruhe die Web-Ansicht beenden.
/// </summary>
public sealed class YtPlaybackSource : IPlaybackSource, IStartAtSource, IShuffleSource, IRadioMixSource, ILikeSource, YTSPlayer.Ui.ITrackMenuProvider, YTSPlayer.Ui.IAddToPlaylistProvider, IArtistSource, ILyricsSource, IExternalVolumeSource
{
    /// <summary>Das Plugin setzt hier das Laden des Songtexts ein (Video-Id, Abbruch → Text).</summary>
    public Func<string, CancellationToken, Task<LyricsResult?>>? LyricsHandler { get; set; }

    public Task<LyricsResult?> GetLyricsAsync(CancellationToken ct) =>
        UrlNormalizer.VideoId(_state.Item?.Id) is { } id && LyricsHandler != null ? LyricsHandler(id, ct) : Task.FromResult<LyricsResult?>(null);

    /// <summary>Kennung (UC…) des Künstlers des laufenden Titels laut Player-Leiste der Seite. Leer, wenn nicht gefunden.</summary>
    public string CurrentArtistId { get; private set; } = "";

    /// <summary>Das Plugin setzt hier das Öffnen der Künstler-Seite ein (die Logik liegt im ViewModel der Seite).</summary>
    public Func<Task>? ArtistHandler { get; set; }

    public Task OpenArtistAsync() => ArtistHandler?.Invoke() ?? Task.CompletedTask;

    /// <summary>Das Plugin setzt hier das Menü des laufenden Titels ein (die Menü-Logik liegt in der Ansicht).</summary>
    public Action<Avalonia.Controls.Control, IReadOnlyList<Avalonia.Controls.Control>>? TrackMenuHandler { get; set; }

    public void ShowTrackMenu(Avalonia.Controls.Control anchor, IReadOnlyList<Avalonia.Controls.Control> extraItems) => TrackMenuHandler?.Invoke(anchor, extraItems);

    /// <summary>Das Plugin setzt hier die Playlist-Auswahl für den laufenden Titel ein.</summary>
    public Action<Avalonia.Controls.Control>? AddToPlaylistHandler { get; set; }

    public void ShowAddToPlaylist(Avalonia.Controls.Control anchor) => AddToPlaylistHandler?.Invoke(anchor);

    // Bewertung des laufenden Titels wie auf der Seite: LIKE, DISLIKE oder INDIFFERENT.
    string _rating = Indifferent;
    string? _rateWant;
    long _rateUntil;
    string _rateTitle = "";
    const string Like = "LIKE", Dislike = "DISLIKE", Indifferent = "INDIFFERENT";
    bool _shuffle;
    readonly ShuffleOrder _order = new();
    List<TrackInfo> _queue = [];
    string? _queueList;
    // Video-Id, die wir selbst geladen haben. Nur für sie lösen wir das „Titel zu Ende“ aus (nicht für Reste der alten Seite).
    string _expect = "";
    bool _advancing;
    public const string SourceKey = "yt";

    readonly YtEngine _engine;
    readonly YtSettings _s;
    readonly ISettingsStore _store;
    readonly IProcessStats _stats;
    readonly IPerfTracker _perf;
    readonly CancellationTokenSource _cts = new();
    PlaybackState _state = PlaybackState.Empty;
    double _volume = 1;
    double _pendingSeek;
    bool _needInit;
    bool _playAfterInit;
    bool _low;
    int _idleSec;

    readonly IChildAudioVolume? _audio;
    long _volumeSetTick;
    long _volumePollTick;

    /// <summary>Die Lautstärke wurde im Windows-Lautstärkemixer geändert (Eintrag der WebView). Wert 0–1.</summary>
    public event Action<double>? VolumeChangedExternally;

    public YtPlaybackSource(YtEngine engine, YtSettings settings, ISettingsStore store, IProcessStats stats, IPerfTracker perf, IChildAudioVolume? audio = null)
    {
        _audio = audio;
        _engine = engine;
        _s = settings;
        _store = store;
        _stats = stats;
        _perf = perf;
        engine.Message += OnMessage;
        engine.Crashed += () => Raise();
        _ = TimerLoopAsync(_cts.Token);
    }

    public string Id => SourceKey;
    public string DisplayName => "YouTube Music";
    public PlaybackCapabilities Capabilities => PlaybackCapabilities.Seek | PlaybackCapabilities.Next | PlaybackCapabilities.Previous | PlaybackCapabilities.Volume | PlaybackCapabilities.Shuffle | PlaybackCapabilities.RadioMix | PlaybackCapabilities.Like | PlaybackCapabilities.TrackMenu | PlaybackCapabilities.AddToPlaylist | PlaybackCapabilities.Artist | PlaybackCapabilities.Lyrics;

    public Task ToggleLikeAsync() => RateAsync(Like);

    public Task ToggleDislikeAsync() => RateAsync(Dislike);

    /// <summary>
    /// Setzt die Bewertung auf <paramref name="target"/> oder nimmt sie zurück, wenn sie schon gilt.
    /// Die Anzeige springt sofort um, danach bestätigt die Seite den echten Zustand.
    /// </summary>
    Task RateAsync(string target)
    {
        _rating = _rating == target ? Indifferent : target;
        // 0 = entfernen, 1 = Like, 2 = Dislike (siehe BridgeScript „rate“).
        _engine.Cmd("rate", _rating switch { Like => 1, Dislike => 2, _ => 0 });
        // Den gewünschten Wert festhalten, bis die Seite ihn bestätigt (oder 7 s vergehen). Sonst überschreibt eine Meldung,
        // die den Klick noch nicht kennt, die Anzeige sofort wieder.
        _rateWant = _rating;
        _rateTitle = _state.Title;
        _rateUntil = Environment.TickCount64 + 7000;
        _state = _state with { Liked = _rating == Like, Disliked = _rating == Dislike };
        Raise();
        return Task.CompletedTask;
    }

    /// <summary>Roher Zustand von „Mag ich“ laut Seite (LIKE, INDIFFERENT …). Für die Diagnose.</summary>
    public string LikeRaw { get; private set; } = "";

    /// <summary>
    /// Zufallswiedergabe der App: Sie mischt die gespeicherte Titelliste der laufenden Playlist selbst und lässt die Seite nur den
    /// jeweils nächsten Titel abspielen. Ohne gespeicherte Titelliste (Radio-Mix, Startseite) hat sie keine Wirkung.
    /// </summary>
    public bool Shuffle => _shuffle;

    public bool AppliesSavedShuffle => true;

    /// <summary>Anzahl Titel der Warteschlange für den Zufall (0 = keine gespeicherte Liste). Für die Diagnose.</summary>
    public int QueueCount => _queue.Count;

    /// <summary>Schaltet den Zufall um. Der laufende Titel spielt weiter, danach folgt ein gemischter.</summary>
    public Task SetShuffleAsync(bool on)
    {
        _shuffle = on;
        if (on)
        {
            EnsureQueue(_state.Item?.Id, force: true);
            SyncOrder(UrlNormalizer.VideoId(_state.Item?.Id));
            _expect = UrlNormalizer.VideoId(_state.Item?.Id) ?? "";
        }
        _state = _state with { Shuffle = on };
        Raise();
        return Task.CompletedTask;
    }

    /// <summary>Lädt die gespeicherten Titel der Playlist aus der Adresse (nur bei neuer Playlist oder mit <paramref name="force"/>).</summary>
    void EnsureQueue(string? url, bool force = false)
    {
        var list = UrlNormalizer.PlaylistId(url);
        if (list == null) { _queue = []; _queueList = null; _order.Start(0, -1); return; }
        if (!force && list == _queueList) return;
        if (list != _queueList) _order.Start(0, -1);
        _queueList = list;
        _queue = _store.Load<YtTracksCache>(YtLibrary.TracksSection(list)).Items;
    }

    /// <summary>Die gespeicherte Titelliste dieser Playlist wurde geändert (Titel hinzugefügt oder entfernt). Die Warteschlange des Zufalls wird neu geladen.</summary>
    public void QueueChanged(string playlistId)
    {
        if (_queueList != playlistId) return;
        EnsureQueue(_state.Item?.Id, force: true);
        if (_shuffle) { var vid = UrlNormalizer.VideoId(_state.Item?.Id); _order.Start(_queue.Count, vid == null ? -1 : _queue.FindIndex(t => t.Id == vid)); }
    }

    /// <summary>Stellt die gemischte Reihenfolge auf den Titel <paramref name="videoId"/> ein, falls sie nicht schon dort steht.</summary>
    void SyncOrder(string? videoId)
    {
        var idx = videoId == null ? -1 : _queue.FindIndex(t => t.Id == videoId);
        if (idx >= 0 && _order.Current >= 0 && _order.Current < _queue.Count && _queue[_order.Current].Id == videoId) return;
        _order.Start(_queue.Count, idx);
    }

    /// <summary>Spielt den Titel mit dem Index <paramref name="index"/> der Warteschlange (innerhalb seiner Playlist).</summary>
    Task PlayQueueAsync(int index)
    {
        _expect = _queue[index].Id;
        return PlayUrlAsync(UrlNormalizer.WatchTrack(_expect, _queueList!), 0, true);
    }

    bool CanShuffleQueue => _shuffle && _queue.Count > 1 && _queueList != null;

    /// <summary>
    /// Ohne Zufall: Index des laufenden Titels in der gespeicherten Playlist (die App geht dann selbst Titel für Titel durch,
    /// statt die Seite weiterzuschalten, deren eigene Warteschlange gemischt sein kann). -1, wenn es keine passende Liste gibt.
    /// </summary>
    int SequentialIndex()
    {
        if (_shuffle || _queueList == null || _queue.Count < 2 || UrlNormalizer.PlaylistId(_state.Item?.Id) != _queueList) return -1;
        var vid = UrlNormalizer.VideoId(_state.Item?.Id);
        return vid == null ? -1 : _queue.FindIndex(t => t.Id == vid);
    }

    /// <summary>Gibt es einen nächsten Titel, den die App selbst spielen kann (Zufall oder Reihenfolge der Playlist)?</summary>
    bool CanAdvance => CanShuffleQueue || SequentialIndex() is var i and >= 0 && i + 1 < _queue.Count;

    async Task PlayNextShuffledAsync()
    {
        _advancing = true;
        try
        {
            if (CanShuffleQueue)
            {
                SyncOrder(UrlNormalizer.VideoId(_state.Item?.Id));
                var i = _order.Next();
                if (i >= 0) await PlayQueueAsync(i);
            }
            else if (SequentialIndex() is var si and >= 0 && si + 1 < _queue.Count) await PlayQueueAsync(si + 1);
        }
        catch { }
        finally { _advancing = false; }
    }

    /// <summary>Startet den Radio-Mix zum aktuellen Titel (Seite lädt die automatische Playlist „RDAMVM…“).</summary>
    public Task StartRadioMixAsync() =>
        UrlNormalizer.VideoId(_state.Item?.Id) is { } id ? PlayUrlAsync(UrlNormalizer.RadioMix(id), 0, true) : Task.CompletedTask;
    public PlaybackState State => _state;
    public bool MemoryLow => _low;

    /// <summary>Wie oft die Seite versucht hat, die Lautstärke zu ändern (wir halten sie fest). Für die Diagnose.</summary>
    public int GuardHits { get; private set; }

    /// <summary>Der zuletzt von der Seite gewünschte Lautstärke-Wert (0–1 als Text).</summary>
    public string GuardLast { get; private set; } = "";

    /// <summary>Wie oft die Seite stumm oder laut geschaltet wurde. Für die Diagnose.</summary>
    public int MuteChanges { get; private set; }

    /// <summary>Seeks, die nicht von uns kamen (ein Sprung zurück ist als Knacken hörbar). Für die Diagnose.</summary>
    public int Seeks { get; private set; }

    /// <summary>Länge laut Medien-Element der Seite (Sekunden). Kann bei Streams vom echten Wert abweichen. Für die Diagnose.</summary>
    public double StreamDuration { get; private set; }

    /// <summary>Länge laut Leiste von YouTube Music (Sekunden, 0 = nicht gefunden). Wird für die Anzeige bevorzugt.</summary>
    public double BarDuration { get; private set; }

    /// <summary>Wie oft der Player wegen leerem Puffer warten musste.</summary>
    public int Waits { get; private set; }

    /// <summary>Wie oft das Medium neu geladen wurde (neuer Titel oder Stream-Wechsel).</summary>
    public int Loads { get; private set; }

    /// <summary>Wie oft der Song-Modus auf „Song“ geklickt hat.</summary>
    public int SongClicks { get; private set; }

    /// <summary>Wahr, solange die Web-Ansicht für Wiedergabe gebraucht wird (spielt oder startet gerade). Dann darf niemand sie wegnavigieren.</summary>
    public bool InUse => _needInit || _state.IsPlaying;
    public event Action<PlaybackState>? StateChanged;

    public Task StartAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
    {
        EnsureQueue(item.Id, force: true);
        if (CanShuffleQueue)
        {
            // Mit Zufall: gewählter Titel zuerst, bei ganzer Playlist ein zufälliger. Danach mischt die App.
            var vid = UrlNormalizer.VideoId(item.Id);
            _order.Start(_queue.Count, vid == null ? -1 : _queue.FindIndex(t => t.Id == vid));
            return PlayQueueAsync(_order.Current);
        }
        _expect = UrlNormalizer.VideoId(item.Id) ?? "";
        return PlayUrlAsync(item.Id, 0, true);
    }

    public Task StartAtAsync(MediaItem item, TimeSpan position, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
    {
        EnsureQueue(item.Id, force: true);
        _expect = UrlNormalizer.VideoId(item.Id) ?? "";
        if (CanShuffleQueue) SyncOrder(_expect);
        return PlayUrlAsync(item.Id, position.TotalSeconds, true);
    }

    /// <summary>Lädt eine Adresse. Mit <paramref name="seek"/> und <paramref name="play"/> setzt der Player nach dem Laden dort fort.</summary>
    public async Task PlayUrlAsync(string url, double seek = 0, bool play = false)
    {
        using var scope = _perf.Begin("Yt.Start");
        try
        {
            if (_engine.LoginMode) _engine.SetLoginMode(false);
            _pendingSeek = seek;
            _playAfterInit = play;
            _needInit = true;
            _idleSec = 0;
            SetLow(false);
            await _engine.StartAsync(url);
        }
        catch
        {
            scope.Fail();
            throw;
        }
    }

    public Task ResumeAsync() =>
        PlayUrlAsync(string.IsNullOrEmpty(_s.LastUrl) ? UrlNormalizer.Home : _s.LastUrl, _s.LastPos, true);

    public Task PlayAsync()
    {
        _idleSec = 0;
        if (!_engine.IsRunning || _state.Duration <= TimeSpan.Zero) return ResumeAsync();
        _engine.Cmd("play");
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        _engine.Cmd("pause");
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        _engine.Cmd("seek", position.TotalSeconds);
        return Task.CompletedTask;
    }

    /// <summary>Setzt den Mixer-Eintrag der WebView auf den Reglerwert. Gibt es ihn noch nicht, klappt es beim nächsten Abgleich.</summary>
    void ApplyAudio()
    {
        if (_audio == null) return;
        _audio.Set(_volume);
    }

    /// <summary>
    /// Gleicht (höchstens einmal pro Sekunde) mit dem Mixer ab: Hat dort jemand die Lautstärke geändert, übernimmt der Regler sie.
    /// Direkt nach einer Änderung am Regler zählt der Mixer-Wert nicht (Windows braucht kurz).
    /// </summary>
    void SyncAudio()
    {
        if (_audio == null) return;
        var now = Environment.TickCount64;
        if (now - _volumePollTick < 1000 || now - _volumeSetTick < 1500) return;
        _volumePollTick = now;
        var actual = _audio.Get();
        if (actual == null) { ApplyAudio(); return; }
        if (Math.Abs(actual.Value - _volume) <= 0.01) return;
        _volume = actual.Value;
        _s.Gain = _volume;
        VolumeChangedExternally?.Invoke(_volume);
    }

    public Task SetVolumeAsync(double volume)
    {
        _volume = volume;
        _s.Gain = volume;
        _volumeSetTick = Environment.TickCount64;
        ApplyAudio();
        if (_engine.IsRunning) _engine.Cmd("volume", 1.0);
        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        AppLog.Info("YouTube", $"Weiter: Zufall={_shuffle}, Liste={(_queueList ?? "-")}, Titel in Liste={_queue.Count}, Seitenliste={(UrlNormalizer.PlaylistId(_state.Item?.Id) ?? "-")}, Index={SequentialIndex()}");
        if (CanAdvance) return PlayNextShuffledAsync();
        _engine.Cmd("next");
        return Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        if (CanShuffleQueue)
        {
            // Wie üblich: nach den ersten Sekunden springt „Zurück“ an den Anfang, sonst zum vorigen Titel.
            SyncOrder(UrlNormalizer.VideoId(_state.Item?.Id));
            var i = _state.Position.TotalSeconds > 3 ? -1 : _order.Previous();
            if (i >= 0) return PlayQueueAsync(i);
            _engine.Cmd("seek", 0);
            return Task.CompletedTask;
        }
        if (SequentialIndex() is var si and >= 0)
        {
            // Wie üblich: nach den ersten Sekunden springt „Zurück“ an den Anfang, sonst zum Titel darüber.
            if (_state.Position.TotalSeconds > 3 || si == 0) _engine.Cmd("seek", 0);
            else return PlayQueueAsync(si - 1);
            return Task.CompletedTask;
        }
        _engine.Cmd("prev");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Remember();
        Save();
        _state = PlaybackState.Empty;
        await _engine.StopAsync();
        Raise();
    }

    /// <summary>Beendet die Web-Ansicht und startet sie mit den neuen Einstellungen wieder (merkt sich Titel und Position).</summary>
    public async Task RestartAsync()
    {
        Save();
        if (!_engine.IsRunning) return;
        Remember();
        await _engine.StopAsync();
        await ResumeAsync();
    }

    /// <summary>Setzt die Leerlauf-Zeit zurück (z. B. wenn gerade Playlists gelesen werden).</summary>
    public void Touch()
    {
        _idleSec = 0;
        SetLow(false);
    }

    public void Save() => _store.Save("yt", _s);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await StopAsync();
    }

    void Remember()
    {
        if (_state.Item?.Id.Contains("watch") == true) { _s.LastUrl = _state.Item.Id; _s.LastPos = _state.Position.TotalSeconds; }
    }

    void SetLow(bool low)
    {
        if (_low == low) return;
        _low = low;
        if (_engine.IsRunning) _engine.Host.SetMemoryLow(low);
        if (low && _s.TrimWhenIdle) _ = TrimAsync();
    }

    /// <summary>Sofort nach einer Pause: kurz warten, dann Sparmodus und Trim (nicht erst nach <see cref="YtSettings.IdleLowSeconds"/>).</summary>
    async Task PauseSaveMemoryAsync()
    {
        await Task.Delay(2000);
        if (_state.IsPlaying || !_engine.IsRunning || _engine.LoginMode) return;
        SetLow(true);
    }

    async Task TrimAsync()
    {
        await Task.Delay(3000);
        if (!_low) return;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        TrimAll();
    }

    /// <summary>Leert den Arbeitssatz der App und aller Web-Prozesse.</summary>
    public int TrimAll()
    {
        var ok = _stats.Trim(Environment.ProcessId) ? 1 : 0;
        foreach (var p in _engine.Processes()) if (_stats.Trim(p.Pid)) ok++;
        return ok;
    }

    void OnMessage(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (r.GetProperty("t").GetString() != "state") return;
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            double N(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
            GuardHits = (int)N("vg");
            GuardLast = S("vgLast");
            MuteChanges = (int)N("mu");
            StreamDuration = N("vdur");
            BarDuration = N("pdur");
            Seeks = (int)N("sk");
            Waits = (int)N("wt");
            Loads = (int)N("ld");
            SongClicks = (int)N("sm");
            var pageRating = S("rate") is Like or Dislike ? S("rate") : Indifferent;
            LikeRaw = S("lr");
            CurrentArtistId = S("aid");
            if (_rateWant is { } want && Environment.TickCount64 < _rateUntil && S("title") == _rateTitle && pageRating != want) pageRating = want;
            else _rateWant = null;
            _rating = pageRating;
            if (_rateWant == null) _rateTitle = S("title");
            var wasPlaying = _state.IsPlaying;
            var url = S("url");
            var title = S("title");
            var art = S("art");
            var item = new MediaItem(SourceKey, url, title, S("artist"), S("album"), art.Length > 0 ? art : null);
            var dur = N("dur");
            _state = new PlaybackState(item, title, S("artist"), S("album"), art.Length > 0 ? art : null, null,
                r.GetProperty("playing").GetBoolean(), TimeSpan.FromSeconds(N("pos")), TimeSpan.FromSeconds(dur), _volume,
                UrlNormalizer.ToYoutubeTrackUrl(url), Loc.T("yt.openOnYoutube"), _shuffle, UrlNormalizer.IsRadioMix(url), 0, _rating == Like, _rating == Dislike);
            if (_needInit && dur > 0)
            {
                _needInit = false;
                AppLog.Info("YouTube", $"Titel geladen, springe zu {_pendingSeek:F0} s, abspielen {_playAfterInit}");
                _engine.Cmd("volume", 1.0);
                ApplyAudio();
                if (_pendingSeek > 1) _engine.Cmd("seek", _pendingSeek);
                if (_playAfterInit) _engine.Cmd("play");
                _pendingSeek = 0;
                _playAfterInit = false;
            }
            // Zufall: kurz vor dem Ende des Titels (den wir geladen haben) laden wir selbst den nächsten gemischten, bevor die Seite weiterspringt.
            if (!_needInit && !_advancing && _state.IsPlaying && CanAdvance && dur > 5 && dur - N("pos") <= 1.5 && UrlNormalizer.VideoId(url) == _expect)
                _ = PlayNextShuffledAsync();
            // Die YT-Seite setzt die Lautstärke selbst um (z. B. beim Titelwechsel). Wir halten sie auf dem Wert vom Regler.
            if (!_needInit && r.TryGetProperty("vol", out var pv) && pv.ValueKind == JsonValueKind.Number && Math.Abs(pv.GetDouble() - 1.0) > 0.01)
                _engine.Cmd("volume", 1.0);
            SyncAudio();
            if (_state.IsPlaying) { _idleSec = 0; SetLow(false); }
            if (wasPlaying && !_state.IsPlaying) { Remember(); Save(); _ = PauseSaveMemoryAsync(); }
            Raise();
        }
        catch { }
    }

    async Task TimerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await Dispatcher.UIThread.InvokeAsync(TickAsync);
        }
        catch (OperationCanceledException) { }
    }

    async Task TickAsync()
    {
        if (!_engine.IsRunning || _engine.LoginMode || _state.IsPlaying) return;
        _idleSec += 5;
        if (_s.IdleLowSeconds > 0 && _idleSec >= _s.IdleLowSeconds) SetLow(true);
        if (_s.IdleDisposeMinutes > 0 && _idleSec >= _s.IdleDisposeMinutes * 60)
        {
            Remember();
            Save();
            await _engine.StopAsync();
            _low = false;
            Raise();
        }
    }

    void Raise() => StateChanged?.Invoke(_state);
}
