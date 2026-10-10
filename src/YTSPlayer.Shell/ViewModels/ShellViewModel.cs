using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Shell.Services;
using YTSPlayer.Ui;

namespace YTSPlayer.Shell.ViewModels;

/// <summary>Zustand des Hauptfensters: Navigation, Player-Leiste, Konto. Kennt keine Modul-Details.</summary>
public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    readonly AppSettings _settings;
    readonly PlayerService _player;
    readonly PluginHost _host;
    readonly CoverLoader _covers;
    readonly NavPage _settingsPage;
    CancellationTokenSource? _coverCts;
    string? _coverKey;
    byte[]? _coverBytes;

    [ObservableProperty] NavPage? _selectedPage;
    [ObservableProperty] string _title = "Nichts spielt";
    [ObservableProperty] string _artist = "";

    /// <summary>Erscheinungsjahr als Text, leer wenn unbekannt (dann entfällt es in der Anzeige).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasYear))] string _year = "";

    public bool HasYear => Year.Length > 0;
    [ObservableProperty] Bitmap? _cover;
    [ObservableProperty] string _playIcon = Icons.Play;
    [ObservableProperty] double _position;
    [ObservableProperty] double _durationSeconds;
    [ObservableProperty] string _positionText = "0:00";
    [ObservableProperty] string _durationText = "0:00";
    [ObservableProperty] double _volume;
    [ObservableProperty] bool _canSeek;
    [ObservableProperty] bool _canNext;
    [ObservableProperty] bool _canPrevious;
    [ObservableProperty] bool _canVolume;
    [ObservableProperty] bool _canShuffle;
    [ObservableProperty] bool _canRadioMix;
    [ObservableProperty] bool _canTrackMenu;
    [ObservableProperty] bool _canOpenArtist;
    [ObservableProperty] bool _canLike;
    [ObservableProperty] bool _isLiked;
    [ObservableProperty] bool _isDisliked;
    [ObservableProperty] bool _isShuffle;
    [ObservableProperty] bool _isRadioMix;
    [ObservableProperty] string _status = "";

    /// <summary>Adresse, die das Cover öffnet (z. B. der Titel auf YouTube). Null, wenn die Quelle keine anbietet.</summary>
    [ObservableProperty] string? _externalUrl;

    /// <summary>Tooltip des Covers, solange <see cref="ExternalUrl"/> gesetzt ist.</summary>
    [ObservableProperty] string? _externalLabel;

    [ObservableProperty] bool _hasAccount;
    [ObservableProperty] bool _isLoggedIn;
    [ObservableProperty] bool _canSignIn;
    [ObservableProperty] bool _canSignOut;
    [ObservableProperty] bool _accountBusy;
    [ObservableProperty] string _accountBusyText = "";
    [ObservableProperty] string _accountName = "";
    [ObservableProperty] string _accountEmail = "";
    [ObservableProperty] string _accountInitial = "";
    [ObservableProperty] Bitmap? _avatar;

    readonly SettingsViewModel _settingsVm;

    public ShellViewModel(AppSettings settings, PlayerService player, PluginHost host, CoverLoader covers, SettingsViewModel settingsVm)
    {
        _settingsVm = settingsVm;
        _settings = settings;
        _player = player;
        _host = host;
        _covers = covers;
        _volume = settings.Volume;
        _settingsPage = new NavPage("settings", "Einstellungen", Icons.Settings, settingsVm, 900);
        Nav = [];
        RebuildNav();
        host.Pages.CollectionChanged += OnPagesChanged;
        player.StateChanged += OnState;
        player.SourceChanged += OnSourceChanged;
        host.AccountChanged += RefreshAccount;
        OnSourceChanged();
        RefreshAccount();
    }

    /// <summary>Die Navigations-Einträge: Seiten der aktiven Plugins plus die feste Einstellungs-Seite.</summary>
    public ObservableCollection<NavPage> Nav { get; }

    public bool CanOpenExternal => ExternalUrl != null;

    /// <summary>Icon des Zufall-Knopfs: durchgestrichen = aus, normal = an (dazu leuchtet der Knopf in der Akzentfarbe).</summary>
    public string ShuffleIcon => IsShuffle ? Icons.Shuffle : Icons.ShuffleOff;

    public string ShuffleTip => IsShuffle ? "Zufallswiedergabe: an (klicken zum Ausschalten)" : "Zufallswiedergabe: aus (klicken zum Einschalten)";

    public string RadioMixTip => IsRadioMix ? "Radio-Mix läuft (klicken startet ihn neu ab diesem Titel)" : "Radio-Mix zu diesem Titel starten";

    partial void OnIsShuffleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShuffleIcon));
        OnPropertyChanged(nameof(ShuffleTip));
    }

    public string LikeIcon => IsLiked ? Icons.Like : Icons.LikeOff;

    public string LikeTip => IsLiked ? "Gefällt mir: ja (klicken zum Entfernen)" : "Gefällt mir";

    partial void OnIsLikedChanged(bool value)
    {
        OnPropertyChanged(nameof(LikeIcon));
        OnPropertyChanged(nameof(LikeTip));
    }

    public string DislikeIcon => IsDisliked ? Icons.Dislike : Icons.DislikeOff;

    public string DislikeTip => IsDisliked ? "Gefällt mir nicht: ja (klicken zum Entfernen)" : "Gefällt mir nicht";

    partial void OnIsDislikedChanged(bool value)
    {
        OnPropertyChanged(nameof(DislikeIcon));
        OnPropertyChanged(nameof(DislikeTip));
    }

    partial void OnIsRadioMixChanged(bool value) => OnPropertyChanged(nameof(RadioMixTip));

    /// <summary>Wahr, wenn ein Cover da ist. Ohne Cover (z. B. lokale Datei ohne Bild) bleibt die Fläche weg.</summary>
    public bool HasCover => Cover != null;

    partial void OnCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCover));

    partial void OnExternalUrlChanged(string? value) => OnPropertyChanged(nameof(CanOpenExternal));

    public bool Dragging { get; private set; }

    string? _lastItemId;
    string? _lastTitle;
    long _dragStartTick;
    double _seekTarget;
    long _seekUntilTick;

    /// <summary>Der Zeiger hat die Leiste gepackt: Positionsmeldungen der Quelle werden bis zum Loslassen nicht angezeigt.</summary>
    public void BeginDrag()
    {
        Dragging = true;
        _dragStartTick = Environment.TickCount64;
    }

    /// <summary>Der Zeiger wurde losgelassen: dorthin spulen.</summary>
    public void EndDrag(double seconds)
    {
        if (!Dragging) return;
        Dragging = false;
        Seek(seconds);
    }
    public bool Quitting { get; set; }
    public bool MinimizeOnClose => _settings.MinimizeOnClose;
    public double SliderMax => DurationSeconds > 0 ? DurationSeconds : 1;
    public object? CurrentContent => SelectedPage?.ViewModel;

    public Action? OpenWidget { get; set; }
    public Action? OpenDiagnostics { get; set; }
    public Action? ShowMain { get; set; }
    public Action? QuitApp { get; set; }

    /// <summary>Wird gemeldet, wenn eine andere Seite geöffnet wird (für das Wiederherstellen nach dem Neustart).</summary>
    public Action<string>? PageChanged { get; set; }

    partial void OnSelectedPageChanged(NavPage? value)
    {
        OnPropertyChanged(nameof(CurrentContent));
        if (value != null) PageChanged?.Invoke(value.Id);
    }
    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(SliderMax));
    bool _syncingVolume;

    partial void OnVolumeChanged(double value)
    {
        if (_syncingVolume) return;
        _ = Run(() => _player.SetVolumeAsync(value));
    }

    /// <summary>Wechselt zur Seite mit dieser Id (INavigator).</summary>
    public void NavigateTo(string pageId)
    {
        var page = Nav.FirstOrDefault(p => p.Id == pageId);
        if (page != null) SelectedPage = page;
    }

    public void Seek(double sec)
    {
        Position = sec;
        // Bis die Quelle die neue Stelle meldet, kommen noch Meldungen von davor. Die zeigen wir nicht an (sonst springt die Leiste zurück).
        _seekTarget = sec;
        _seekUntilTick = Environment.TickCount64 + 2500;
        _ = Run(() => _player.SeekAsync(TimeSpan.FromSeconds(sec)));
    }

    [RelayCommand] Task TogglePlay() => Run(_player.TogglePlayAsync);
    [RelayCommand] Task ToggleShuffle() => Run(_player.ToggleShuffleAsync);
    // ---- Songtext (optional einblendbar) ----

    [ObservableProperty] bool _canLyrics;
    [ObservableProperty] bool _showLyrics;
    [ObservableProperty] string _lyricsText = "";
    [ObservableProperty] string _lyricsSource = "";

    CancellationTokenSource? _lyricsCts;
    string? _lyricsFor;

    [RelayCommand]
    void ToggleLyrics() => ShowLyrics = !ShowLyrics;

    partial void OnShowLyricsChanged(bool value)
    {
        if (value) _ = LoadLyricsAsync();
        else _lyricsCts?.Cancel();
    }

    /// <summary>Lädt den Songtext des laufenden Titels (nur wenn die Anzeige offen ist und der Titel sich geändert hat).</summary>
    async Task LoadLyricsAsync()
    {
        var key = _lastItemId + "|" + _lastTitle;
        if (!ShowLyrics || key == _lyricsFor) return;
        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        _lyricsFor = key;
        LyricsSource = "";
        if (_player.ActiveSource is not ILyricsSource s)
        {
            LyricsText = "Die laufende Quelle liefert keine Songtexte.";
            return;
        }
        LyricsText = "Songtext wird geladen …";
        try
        {
            // Kurz warten: beim Titelwechsel ändern sich Angaben oft noch einmal.
            await Task.Delay(400, cts.Token);
            var r = await s.GetLyricsAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            LyricsText = r?.Text ?? "Zu diesem Titel gibt es keinen Songtext.";
            LyricsSource = r?.Source ?? "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("Songtext", "Laden fehlgeschlagen", ex);
            if (!cts.IsCancellationRequested) LyricsText = "Der Songtext konnte nicht geladen werden.";
        }
    }

    // ---- Sleep-Timer ----

    SleepTimer? _sleep;

    /// <summary>Verbindet den Sleep-Timer mit der Anzeige (Knopf in der Kopfzeile, Hinweis in der Player-Leiste).</summary>
    public void AttachSleep(SleepTimer sleep)
    {
        _sleep = sleep;
        sleep.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(SleepOn));
            OnPropertyChanged(nameof(SleepIcon));
            OnPropertyChanged(nameof(SleepTip));
        });
        sleep.Message += m => Dispatcher.UIThread.Post(() =>
        {
            // Der Hinweis bleibt in der Player-Leiste stehen, auch wenn die Quelle neue Zustände meldet.
            _sleepMessage = m;
            Status = m.Length > 0 ? m : _playStatus;
        });
    }

    string _sleepMessage = "";
    string _playStatus = "";

    public bool SleepOn => _sleep?.Enabled ?? false;
    public string SleepIcon => SleepOn ? Icons.Sleep : Icons.SleepOff;

    public string SleepTip => SleepOn
        ? $"Sleep-Timer an: Pause nach {_sleep?.Minutes} Minuten ohne Bedienung (klicken zum Ausschalten)"
        : "Sleep-Timer aus (klicken zum Einschalten)";

    [RelayCommand]
    void ToggleSleep()
    {
        if (_sleep != null) _sleep.Enabled = !_sleep.Enabled;
    }

    /// <summary>Klick auf den Künstlernamen: zeigt die Seite des Künstlers (die große App kommt dabei nach vorn, auch aus dem Widget).</summary>
    [RelayCommand]
    async Task OpenArtist()
    {
        if (_player.ActiveSource is not IArtistSource s) return;
        ShowMain?.Invoke();
        await Run(s.OpenArtistAsync);
    }

    /// <summary>Zeigt das Menü des laufenden Titels (wenn die aktive Quelle eins anbietet).</summary>
    public void ShowTrackMenu(Avalonia.Controls.Control anchor)
    {
        if (_player.ActiveSource is YTSPlayer.Ui.ITrackMenuProvider p) p.ShowTrackMenu(anchor);
    }

    [RelayCommand] Task ToggleLike() => Run(_player.ToggleLikeAsync);

    [RelayCommand] Task ToggleDislike() => Run(_player.ToggleDislikeAsync);
    [RelayCommand] Task RadioMix() => Run(_player.StartRadioMixAsync);
    [RelayCommand] Task Next() => Run(_player.NextAsync);
    [RelayCommand] Task Previous() => Run(_player.PreviousAsync);
    /// <summary>Verwandelt die große App in das Widget (das Hauptfenster verschwindet).</summary>
    [RelayCommand] void Widget() => OpenWidget?.Invoke();

    /// <summary>Vom Widget zurück zur großen App (das Widget verschwindet).</summary>
    [RelayCommand] void ReturnToMain() => ShowMain?.Invoke();
    [RelayCommand] void Diagnostics() => OpenDiagnostics?.Invoke();
    [RelayCommand] void Quit() => QuitApp?.Invoke();

    [RelayCommand]
    Task SignIn() => _host.Account is { } a ? Run(a.SignInAsync) : Task.CompletedTask;

    /// <summary>Öffnet die Einstellungen (Kopfzeile, Zahnrad) bei „Allgemein“.</summary>
    [RelayCommand]
    void OpenSettings()
    {
        _settingsVm.Select(SettingsViewModel.GeneralId);
        NavigateTo(_settingsPage.Id);
    }

    [RelayCommand]
    void OpenAccountSettings()
    {
        // Das Konto-Modul nennt die Einstellungs-Seite, die zu ihm gehört (bei YouTube Music dessen Eintrag).
        _settingsVm.Select(_host.Account?.SettingsPageId);
        NavigateTo(_settingsPage.Id);
    }

    [RelayCommand]
    Task SignOut() => _host.Account is { } a ? Run(a.SignOutAsync) : Task.CompletedTask;

    async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            AppLog.Error("Shell", "Aktion fehlgeschlagen", ex);
            Status = "Fehler: " + ex.Message;
        }
    }

    void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNav();

    void RebuildNav()
    {
        var keep = SelectedPage?.Id;
        Nav.Clear();
        foreach (var p in _host.Pages) Nav.Add(p);
        Nav.Add(_settingsPage);
        SelectedPage = Nav.FirstOrDefault(p => p.Id == keep) ?? Nav[0];
    }

    void OnSourceChanged()
    {
        // Die neue Quelle hat ihre eigene Lautstärke. Den Regler anpassen, ohne sie zurückzuschreiben.
        if (Math.Abs(Volume - _player.Volume) > 0.0001)
        {
            _syncingVolume = true;
            Volume = _player.Volume;
            _syncingVolume = false;
        }
        var c = _player.Capabilities;
        CanSeek = c.HasFlag(PlaybackCapabilities.Seek);
        CanNext = c.HasFlag(PlaybackCapabilities.Next);
        CanPrevious = c.HasFlag(PlaybackCapabilities.Previous);
        CanVolume = c.HasFlag(PlaybackCapabilities.Volume);
        CanShuffle = c.HasFlag(PlaybackCapabilities.Shuffle);
        CanRadioMix = c.HasFlag(PlaybackCapabilities.RadioMix);
        CanLike = c.HasFlag(PlaybackCapabilities.Like);
        CanTrackMenu = c.HasFlag(PlaybackCapabilities.TrackMenu);
        CanOpenArtist = c.HasFlag(PlaybackCapabilities.Artist);
        CanLyrics = c.HasFlag(PlaybackCapabilities.Lyrics);
    }

    void OnState(PlaybackState st)
    {
        Title = string.IsNullOrEmpty(st.Title) ? "Nichts spielt" : st.Title;
        Artist = st.Artist;
        Year = st.Year > 0 ? st.Year.ToString() : "";
        PlayIcon = st.IsPlaying ? Icons.Pause : Icons.Play;
        DurationSeconds = st.Duration.TotalSeconds;
        // Hängt das Ziehen (Loslassen verloren), nach 15 s von selbst beenden.
        if (Dragging && Environment.TickCount64 - _dragStartTick > 15000) Dragging = false;
        // Neuer Titel: Wartezeit für das Spulen und Ziehen beenden, die Leiste folgt wieder der Quelle.
        if (st.Item?.Id != _lastItemId || st.Title != _lastTitle)
        {
            _lastItemId = st.Item?.Id;
            _lastTitle = st.Title;
            _seekUntilTick = 0;
            Dragging = false;
            if (ShowLyrics && st.Title.Length > 0) _ = LoadLyricsAsync();
        }
        var seeking = Environment.TickCount64 < _seekUntilTick && Math.Abs(st.Position.TotalSeconds - _seekTarget) > 2;
        if (!Dragging && !seeking)
        {
            Position = st.Position.TotalSeconds;
            if (Environment.TickCount64 < _seekUntilTick) _seekUntilTick = 0;
        }
        PositionText = Fmt(st.Position);
        DurationText = Fmt(st.Duration);
        _playStatus = st.Title.Length == 0 ? "" : st.IsPlaying ? "Spielt" : "Pausiert";
        Status = _sleepMessage.Length > 0 ? _sleepMessage : _playStatus;
        IsShuffle = st.Shuffle;
        IsRadioMix = st.RadioMix;
        IsLiked = st.Liked;
        IsDisliked = st.Disliked;
        ExternalUrl = st.ExternalUrl;
        ExternalLabel = st.ExternalLabel;
        OnSourceChanged();
        if (st.CoverUrl != _coverKey || !ReferenceEquals(st.CoverBytes, _coverBytes))
        {
            _coverKey = st.CoverUrl;
            _coverBytes = st.CoverBytes;
            _ = LoadCoverAsync(st.CoverUrl, st.CoverBytes);
        }
    }

    async Task LoadCoverAsync(string? url, byte[]? bytes)
    {
        _coverCts?.Cancel();
        var cts = _coverCts = new CancellationTokenSource();
        try
        {
            var bmp = await _covers.LoadAsync(url, bytes, 320, cts.Token);
            if (!cts.IsCancellationRequested) Cover = bmp;
        }
        catch (OperationCanceledException) { }
    }

    void RefreshAccount()
    {
        var a = _host.Account;
        HasAccount = a != null;
        var state = a?.State ?? AccountState.LoggedOut;
        IsLoggedIn = state == AccountState.LoggedIn;
        CanSignIn = a != null && state == AccountState.LoggedOut;
        CanSignOut = a != null && state == AccountState.LoggedIn;
        AccountBusy = state is AccountState.LoginPanelOpen or AccountState.Verifying or AccountState.LoggingOut;
        AccountBusyText = state switch
        {
            AccountState.LoginPanelOpen => "Anmeldung läuft …",
            AccountState.Verifying => "Profil wird gelesen …",
            AccountState.LoggingOut => "Abmelden …",
            _ => "",
        };
        var p = a?.Profile;
        AccountName = p?.Name ?? "";
        AccountEmail = p?.Email ?? "";
        AccountInitial = string.IsNullOrEmpty(p?.Name) ? "" : p!.Name[..1].ToUpperInvariant();
        Avatar = p?.Avatar is { Length: > 0 } b ? CoverLoader.Decode(b, 96) : null;
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
