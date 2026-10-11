using Fledgr.Audio;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.MusicPlayer.Playback;

/// <summary>
/// Spielt lokale Dateien über den gemeinsamen Audio-Motor (ohne Video). Der Motor wird erst beim ersten Abspielen geladen
/// und beim Stoppen wieder freigegeben, damit im Leerlauf kein Speicher belegt bleibt.
/// </summary>
public sealed class LibVlcSource : IPlaybackSource, YTSPlayer.Contracts.Playback.IExternalVolumeSource, IStartAtSource, IShuffleSource, IQueueSource, ILyricsSource
{
    /// <summary>
    /// Songtext des laufenden Titels: erst aus den Tags der Datei, sonst aus einer Datei gleichen Namens (.lrc oder .txt).
    /// Zeitmarken wie [01:23.45] einer .lrc werden entfernt.
    /// </summary>
    public Task<LyricsResult?> GetLyricsAsync(CancellationToken ct)
    {
        var path = _index >= 0 && _index < _queue.Count ? _queue[_index].Id : null;
        if (path == null) return Task.FromResult<LyricsResult?>(null);
        return Task.Run(() => ReadLyrics(path), ct);
    }

    static LyricsResult? ReadLyrics(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path, TagLib.ReadStyle.None);
            if (!string.IsNullOrWhiteSpace(f.Tag.Lyrics)) return new LyricsResult(f.Tag.Lyrics.Trim(), Loc.T("music.lyrics.tags"));
        }
        catch (Exception)
        {
            // Keine lesbaren Tags: weiter mit der Textdatei.
        }
        foreach (var ext in new[] { ".lrc", ".txt" })
        {
            try
            {
                var side = Path.ChangeExtension(path, ext);
                if (!File.Exists(side) || new FileInfo(side).Length > 200_000) continue;
                var text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(side), @"\[\d{1,2}:\d{2}(?:[.:]\d{1,3})?\]", "");
                // Kopfzeilen einer .lrc ([ar:…], [ti:…]) sind keine Textzeilen.
                text = System.Text.RegularExpressions.Regex.Replace(text, @"^\[[a-zA-Z]+:[^\]]*\]\s*$", "", System.Text.RegularExpressions.RegexOptions.Multiline).Trim();
                if (text.Length > 0) return new LyricsResult(text, "Aus " + Path.GetFileName(side));
            }
            catch (Exception)
            {
                // Datei nicht lesbar: ohne Songtext weiter.
            }
        }
        return null;
    }

    TimeSpan _startAt;
    readonly ShuffleOrder _shuffle = new();
    bool _shuffleOn;
    readonly IPerfTracker _perf;
    readonly LibVlcEngine _engine;

    public const string SourceKey = "local";

    readonly object _lock = new();
    List<MediaItem> _queue = [];
    int _index = -1;
    double _volume = 1;
    PlaybackState _state = PlaybackState.Empty;
    /// <summary>Länge des laufenden Titels, sobald LibVLC sie meldet (sonst 0).</summary>
    TimeSpan _length;
    long _lastReport;
    int _generation;

    public LibVlcSource(IPerfTracker perf, Func<string?>? libVlcDirectory = null)
    {
        _perf = perf;
        _engine = new LibVlcEngine(libVlcDirectory);
        _engine.VolumeChangedExternally += gain => { _volume = gain; VolumeChangedExternally?.Invoke(gain); };
        _engine.Playing += () => Update(playing: true);
        _engine.Paused += () => Update(playing: false);
        _engine.TimeChanged += Tick;
        _engine.LengthChanged += UpdateLength;
        _engine.EndReached += () => _ = Task.Run(OnEndedAsync);
        _engine.Error += () => _ = Task.Run(OnEndedAsync);
    }

    public string Id => SourceKey;
    public string DisplayName => Loc.T("music.source");
    public PlaybackCapabilities Capabilities => PlaybackCapabilities.Seek | PlaybackCapabilities.Next | PlaybackCapabilities.Previous | PlaybackCapabilities.Volume | PlaybackCapabilities.Shuffle | PlaybackCapabilities.Lyrics;
    public PlaybackState State => _state;
    public event Action<PlaybackState>? StateChanged;
    public event Action<double>? VolumeChangedExternally;

    public async Task StartAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
    {
        _queue = queue is { Count: > 0 } ? queue.ToList() : [item];
        _index = _queue.FindIndex(q => q.Id == item.Id);
        if (_index < 0) { _queue.Insert(0, item); _index = 0; }
        if (_shuffleOn) _shuffle.Start(_queue.Count, _index);
        await PlayIndexAsync(_index, ct);
    }

    /// <summary>Startet den Titel direkt bei <paramref name="position"/> (LibVLC-Option start-time, kein Anspielen von vorn).</summary>
    public Task StartAtAsync(MediaItem item, TimeSpan position, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
    {
        _startAt = position;
        return StartAsync(item, queue, ct);
    }

    public IReadOnlyList<MediaItem> Queue => _queue;

    public Task PlayAsync()
    {
        if (!_engine.IsLoaded) return _index >= 0 ? PlayIndexAsync(_index, CancellationToken.None) : Task.CompletedTask;
        if (!_engine.IsPlaying) _engine.SetPaused(false);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (_engine.IsPlaying) _engine.SetPaused(true);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        if (_engine.IsSeekable) _engine.TimeMs = (long)position.TotalMilliseconds;
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _engine.SetGain(_volume);
        return Task.CompletedTask;
    }

    int VolumePercent => (int)Math.Round(_volume * 100);

    public bool Shuffle => _shuffleOn;

    public bool AppliesSavedShuffle => true;

    /// <summary>Schaltet die Zufallswiedergabe an oder aus. Beim Einschalten beginnt ein neuer Durchgang mit dem aktuellen Titel.</summary>
    public Task SetShuffleAsync(bool on)
    {
        _shuffleOn = on;
        if (on && _index >= 0 && _queue.Count > 0) _shuffle.Start(_queue.Count, _index);
        lock (_lock) _state = _state with { Shuffle = on };
        Raise();
        return Task.CompletedTask;
    }

    /// <summary>Der nächste Index: bei Zufall aus dem gemischten Durchgang (nie zweimal derselbe hintereinander), sonst der folgende. -1 am Ende.</summary>
    int NextIndex()
    {
        if (_queue.Count == 0) return -1;
        if (_shuffleOn) return _shuffle.Next();
        return _index + 1 < _queue.Count ? _index + 1 : -1;
    }

    public async Task NextAsync()
    {
        var next = NextIndex();
        if (next >= 0) await PlayIndexAsync(next, CancellationToken.None);
    }

    public async Task PreviousAsync()
    {
        if (_engine.TimeMs > 3000) { _engine.TimeMs = 0; return; }
        var prev = _shuffleOn ? _shuffle.Previous() : _index > 0 ? _index - 1 : -1;
        if (prev >= 0) await PlayIndexAsync(prev, CancellationToken.None);
        else _engine.TimeMs = 0;
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        await _engine.ReleaseAsync();
        _queue = [];
        _index = -1;
        Publish(PlaybackState.Empty);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    async Task PlayIndexAsync(int index, CancellationToken ct)
    {
        using var scope = _perf.Begin("Local.Start");
        try
        {
            var gen = Interlocked.Increment(ref _generation);
            _index = index;
            var item = _queue[index];
            var startAt = _startAt;
            _startAt = TimeSpan.Zero;
            // Die Länge gehört zum neuen Titel. Die alte darf nicht stehen bleiben.
            lock (_lock) _length = TimeSpan.Zero;
            await Task.Run(() =>
            {
                _engine.SetGain(_volume);
                _engine.Play(item.Id, isUrl: false, startAt: startAt);
            }, ct);
            // Die Länge kann schon vor diesem Schritt gemeldet worden sein (eigener Thread). Deshalb aus _length nehmen, nicht auf 0 setzen.
            lock (_lock)
                _state = new PlaybackState(item, item.Title, item.Artist, item.Album, null, null, true, TimeSpan.Zero, _length, _volume, Shuffle: _shuffleOn);
            Raise();
            _ = LoadTagsAsync(item, gen);
        }
        catch
        {
            scope.Fail();
            throw;
        }
    }

    async Task OnEndedAsync()
    {
        // Aus dem LibVLC-Ereignis heraus nie direkt auf den Player zugreifen. Deshalb eigener Task.
        var next = NextIndex();
        if (next >= 0) await PlayIndexAsync(next, CancellationToken.None);
        else Update(playing: false, ended: true);
    }

    async Task LoadTagsAsync(MediaItem item, int gen)
    {
        var meta = await Task.Run(() => ReadTags(item.Id));
        if (meta == null || gen != _generation) return;
        lock (_lock)
        {
            _state = _state with
            {
                Title = string.IsNullOrWhiteSpace(meta.Title) ? _state.Title : meta.Title,
                Artist = meta.Artist,
                Album = meta.Album,
                Year = meta.Year,
                CoverBytes = meta.Cover,
            };
        }
        Raise();
    }

    sealed record Tags(string Title, string Artist, string Album, int Year, byte[]? Cover);

    static Tags? ReadTags(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var pic = f.Tag.Pictures.Length > 0 ? f.Tag.Pictures[0].Data.Data : null;
            if (pic is { Length: > 3_000_000 }) pic = null;
            return new Tags(f.Tag.Title ?? "", f.Tag.FirstPerformer ?? "", f.Tag.Album ?? "", f.Tag.Year is > 0 and < 10000 ? (int)f.Tag.Year : 0, pic);
        }
        catch
        {
            return null;
        }
    }

    void Update(bool playing, bool ended = false)
    {
        lock (_lock)
            _state = _state with { IsPlaying = playing, Position = ended ? TimeSpan.Zero : _state.Position };
        Raise();
    }

    void UpdateLength(TimeSpan length)
    {
        lock (_lock)
        {
            _length = length;
            _state = _state with { Duration = length };
        }
        Raise();
    }

    void Tick(TimeSpan position)
    {
        var now = Environment.TickCount64;
        if (now - _lastReport < 400) return;
        _lastReport = now;
        lock (_lock) _state = _state with { Position = position };
        Raise();
    }

    void Publish(PlaybackState st)
    {
        lock (_lock) _state = st;
        Raise();
    }

    void Raise() => StateChanged?.Invoke(_state);
}
