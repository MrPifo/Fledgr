using Avalonia.Threading;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Die eine Player-Schnittstelle der App. Hält genau eine aktive Quelle. Beim Wechsel stoppt die alte Quelle zuerst.
/// </summary>
public sealed class PlayerService(AppSettings settings, IPerfTracker perf) : IPlayer
{
    readonly Dictionary<string, IPlaybackSource> _sources = [];
    IPlaybackSource? _active;
    PlaybackState _state = PlaybackState.Empty;
    (MediaItem Item, TimeSpan Position, IReadOnlyList<MediaItem>? Queue)? _pending;

    public PlaybackState State => _state;

    /// <summary>Die aktive Quelle (für optionale Funktionen wie das Titel-Menü).</summary>
    public IPlaybackSource? ActiveSource => _active;
    public PlaybackCapabilities Capabilities => _active?.Capabilities ?? PlaybackCapabilities.None;
    public string? ActiveSourceId => _active?.Id;
    public double Volume { get; private set; } = settings.Volume;

    public event Action<PlaybackState>? StateChanged;
    public event Action? SourceChanged;

    /// <summary>Der Nutzer hat etwas am Player bedient (Abspielen, Pause, Weiter, Spulen, Lautstärke …). Der Sleep-Timer setzt dann seine Zeit zurück.</summary>
    public event Action? UserAction;

    /// <summary>Meldet eine Quelle an. Muss auf dem UI-Thread laufen.</summary>
    public void Register(IPlaybackSource source) => _sources[source.Id] = source;

    /// <summary>Meldet eine Quelle ab. Ist sie aktiv, wird sie gestoppt.</summary>
    public async Task UnregisterAsync(IPlaybackSource source)
    {
        _sources.Remove(source.Id);
        if (_active == source)
        {
            await DeactivateAsync();
            SourceChanged?.Invoke();
        }
    }

    /// <summary>
    /// Stellt nach einem Neustart den zuletzt gespielten Titel wieder her. Mit <paramref name="play"/> startet er sofort an der Position.
    /// Ohne <paramref name="play"/> wird er nur angezeigt und erst beim Drücken auf Play geladen (spart RAM und vermeidet kurzen Ton).
    /// </summary>
    public async Task RestoreAsync(MediaItem item, TimeSpan position, TimeSpan duration, bool play, IReadOnlyList<MediaItem>? queue = null)
    {
        if (!_sources.TryGetValue(item.SourceId, out var source)) return;
        if (play)
        {
            await ActivateAsync(source);
            if (source is IStartAtSource s) await s.StartAtAsync(item, position, queue);
            else await source.StartAsync(item, queue);
            return;
        }
        _pending = (item, position, queue);
        settings.LastSourceId = item.SourceId;
        if (settings.SourceVolumes.TryGetValue(item.SourceId, out var own)) Volume = own;
        SourceChanged?.Invoke();
        Publish(new PlaybackState(item, item.Title, item.Artist, item.Album, item.CoverUrl, null, false, position, duration, Volume));
    }

    public async Task PlayAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
    {
        UserAction?.Invoke();
        _pending = null;
        if (!_sources.TryGetValue(item.SourceId, out var source))
            throw new InvalidOperationException($"Quelle „{item.SourceId}“ ist nicht aktiv. Plugin einschalten.");
        using var scope = perf.Begin("Player.Start");
        try
        {
            await ActivateAsync(source);
            await source.StartAsync(item, queue, ct);
        }
        catch
        {
            scope.Fail();
            throw;
        }
    }

    public async Task TogglePlayAsync()
    {
        UserAction?.Invoke();
        if (_active == null && _pending is { } p)
        {
            // Wiederhergestellter, noch nicht geladener Titel: jetzt laden und an der gemerkten Stelle starten.
            _pending = null;
            if (_sources.TryGetValue(p.Item.SourceId, out var src))
            {
                await ActivateAsync(src);
                if (src is IStartAtSource s) await s.StartAtAsync(p.Item, p.Position, p.Queue);
                else await src.StartAsync(p.Item, p.Queue);
            }
            return;
        }
        if (_active == null)
        {
            if (settings.LastSourceId is { } id && _sources.TryGetValue(id, out var last)) await ActivateAsync(last);
            else return;
        }
        if (_state.IsPlaying) await _active!.PauseAsync();
        else await _active!.PlayAsync();
    }

