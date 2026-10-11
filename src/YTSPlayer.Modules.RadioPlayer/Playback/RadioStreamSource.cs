using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Fledgr.Audio;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Modules.RadioPlayer.Data;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.RadioPlayer.Playback;

/// <summary>
/// Internet-Radio (Live-Streams) über den gemeinsamen Audio-Motor. Pause stoppt den Stream, Play verbindet neu.
/// Kommt ein Stream nicht zustande oder bricht ab, fragt die Quelle die Datenbank nach dem Sender:
/// neue Adresse → neu versuchen, nicht mehr da → „Sender weg“, sonst „Sender tot“.
/// </summary>
public sealed class RadioStreamSource : IPlaybackSource, YTSPlayer.Contracts.Playback.IExternalVolumeSource, YTSPlayer.Ui.IFavoritesProvider
{
    public const string SourceKey = "radio";
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    readonly LibVlcEngine _engine;
    readonly RadioStore _store;
    readonly RadioBrowserClient _client;
    readonly IPerfTracker _perf;
    readonly object _lock = new();
    List<MediaItem> _queue = [];
    int _index = -1;
    int _generation;
    int _playingGeneration = -1;
    int _handledGeneration = -1;
    double _volume = 1;
    // Titel aus dem Stream (ICY), meist „Künstler - Titel“.
    string? _songArtist;
    string? _songTitle;
    string? _message;
    PlaybackState _state = PlaybackState.Empty;

    public RadioStreamSource(LibVlcEngine engine, RadioStore store, RadioBrowserClient client, IPerfTracker perf)
    {
        _engine = engine;
        _store = store;
        _client = client;
        _perf = perf;
        _engine.VolumeChangedExternally += gain => { _volume = gain; VolumeChangedExternally?.Invoke(gain); };
        _engine.Playing += OnPlaying;
        _engine.NowPlayingChanged += OnNowPlaying;
        _engine.Error += OnStreamEnded;
        _engine.EndReached += OnStreamEnded;
    }

    /// <summary>Der Status eines gemerkten Senders hat sich geändert (weg, tot, neue Adresse). Kommt aus einem Hintergrund-Thread.</summary>
    public event Action? StatusChanged;

    public string Id => SourceKey;
    public string DisplayName => Loc.T("radio.nav");
    public PlaybackCapabilities Capabilities => PlaybackCapabilities.Volume | PlaybackCapabilities.Next | PlaybackCapabilities.Previous;
    public PlaybackState State => _state;
    public event Action<PlaybackState>? StateChanged;
    public event Action<double>? VolumeChangedExternally;

    /// <summary>Der Sender als abspielbarer Eintrag. Die Stream-Adresse ist die Id, damit das Wiederherstellen nach einem Neustart ohne Suche geht.</summary>
    public static MediaItem ToItem(RadioStation station) =>
        new(SourceKey, station.StreamUrl, station.Name, station.Country, station.TechnicalInfo, station.LogoUrl);

    public Task StartAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
    {
        List<MediaItem> list = queue is { Count: > 0 } ? queue.ToList() : [item];
        var index = list.FindIndex(q => q.Id == item.Id);
        if (index < 0) { list.Insert(0, item); index = 0; }
        lock (_lock) _queue = list;
        return PlayIndexAsync(index, retried: false, ct);
    }

    public async Task PlayAsync()
    {
        int index;
        lock (_lock) index = _index;
        if (index < 0 || _engine.IsPlaying) return;
        await PlayIndexAsync(index, retried: false);
    }

    /// <summary>Stoppt den Stream. Der Sender bleibt angezeigt, Play verbindet neu.</summary>
    public async Task PauseAsync()
    {
        Interlocked.Increment(ref _generation);
        await _engine.ReleaseAsync();
        Publish(false);
    }

    public Task SeekAsync(TimeSpan position) => Task.CompletedTask;

