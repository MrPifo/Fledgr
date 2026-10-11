using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Localization;
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
    [ObservableProperty] string _title = Loc.Current["shell.player.idle"];
    [ObservableProperty] string _artist = "";

    /// <summary>Wahr, solange kein Titel läuft (die Anzeige zeigt dann den Leerlauf-Text).</summary>
    public bool IsIdle { get; private set; } = true;

    /// <summary>Künstler, bei bekanntem Jahr mit Jahr (z. B. „Name · 2019“) für die Zeile in der Player-Karte.</summary>
    public string ArtistLine => HasYear ? $"{Artist} · {Year}" : Artist;

    partial void OnArtistChanged(string value) => OnPropertyChanged(nameof(ArtistLine));

    /// <summary>Erscheinungsjahr als Text, leer wenn unbekannt (dann entfällt es in der Anzeige).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasYear))] string _year = "";

    public bool HasYear => Year.Length > 0;

    partial void OnYearChanged(string value) => OnPropertyChanged(nameof(ArtistLine));
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
    [ObservableProperty] bool _canAddToPlaylist;
    [ObservableProperty] bool _canOpenArtist;
    [ObservableProperty] bool _canLike;
    [ObservableProperty] bool _isLiked;
    [ObservableProperty] bool _isDisliked;
    [ObservableProperty] bool _isShuffle;
    [ObservableProperty] bool _isRadioMix;
    [ObservableProperty] string _status = "";

    /// <summary>Name und Symbol der Quelle, die gerade spielt (Miniplayer).</summary>
    [ObservableProperty] string _sourceName = "";
    [ObservableProperty] string _sourceIcon = Icons.Music;
    /// <summary>Kurzer Quellenname fürs Widget (Radio, Music, Lokal).</summary>
    [ObservableProperty] string _sourceShort = "";
    /// <summary>Modulfarbe der Quelle (music, yt, radio), wie NavPage.Tone.</summary>
    [ObservableProperty] string _sourceTone = "music";
    /// <summary>Wahr bei Live-Streams (Radio): im Widget erscheint der rote Punkt.</summary>
    [ObservableProperty] bool _isLive;
    /// <summary>Wahr, wenn die Quelle wirklich spielt (nicht pausiert, nicht beim Verbinden): dann bewegen sich die Signal-Balken.</summary>
    [ObservableProperty] bool _barsOn;
    /// <summary>Wahr, wenn die aktive Quelle eine Favoriten-Liste hat (Radio).</summary>
    [ObservableProperty] bool _canFavorites;
    [ObservableProperty] bool _hasSource;

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
        _settingsPage = new NavPage("settings", "shell.nav.settings", Icons.Settings, settingsVm, 900);
        Nav = [];
        Modules = [];
        RebuildNav();
        host.Pages.CollectionChanged += OnPagesChanged;
        player.StateChanged += OnState;
        player.SourceChanged += OnSourceChanged;
        host.AccountChanged += RefreshAccount;
        OnSourceChanged();
        RefreshAccount();
        Loc.Current.LanguageChanged += () => Dispatcher.UIThread.Post(RefreshTexts);
    }

    /// <summary>Die Sprache hat gewechselt: alle berechneten Texte neu abfragen und die Navigation neu aufbauen.</summary>
    void RefreshTexts()
    {
        if (IsIdle) Title = Loc.Current["shell.player.idle"];
        RebuildNav();
        RefreshAccount();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Die Navigations-Einträge: Seiten der aktiven Plugins plus die feste Einstellungs-Seite.</summary>
    public ObservableCollection<NavPage> Nav { get; }

    /// <summary>Die Module in der Schiene links: wie <see cref="Nav"/>, aber ohne die Einstellungs-Seite (sie hat einen eigenen Knopf unten).</summary>
    public ObservableCollection<NavPage> Modules { get; }

    /// <summary>Das gewählte Modul in der Schiene. Ist die Einstellungs-Seite offen, ist keins gewählt.</summary>
    public NavPage? SelectedModule
    {
        get => SelectedPage != null && Modules.Contains(SelectedPage) ? SelectedPage : null;
        set { if (value != null) SelectedPage = value; }
    }

    /// <summary>Wahr, solange das Einstellungs-Popup offen ist (es liegt über der Seite und ersetzt sie nicht).</summary>
    [ObservableProperty] bool _isSettingsOpen;

    /// <summary>Das ViewModel der Einstellungen (Inhalt des Popups).</summary>
    public SettingsViewModel SettingsVm => _settingsVm;

    /// <summary>Das Popup ist im Baum sichtbar. Bleibt nach dem Schließen kurz an, damit es ausblenden kann, und fängt keine Eingaben mehr ab.</summary>
    [ObservableProperty] bool _settingsVisible;

    /// <summary>Öffnet das Einstellungs-Popup mit Einblend-Animation (erst sichtbar schalten, dann im nächsten Takt „offen“).</summary>
    void ShowSettings()
    {
        SettingsVisible = true;
        Dispatcher.UIThread.Post(() => IsSettingsOpen = true, DispatcherPriority.Background);
    }

    /// <summary>Schließt das Einstellungs-Popup (X, Esc oder Klick daneben).</summary>
    [RelayCommand]
    void CloseSettings()
    {
        IsSettingsOpen = false;
        _ = Task.Delay(300).ContinueWith(_ => Dispatcher.UIThread.Post(() => { if (!IsSettingsOpen) SettingsVisible = false; }));
    }

    /// <summary>Was die offene Seite für die Mitte der Kopfzeile anbietet (z. B. die Suche), sonst null.</summary>
    public object? HeaderContent => (SelectedPage?.ViewModel as IHeaderContent)?.HeaderViewModel;

    /// <summary>Wahr, wenn der Drei-Punkte-Knopf der Player-Karte etwas anzeigen kann.</summary>
    public bool CanMore => CanTrackMenu || CanLyrics || CanRadioMix || CanLike;

    /// <summary>Wahr, wenn das Widget einen Drei-Punkte-Knopf zeigt. Gefällt mir, Nicht gefallen und Zufall liegen dort im Menü.</summary>
    public bool CanWidgetMore => CanMore || CanLike || CanShuffle;

    /// <summary>Wahr, wenn die Statuszeile mehr als „Spielt/Pausiert“ zeigt (z. B. Verbindungs- oder Fehlermeldung).</summary>
    public bool StatusVisible => Status.Length > 0 && Status != _playStatus;

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusVisible));
    partial void OnCanTrackMenuChanged(bool value) => RaiseMore();
    partial void OnCanLyricsChanged(bool value) => RaiseMore();
    partial void OnCanRadioMixChanged(bool value) => RaiseMore();
    partial void OnCanLikeChanged(bool value) => RaiseMore();
    partial void OnCanShuffleChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWidgetMore));
        OnPropertyChanged(nameof(ShowAddSpacer));
    }

    partial void OnCanAddToPlaylistChanged(bool value) => OnPropertyChanged(nameof(ShowAddSpacer));

    /// <summary>Wahr, wenn rechts von „Weiter“ ein leerer Platz steht (Zufall ist da, „Zu Playlist“ nicht), damit Play mittig bleibt.</summary>
    public bool ShowAddSpacer => CanShuffle && !CanAddToPlaylist;

    void RaiseMore()
    {
        OnPropertyChanged(nameof(CanMore));
        OnPropertyChanged(nameof(CanWidgetMore));
    }

    public bool CanOpenExternal => ExternalUrl != null;

    /// <summary>Quelle unter dem Cover (Radio, YouTube). Lokale Musik zeigt sie nicht.</summary>
    public bool ShowSourceLine => HasSource && _player.ActiveSourceId is "radio" or "yt";

    /// <summary>Linke Spalte im Widget (Cover, Quelle). Ohne beides steht der Titel ganz links.</summary>
    public bool ShowCoverColumn => HasCover || ShowSourceLine;

    /// <summary>Icon des Zufall-Knopfs: durchgestrichen = aus, normal = an (dazu leuchtet der Knopf in der Akzentfarbe).</summary>
    public string ShuffleIcon => IsShuffle ? Icons.Shuffle : Icons.ShuffleOff;

    public string ShuffleTip => Loc.Current[IsShuffle ? "shell.player.shuffle.on" : "shell.player.shuffle.off"];

    public string RadioMixTip => Loc.Current[IsRadioMix ? "shell.player.mix.on" : "shell.player.mix.off"];

    partial void OnIsShuffleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShuffleIcon));
        OnPropertyChanged(nameof(ShuffleTip));
    }

    public string LikeIcon => IsLiked ? Icons.Like : Icons.LikeOff;

    public string LikeTip => Loc.Current[IsLiked ? "shell.player.like.on" : "shell.player.like.off"];

    partial void OnIsLikedChanged(bool value)
    {
        OnPropertyChanged(nameof(LikeIcon));
        OnPropertyChanged(nameof(LikeTip));
    }

    public string DislikeIcon => IsDisliked ? Icons.Dislike : Icons.DislikeOff;

    public string DislikeTip => Loc.Current[IsDisliked ? "shell.player.dislike.on" : "shell.player.dislike.off"];

    partial void OnIsDislikedChanged(bool value)
    {
        OnPropertyChanged(nameof(DislikeIcon));
        OnPropertyChanged(nameof(DislikeTip));
    }

    partial void OnIsRadioMixChanged(bool value) => OnPropertyChanged(nameof(RadioMixTip));

    /// <summary>Wahr, wenn ein Cover da ist. Ohne Cover (z. B. lokale Datei ohne Bild) bleibt die Fläche weg.</summary>
    public bool HasCover => Cover != null;

    partial void OnCoverChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(ShowCoverColumn));
    }

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

    System.ComponentModel.INotifyPropertyChanged? _pageNotifier;

    /// <summary>Das ViewModel der offenen Seite meldet, dass sich die Mitte der Kopfzeile geändert hat (z. B. nach der Anmeldung).</summary>
    void OnPageVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IHeaderContent.HeaderViewModel)) Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(HeaderContent)));
    }

    partial void OnSelectedPageChanged(NavPage? value)
    {
        if (_pageNotifier != null) _pageNotifier.PropertyChanged -= OnPageVmChanged;
        _pageNotifier = value?.ViewModel as System.ComponentModel.INotifyPropertyChanged;
        if (_pageNotifier != null) _pageNotifier.PropertyChanged += OnPageVmChanged;
        OnPropertyChanged(nameof(CurrentContent));
        OnPropertyChanged(nameof(SelectedModule));
        OnPropertyChanged(nameof(HeaderContent));
        if (value != null) PageChanged?.Invoke(value.Id);
    }
    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(SliderMax));
    bool _syncingVolume;

    /// <summary>Lautstärke als Text (z. B. „50 %“) für das Lautstärke-Fenster im Widget.</summary>
    public string VolumeText => Loc.F("shell.player.volume.percent", Loc.Current.Integer((long)Math.Round(Volume * 100)));

    /// <summary>Symbol des Lautstärke-Knopfs: durchgestrichen bei 0 %.</summary>
    public string VolumeIcon => Volume <= 0.001 || _systemMuted ? Icons.VolumeOff : Icons.Volume;

    double _lastVolume = 0.5;

    /// <summary>Stumm schalten oder wieder die letzte Lautstärke setzen (Klick auf das Lautstärke-Symbol der Player-Karte, im Widget Mausrad-Klick).</summary>
    [RelayCommand]
    public void ToggleMute()
    {
        if (Volume > 0.001) { _lastVolume = Volume; Volume = 0; }
        else Volume = _lastVolume > 0.001 ? _lastVolume : 0.5;
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeText));
        OnPropertyChanged(nameof(VolumeIcon));
        if (value > 0.001) _lastVolume = value;
        if (_syncingVolume) return;
        _ = Run(() => _player.SetVolumeAsync(value));
    }

    /// <summary>Wechselt zur Seite mit dieser Id (INavigator).</summary>
    public void NavigateTo(string pageId)
    {
        if (pageId == _settingsPage.Id) { ShowSettings(); return; }
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
            LyricsText = Loc.Current["shell.lyrics.unsupported"];
            return;
        }
        LyricsText = Loc.Current["shell.lyrics.loading"];
        try
        {
            // Kurz warten: beim Titelwechsel ändern sich Angaben oft noch einmal.
            await Task.Delay(400, cts.Token);
            var r = await s.GetLyricsAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            LyricsText = r?.Text ?? Loc.Current["shell.lyrics.none"];
            LyricsSource = r?.Source ?? "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Warn("Songtext", "Laden fehlgeschlagen", ex);
            if (!cts.IsCancellationRequested) LyricsText = Loc.Current["shell.lyrics.failed"];
        }
    }

    // ---- Sleep-Timer ----

    SleepTimer? _sleep;

    /// <summary>Verbindet den Sleep-Timer mit der Anzeige (Knopf in der Kopfzeile, Hinweis in der Player-Leiste).</summary>
    /// <summary>Fragt alle 1,5 s ab, ob das System stumm ist, und zeigt dann das durchgestrichene Lautsprecher-Symbol.</summary>
    public void AttachSystemAudio(YTSPlayer.Contracts.Playback.ISystemAudio audio)
    {
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += (_, _) =>
        {
            var muted = audio.IsMuted();
            if (muted == _systemMuted) return;
            _systemMuted = muted;
            OnPropertyChanged(nameof(VolumeIcon));
        };
        timer.Start();
    }

    bool _systemMuted;

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
        ? Loc.Current.Format("shell.sleep.on", _sleep?.Minutes)
        : Loc.Current["shell.sleep.off"];

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
    public void ShowTrackMenu(Avalonia.Controls.Control anchor, bool withActions = false)
    {
        // Einträge, die die Shell selbst beisteuert: Songtext und (wenn die Quelle kein eigenes Menü hat) Radio-Mix.
        var provider = _player.ActiveSource as YTSPlayer.Ui.ITrackMenuProvider;
        var extras = new List<Avalonia.Controls.Control>();
        // Gefällt mir und Nicht gefallen gibt es nur hier im Menü (Player-Karte und Widget). Im Widget kommt die Zufall-Taste dazu.
        if (CanLike)
        {
            extras.Add(ActionItem(LikeTip, LikeIcon, ToggleLikeCommand));
            extras.Add(ActionItem(DislikeTip, DislikeIcon, ToggleDislikeCommand));
        }
        if (withActions && CanShuffle) extras.Add(ActionItem(ShuffleTip, ShuffleIcon, ToggleShuffleCommand));
        if (CanLyrics)
            extras.Add(new Avalonia.Controls.MenuItem { Header = Loc.Current["shell.menu.lyrics"], Command = ToggleLyricsCommand, Icon = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.TextLong } });
        if (CanRadioMix && provider == null)
            extras.Add(new Avalonia.Controls.MenuItem { Header = Loc.Current["shell.menu.radiomix"], Command = RadioMixCommand, Icon = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.RadioTower } });
        if (provider != null) provider.ShowTrackMenu(anchor, extras);
        else if (extras.Count > 0)
        {
            var menu = new Avalonia.Controls.MenuFlyout { ShowMode = Avalonia.Controls.FlyoutShowMode.Transient };
            foreach (var e in extras) menu.Items.Add(e);
            YTSPlayer.Ui.Controls.PopupExtensions.ShowAbove(menu, anchor);
        }
    }

    /// <summary>Zeigt die Playlist-Auswahl für den laufenden Titel (wenn die aktive Quelle das kann).</summary>
    public void ShowAddToPlaylist(Avalonia.Controls.Control anchor) => (_player.ActiveSource as YTSPlayer.Ui.IAddToPlaylistProvider)?.ShowAddToPlaylist(anchor);

    /// <summary>Zeigt die Favoriten der aktiven Quelle (Radio) als Menü.</summary>
    public void ShowFavorites(Avalonia.Controls.Control anchor) => (_player.ActiveSource as YTSPlayer.Ui.IFavoritesProvider)?.ShowFavorites(anchor);

    static Avalonia.Controls.MenuItem ActionItem(string header, string icon, System.Windows.Input.ICommand command) => new()
    {
        Header = header,
        Command = command,
        Icon = new Material.Icons.Avalonia.MaterialIcon
        {
            Kind = Enum.TryParse<Material.Icons.MaterialIconKind>(icon, out var kind) ? kind : Material.Icons.MaterialIconKind.Help,
        },
    };

    [RelayCommand] Task ToggleLike() => Run(_player.ToggleLikeAsync);

    [RelayCommand] Task ToggleDislike() => Run(_player.ToggleDislikeAsync);
    [RelayCommand] Task RadioMix() => Run(_player.StartRadioMixAsync);
    [RelayCommand] Task Next() => Run(_player.NextAsync);
    [RelayCommand] Task Previous() => Run(_player.PreviousAsync);
    /// <summary>Verwandelt die große App in das Widget (das Hauptfenster verschwindet).</summary>
    [RelayCommand] void Widget() => OpenWidget?.Invoke();

    /// <summary>Vom Widget zurück zur großen App (das Widget verschwindet).</summary>
    [RelayCommand] void ReturnToMain() => ShowMain?.Invoke();
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
            Status = Loc.Current.Format("shell.error", ex.Message);
        }
    }

    void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildNav();

    void RebuildNav()
    {
        var keep = SelectedPage?.Id;
        Nav.Clear();
        Modules.Clear();
        foreach (var p in _host.Pages) { Nav.Add(p); Modules.Add(p); }
        SelectedPage = Nav.FirstOrDefault(p => p.Id == keep) ?? Nav.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedModule));
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
        CanAddToPlaylist = c.HasFlag(PlaybackCapabilities.AddToPlaylist);
        CanOpenArtist = c.HasFlag(PlaybackCapabilities.Artist);
        CanLyrics = c.HasFlag(PlaybackCapabilities.Lyrics);

        SourceName = _player.ActiveSource?.DisplayName ?? "";
        HasSource = SourceName.Length > 0;
        OnPropertyChanged(nameof(ShowSourceLine));
        OnPropertyChanged(nameof(ShowCoverColumn));
        // Symbole je bekannter Quelle. Unbekannte Quellen bekommen das Musik-Symbol.
        SourceIcon = _player.ActiveSourceId switch
        {
            "radio" => Icons.Radio,
            "yt" => Icons.Youtube,
            _ => Icons.Music,
        };
        SourceTone = _player.ActiveSourceId switch
        {
            "radio" => "radio",
            "yt" => "yt",
            _ => "music",
        };
        IsLive = _player.ActiveSourceId == "radio";
        CanFavorites = _player.ActiveSource is YTSPlayer.Ui.IFavoritesProvider { HasFavorites: true };
        SourceShort = Loc.Current[_player.ActiveSourceId switch
        {
            "radio" => "shell.widget.source.radio",
            "yt" => "shell.widget.source.yt",
            _ => "shell.widget.source.local",
        }];
    }

    void OnState(PlaybackState st)
    {
        IsIdle = string.IsNullOrEmpty(st.Title);
        Title = IsIdle ? Loc.Current["shell.player.idle"] : st.Title;
        Artist = st.Artist;
        Year = st.Year > 0 ? st.Year.ToString() : "";
        PlayIcon = st.IsPlaying ? Icons.Pause : Icons.Play;
        BarsOn = st.IsPlaying;
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
        _playStatus = st.Title.Length == 0 ? "" : Loc.Current[st.IsPlaying ? "shell.status.playing" : "shell.status.paused"];
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
            AccountState.LoginPanelOpen => Loc.Current["shell.account.busy.login"],
            AccountState.Verifying => Loc.Current["shell.account.busy.verify"],
            AccountState.LoggingOut => Loc.Current["shell.account.busy.logout"],
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
