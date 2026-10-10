using Avalonia.Threading;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Pausiert die Wiedergabe, wenn länger als eine einstellbare Zeit nichts bedient wurde (Maus, Tastatur, Medientasten, Tray).
/// Gilt für alle Quellen. Die Zeit läuft nur, solange etwas spielt. Eine Minute vorher kommt ein Hinweis, jede Bedienung bricht ab.
/// </summary>
public sealed class SleepTimer : IDisposable
{
    const int WarnSeconds = 60;

    readonly AppSettings _settings;
    readonly ISettingsStore _store;
    readonly PlayerService _player;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    DateTime _last = DateTime.UtcNow;
    bool _warned;

    public SleepTimer(AppSettings settings, ISettingsStore store, PlayerService player)
    {
        _settings = settings;
        _store = store;
        _player = player;
        _player.UserAction += Touch;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>An oder aus (Einstellung gilt für alle Player).</summary>
    public bool Enabled
    {
        get => _settings.SleepTimerEnabled;
        set
        {
            if (_settings.SleepTimerEnabled == value) return;
            _settings.SleepTimerEnabled = value;
            _store.Save("app", _settings);
            Touch();
            Changed?.Invoke();
        }
    }

    /// <summary>Minuten ohne Bedienung bis zur Pause (1 bis 720).</summary>
    public int Minutes
    {
        get => Math.Clamp(_settings.SleepTimerMinutes, 1, 720);
        set
        {
            value = Math.Clamp(value, 1, 720);
            if (_settings.SleepTimerMinutes == value) return;
            _settings.SleepTimerMinutes = value;
            _store.Save("app", _settings);
            Touch();
            Changed?.Invoke();
        }
    }

    /// <summary>Einstellung geändert.</summary>
    public event Action? Changed;

    /// <summary>Hinweis für die Anzeige. Leer = Hinweis zurücknehmen.</summary>
    public event Action<string>? Message;

    /// <summary>Es wurde etwas bedient: die Zeit beginnt von vorn.</summary>
    public void Touch()
    {
        _last = DateTime.UtcNow;
        _warned = false;
        if (_messageShown)
        {
            _messageShown = false;
            Message?.Invoke("");
        }
    }

    bool _messageShown;

    void Say(string text)
    {
        _messageShown = true;
        Message?.Invoke(text);
    }

    void Tick()
    {
        var now = DateTime.UtcNow;
        // Spielt nichts, läuft auch keine Zeit. Sie beginnt neu, sobald etwas spielt.
        if (!Enabled || !_player.State.IsPlaying)
        {
            _last = now;
            // Eine Warnung nehmen wir zurück. Die Meldung „pausiert“ bleibt stehen, bis jemand etwas bedient.
            if (_warned) { _warned = false; _messageShown = false; Message?.Invoke(""); }
            return;
        }
        var idle = now - _last;
        var limit = TimeSpan.FromMinutes(Minutes);
        if (idle >= limit)
        {
            AppLog.Info("Sleep-Timer", $"Pause nach {Minutes} min ohne Bedienung");
            _warned = false;
            _ = PauseAsync();
            _last = now;
            Say($"Sleep-Timer: Wiedergabe pausiert (keine Bedienung seit {Minutes} Minuten).");
        }
        else if (!_warned && limit - idle <= TimeSpan.FromSeconds(WarnSeconds))
        {
            _warned = true;
            Say("Sleep-Timer: Pause in einer Minute. Maus bewegen oder Taste drücken bricht ab.");
        }
    }

    async Task PauseAsync()
    {
        try { if (_player.State.IsPlaying) await _player.TogglePlayAsync(); }
        catch (Exception ex) { AppLog.Warn("Sleep-Timer", "Pausieren fehlgeschlagen", ex); }
    }

    public void Dispose()
    {
        _timer.Stop();
        _player.UserAction -= Touch;
    }
}