    /// <summary>
    /// Die Lautstärke gilt für diese Quelle. Die Shell setzt sie beim Aktivieren aus dem Speicher der Quelle,
    /// deshalb nichts anzeigen, solange noch kein Sender geladen ist (sonst leert sich die Player-Leiste).
    /// </summary>
    public Task SetVolumeAsync(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _engine.SetGain(_volume);
        bool loaded;
        lock (_lock) loaded = _index >= 0;
        if (loaded) Publish(_state.IsPlaying);
        return Task.CompletedTask;
    }

    public Task NextAsync() => StepAsync(1);

    public Task PreviousAsync() => StepAsync(-1);

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        lock (_lock)
        {
            _queue = [];
            _index = -1;
            _message = null;
            _songTitle = null; _songArtist = null;
        }
        await _engine.ReleaseAsync();
        Raise(PlaybackState.Empty);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public bool HasFavorites => _store.Favorites.Count > 0;

    /// <summary>Zeigt die Favoriten als Menü. Der laufende Sender hat ein Häkchen, ein Klick spielt den Sender (die Favoriten sind die Warteschlange).</summary>
    public void ShowFavorites(Avalonia.Controls.Control anchor)
    {
        var favorites = _store.Favorites;
        if (favorites.Count == 0) return;
        string? current;
        lock (_lock) current = _index >= 0 && _index < _queue.Count ? _queue[_index].Id : null;
        var items = favorites.Select(s => ToItem(ToStation(s))).ToList();
        var menu = new Avalonia.Controls.MenuFlyout { ShowMode = Avalonia.Controls.FlyoutShowMode.Transient };
        var head = new Avalonia.Controls.MenuItem { Header = ShadowText(Loc.T("radio.view.favorites").ToUpperInvariant()) };
        head.Classes.Add("head");
        menu.Items.Add(head);
        foreach (var s in favorites)
        {
            var station = ToStation(s);
            var selected = s.StreamUrl == current;
            var item = new Avalonia.Controls.MenuItem
            {
                Header = FavoriteRow(s.Name, FirstTag(s.Tags)),
                Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() =>
                {
                    _store.MarkPlayed(station);
                    return StartAsync(ToItem(station), items);
                }),
            };
            item.Classes.Add("fav");
            if (selected) item.Classes.Add("current");
            menu.Items.Add(item);
        }
        YTSPlayer.Ui.Controls.PopupExtensions.ShowAbove(menu, anchor);
    }

    /// <summary>Zeile im Favoriten-Menü (Vorlage widgets-v13): Name links, Genre rechts.</summary>
    static Avalonia.Controls.Control FavoriteRow(string name, string genre)
    {
        var row = new Avalonia.Controls.Grid { ColumnDefinitions = new("*,Auto") };
        row.Bind(Avalonia.Layout.Layoutable.MinWidthProperty, row.GetResourceObservable("Size.MenuRow"));
        row.Bind(Avalonia.Controls.Grid.ColumnSpacingProperty, row.GetResourceObservable("Space.S"));
        var title = new Avalonia.Controls.TextBlock { Text = name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        title.Classes.Add("shadow");
        row.Children.Add(title);
        var tag = new Avalonia.Controls.TextBlock { Text = genre, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        tag.Classes.Add("tiny");
        tag.Classes.Add("muted");
        tag.Classes.Add("shadow");
        Avalonia.Controls.Grid.SetColumn(tag, 1);
        row.Children.Add(tag);
        return row;
    }

    /// <summary>Text mit Schlagschatten (wie die Texte im Widget), für Kopfzeilen von Menüs.</summary>
    static Avalonia.Controls.TextBlock ShadowText(string text)
    {
        var block = new Avalonia.Controls.TextBlock { Text = text };
        block.Classes.Add("shadow");
        return block;
    }

    /// <summary>Erstes Genre aus den Tags („pop,rock“ → „Pop“).</summary>
    static string FirstTag(string tags)
    {
        var first = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return first.Length == 0 ? "" : char.ToUpperInvariant(first[0]) + first[1..];
    }

    static RadioStation ToStation(SavedStation s) => new(s.Id, s.Name, s.StreamUrl, s.Country, s.Tags, s.Codec, s.Bitrate, s.LogoUrl);

    int VolumePercent => (int)Math.Round(_volume * 100);

    Task StepAsync(int step)
    {
        int target, count;
        lock (_lock)
        {
            target = _index + step;
            count = _queue.Count;
        }
        // Am Ende der Liste (Favoriten, Verlauf, Suchtreffer) geht es vorn weiter und umgekehrt.
        if (count > 1) target = (target % count + count) % count;
        return target >= 0 && target < count ? PlayIndexAsync(target, retried: false) : Task.CompletedTask;
    }

    async Task PlayIndexAsync(int index, bool retried, CancellationToken ct = default)
    {
        using var scope = _perf.Begin("Radio.Start");
        var gen = Interlocked.Increment(ref _generation);
        try
        {
            string url;
            lock (_lock)
            {
                _index = index;
                url = _queue[index].Id;
                _message = null;
                _songTitle = null; _songArtist = null;
            }
            Publish(true);
            await Task.Run(() =>
            {
                _engine.SetGain(_volume);
                return _engine.PlayStreamAsync(url, ct);
            }, ct);
            _ = WatchdogAsync(gen, retried);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            scope.Fail();
            await FailAsync(gen, Loc.T("radio.status.playFailed"), statusChanged: false);
        }
    }

    void OnPlaying()
    {
        var gen = Volatile.Read(ref _generation);
        Volatile.Write(ref _playingGeneration, gen);
        string? url;
        lock (_lock)
        {
            _message = null;
            url = CurrentUrl();
        }
        // Läuft der Stream wieder, gilt der Sender als in Ordnung.
        if (url != null && _store.Find(url) is { Status: not StationStatus.Ok })
        {
            _store.SetStatus(url, StationStatus.Ok);
            StatusChanged?.Invoke();
        }
        Publish(true);
    }

    void OnNowPlaying(string text)
    {
        // Der ICY-Text ist meist „Künstler - Titel“. Ohne Trennzeichen steht nur der Titel da.
        var sep = text.IndexOf(" - ", StringComparison.Ordinal);
        lock (_lock)
        {
            _songArtist = sep > 0 ? text[..sep].Trim() : null;
            _songTitle = sep > 0 ? text[(sep + 3)..].Trim() : text;
        }
        Publish(_state.IsPlaying);
    }

    void OnStreamEnded()
    {
        // Aus dem LibVLC-Ereignis heraus nie direkt auf den Player zugreifen. Deshalb eigener Task.
        var gen = Volatile.Read(ref _generation);
        // Riss ein laufender Stream ab (kurzer Netz-Aussetzer), verbindet die App still neu, statt den Sender gleich als tot zu melden.
        if (Volatile.Read(ref _playingGeneration) == gen && TryReserveReconnect())
        {
            _ = Task.Run(() => ReconnectAsync(gen));
            return;
        }
        _ = Task.Run(() => HandleFailureAsync(gen, retried: false));
    }

    static readonly TimeSpan ReconnectWindow = TimeSpan.FromMinutes(2);
    const int MaxReconnects = 3;
    readonly Queue<DateTime> _reconnects = new();

    /// <summary>Höchstens drei stille Neuverbindungen in zwei Minuten, danach gilt der Abbruch als Fehler.</summary>
    bool TryReserveReconnect()
    {
        lock (_reconnects)
        {
            var now = DateTime.UtcNow;
            while (_reconnects.Count > 0 && now - _reconnects.Peek() > ReconnectWindow) _reconnects.Dequeue();
            if (_reconnects.Count >= MaxReconnects) return false;
            _reconnects.Enqueue(now);
            return true;
        }
    }

    async Task ReconnectAsync(int gen)
    {
        try
        {
            await Task.Delay(500);
            int index;
            lock (_lock) index = _index;
            if (gen != Volatile.Read(ref _generation) || index < 0) return;
            await PlayIndexAsync(index, retried: false);
        }
        catch (Exception)
        {
            // Klappt die Neuverbindung nicht, meldet der Wächter den Ausfall wie bei einem normalen Start.
        }
    }

    /// <summary>Wartet auf den ersten Ton. Kommt keiner, gilt der Stream als ausgefallen.</summary>
    async Task WatchdogAsync(int gen, bool retried)
    {
        await Task.Delay(ConnectTimeout);
        if (gen != Volatile.Read(ref _generation) || Volatile.Read(ref _playingGeneration) == gen) return;
        await HandleFailureAsync(gen, retried);
    }

    async Task HandleFailureAsync(int gen, bool retried)
    {
        // Ein Fehler je Wiedergabe. Meldet LibVLC Fehler und Ende, wird nur einmal geprüft.
        if (Interlocked.Exchange(ref _handledGeneration, gen) == gen) return;
        try
        {
            MediaItem? item;
            int index;
            lock (_lock)
            {
                index = _index;
                item = index >= 0 && index < _queue.Count ? _queue[index] : null;
            }
            if (item == null || gen != Volatile.Read(ref _generation)) return;

            var saved = _store.Find(item.Id);
            if (saved != null && RadioIds.IsDatabase(saved.Id) && !retried)
            {
                // Die Datenbank-Anfrage kann dauern. Bis dahin zeigt die Leiste, dass der Sender geprüft wird.
                lock (_lock) _message = Loc.T("radio.status.checking");
                Publish(false);

                RadioStation? fresh;
                try
                {
                    fresh = await _client.FindAsync(saved.Id, CancellationToken.None);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // Ohne Netz ist nicht klar, ob der Sender tot ist. Deshalb kein Status.
                    await FailAsync(gen, Loc.T("radio.status.noDb"), statusChanged: false);
                    return;
                }
                if (gen != Volatile.Read(ref _generation)) return;

                if (fresh == null)
                {
                    _store.SetStatus(item.Id, StationStatus.Gone);
                    await FailAsync(gen, Loc.T("radio.status.gone"), statusChanged: true);
                    return;
                }
                if (fresh.StreamUrl != item.Id)
                {
                    _store.ReplaceUrl(item.Id, fresh.StreamUrl);
                    lock (_lock) _queue[index] = item with { Id = fresh.StreamUrl };
                    StatusChanged?.Invoke();
                    await PlayIndexAsync(index, retried: true);
                    return;
                }
            }

            if (saved != null) _store.SetStatus(item.Id, StationStatus.Dead);
            await FailAsync(gen, saved != null ? Loc.T("radio.status.dead") : Loc.T("radio.status.noResponse"), statusChanged: saved != null);
        }
        catch (Exception)
        {
            // Die Fehlerbehandlung darf nie selbst abstürzen. Die Anzeige bleibt dann wie sie ist.
        }
    }

    async Task FailAsync(int gen, string message, bool statusChanged)
    {
        if (gen != Volatile.Read(ref _generation)) return;
        await _engine.ReleaseAsync();
        if (gen != Volatile.Read(ref _generation)) return;
        lock (_lock) _message = message;
        Publish(false);
        if (statusChanged) StatusChanged?.Invoke();
    }

    /// <summary>Die Adresse des aktuellen Senders. Nur mit gehaltenem Schloss aufrufen.</summary>
    string? CurrentUrl() => _index >= 0 && _index < _queue.Count ? _queue[_index].Id : null;

    /// <summary>
    /// Zeigt den Sender. Läuft ein Titel im Stream, steht er vorn und der Sender dahinter. Ein Fehlertext ersetzt den Interpreten.
    /// </summary>
    void Publish(bool playing)
    {
        PlaybackState state;
        lock (_lock)
        {
            if (_index < 0 || _index >= _queue.Count)
            {
                state = PlaybackState.Empty;
            }
            else
            {
                var item = _queue[_index];
                // Läuft ein Titel aus dem Stream, steht der Sendername im Album. Sonst Sendername und Land wie bisher.
                var song = _songTitle != null;
                var artist = _message ?? (song ? _songArtist ?? item.Title : item.Artist);
                var album = song ? item.Title : item.Album;
                state = new PlaybackState(item, _songTitle ?? item.Title, artist, album, item.CoverUrl, null, playing, TimeSpan.Zero, TimeSpan.Zero, _volume);
            }
        }
        Raise(state);
    }

    void Raise(PlaybackState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