    /// <summary>Schaltet die Zufallswiedergabe der aktiven Quelle um und merkt sich die Wahl für diese Quelle.</summary>
    public async Task ToggleShuffleAsync()
    {
        UserAction?.Invoke();
        if (_active is not IShuffleSource s) return;
        var on = !s.Shuffle;
        await s.SetShuffleAsync(on);
        settings.SourceShuffle[_active.Id] = on;
    }

    /// <summary>Schaltet „Gefällt mir“ des laufenden Titels um, wenn die aktive Quelle das kann.</summary>
    public Task ToggleLikeAsync() => _active is ILikeSource l ? l.ToggleLikeAsync() : Task.CompletedTask;

    public Task ToggleDislikeAsync() => _active is ILikeSource l ? l.ToggleDislikeAsync() : Task.CompletedTask;

    /// <summary>Startet den Radio-Mix zum aktuellen Titel, wenn die aktive Quelle das kann.</summary>
    public Task StartRadioMixAsync() => _active is IRadioMixSource r ? r.StartRadioMixAsync() : Task.CompletedTask;

    public Task NextAsync()
    {
        UserAction?.Invoke();
        return _active?.NextAsync() ?? Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        UserAction?.Invoke();
        return _active?.PreviousAsync() ?? Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        UserAction?.Invoke();
        return _active?.SeekAsync(position) ?? Task.CompletedTask;
    }

    public async Task SetVolumeAsync(double volume)
    {
        UserAction?.Invoke();
        Volume = Math.Clamp(volume, 0, 1);
        settings.Volume = Volume;
        // Jede Quelle merkt sich ihre eigene Lautstärke (auch eine nur angezeigte, noch nicht geladene).
        var id = _active?.Id ?? _pending?.Item.SourceId ?? settings.LastSourceId;
        if (id != null) settings.SourceVolumes[id] = Volume;
        if (_active != null) await _active.SetVolumeAsync(ToGain(Volume));
    }

    /// <summary>Reglerstellung (0–1) zu Lautstärke. Linear: LibVLC wendet unter Windows selbst schon eine Kubik-Kurve an.</summary>
    static double ToGain(double slider) => slider;

    public async Task StopAsync()
    {
        await DeactivateAsync();
        SourceChanged?.Invoke();
    }

    async Task ActivateAsync(IPlaybackSource source)
    {
        if (_active == source) return;
        await DeactivateAsync();
        _active = source;
        settings.LastSourceId = source.Id;
        // Die Lautstärke dieser Quelle laden (sonst die zuletzt benutzte). Die Oberfläche übernimmt sie über SourceChanged.
        Volume = settings.SourceVolumes.TryGetValue(source.Id, out var own) ? own : Volume;
        source.StateChanged += OnSourceState;
        await source.SetVolumeAsync(ToGain(Volume));
        // Quellen, die den Zufall nicht selbst behalten (lokale Musik), bekommen die gemerkte Wahl zurück.
        if (source is IShuffleSource { AppliesSavedShuffle: true } sh && settings.SourceShuffle.TryGetValue(source.Id, out var shuffle))
            await sh.SetShuffleAsync(shuffle);
        SourceChanged?.Invoke();
    }

    async Task DeactivateAsync()
    {
        var old = _active;
        if (old == null) return;
        _active = null;
        old.StateChanged -= OnSourceState;
        try { await old.StopAsync(); }
        catch { }
        Publish(PlaybackState.Empty);
    }

    void OnSourceState(PlaybackState st)
    {
        // Quellen dürfen von jedem Thread melden. Die UI bekommt alles auf dem UI-Thread.
        Dispatcher.UIThread.Post(() =>
        {
            if (_active != null) Publish(st);
        });
    }

    void Publish(PlaybackState st)
    {
        _state = st;
        StateChanged?.Invoke(st);
    }
}
