using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Localization;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>Seite „YouTube Music“: Anmelden, Link abspielen, Playlists und ihre Titel.</summary>
public sealed partial class YtViewModel : ObservableObject, IHeaderContent
{
    readonly YtAccountSession _account;
    readonly YtLibrary _library;
    readonly IPlayer _player;
    readonly IYtWebHost _host;
    readonly IThumbnailCache _thumbs;
    readonly ISettingsStore _store;
    readonly YtViewState _view;
    readonly YtSearchHeader _header;
    Action<bool, double>? _applyScroll;
    bool _restoringScroll;
    CancellationTokenSource? _saveCts;
    CancellationTokenSource? _refreshCts;
    CancellationTokenSource? _tracksCts;
    CancellationTokenSource? _checkCts;
    bool _autoChecked;

    [ObservableProperty] bool _showLoggedOut = true;
    [ObservableProperty] bool _showLogin;
    [ObservableProperty] bool _showBusy;
    [ObservableProperty] bool _showLoggedIn;
    [ObservableProperty] string _busyText = "";
    [ObservableProperty] string _notice = "";
    [ObservableProperty] Control? _loginSurface;

    [ObservableProperty] PlaylistInfo? _selected;
    [ObservableProperty] LoadState _state = LoadState.Empty;
    [ObservableProperty] string _message = Loc.T("yt.msg.noPlaylists");
    [ObservableProperty] string _errorText = "";
    [ObservableProperty] string _info = "";
    [ObservableProperty] bool _backgroundBusy;


    [ObservableProperty] PlaylistInfo? _opened;
    [ObservableProperty] TrackItem? _selectedTrack;
    [ObservableProperty] LoadState _tracksState = LoadState.Empty;
    [ObservableProperty] string _tracksMessage = "";
    [ObservableProperty] string _tracksError = "";

    public YtViewModel(YtAccountSession account, YtLibrary library, IPlayer player, YtEngine engine, IThumbnailCache thumbs, ISettingsStore store)
    {
        _account = account;
        _library = library;
        _player = player;
        _thumbs = thumbs;
        _store = store;
        _view = store.Load<YtViewState>("yt-view");
        _header = new YtSearchHeader(this);
        // Die Startseite ist der Einstieg (die zuletzt geöffnete Playlist zeigt sich trotzdem wieder, siehe RestoreView).
        _homeMode = true;
        _host = engine.Host;
        Items = [.. library.LoadCached()];
        EnsureLiked();
        Items.CollectionChanged += (_, _) => UpdatePlayingPlaylists();
        if (HasRealItems) State = LoadState.Ready;
        HookPlaying();
        LoadCachedHome();
        account.Changed += () => Dispatcher.UIThread.Post(RefreshAccountState);
        RefreshAccountState();
        RestoreView();
        _ = AutoCheckAsync();
    }

    // ---- Laufender Titel in den Listen markieren ----

    string _curId = "";
    bool _curPlaying;

    void HookPlaying()
    {
        _player.StateChanged += OnPlayerState;
        Tracks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (TrackItem t in e.NewItems)
            {
                MarkRow(t);
                if (t.IsCurrent && SelectedTrack == null && Opened != null) SelectedTrack = t;
            }
        };
        ArtistSongs.CollectionChanged += (_, e) => MarkNewRows(e);
        HomeSections.CollectionChanged += (_, e) => MarkNewSections(e);
        ArtistSections.CollectionChanged += (_, e) => MarkNewSections(e);
        ApplyPlayerState(_player.State, select: false);
    }

    void MarkNewRows(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null) return;
        foreach (TrackItem t in e.NewItems) MarkRow(t);
    }

    void MarkNewSections(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null) return;
        foreach (ArtistSectionVm sec in e.NewItems)
            foreach (var t in sec.Items) MarkRow(t);
    }

    void MarkRow(TrackItem t) => t.SetPlaying(_curId.Length > 0 && t.Info.Kind.Length == 0 && t.Info.Id == _curId, _curPlaying);

    void OnPlayerState(PlaybackState st) => ApplyPlayerState(st, select: true);

    /// <summary>Merkt, welcher YT-Titel läuft, markiert ihn in allen Listen und wählt ihn in der offenen Titelliste aus. Arbeitet nur bei Änderung (Titel oder Spielt/Pause).</summary>
    void ApplyPlayerState(PlaybackState st, bool select)
    {
        var id = st.Item is { } it && it.SourceId == YtPlaybackSource.SourceKey ? UrlNormalizer.VideoId(it.Id) ?? "" : "";
        var playing = id.Length > 0 && st.IsPlaying;
        if (id == _curId && playing == _curPlaying) return;
        var idChanged = id != _curId;
        _curId = id;
        _curPlaying = playing;
        foreach (var t in Tracks) MarkRow(t);
        foreach (var t in ArtistSongs) MarkRow(t);
        foreach (var sec in HomeSections) foreach (var t in sec.Items) MarkRow(t);
        foreach (var sec in ArtistSections) foreach (var t in sec.Items) MarkRow(t);
        UpdatePlayingPlaylists();
        if (select && idChanged && id.Length > 0 && Opened != null && Tracks.FirstOrDefault(t => t.IsCurrent) is { } row) SelectedTrack = row;
    }

    readonly Dictionary<string, HashSet<string>> _plIndex = [];
    string _lastOpenedId = "";

    /// <summary>
    /// Markiert die Playlists, in denen der laufende Titel steckt (Hervorhebung und Balken in der Playlist-Liste).
    /// Grundlage sind die gespeicherten Titellisten, sie werden einmal im Hintergrund eingelesen. Die „Gefällt mir“-Zeile zählt nicht.
    /// </summary>
    async void UpdatePlayingPlaylists()
    {
        try
        {
            var missing = _curId.Length == 0 ? [] : Items.Where(p => p.Id != YtLibrary.LikedId && p.Id != YtLibrary.SearchId && !_plIndex.ContainsKey(p.Id)).Select(p => p.Id).ToList();
            if (missing.Count > 0)
            {
                var built = await Task.Run(() => missing.ToDictionary(id => id, id => new HashSet<string>(_library.LoadCachedTracks(id).Select(t => t.Id))));
                foreach (var kv in built) _plIndex[kv.Key] = kv.Value;
            }
            foreach (var p in Items)
                p.SetPlaying(_curId.Length > 0 && _plIndex.TryGetValue(p.Id, out var set) && set.Contains(_curId), _curPlaying);
        }
        catch (Exception ex)
        {
            AppLog.Warn("YouTube", "Playlist-Markierung fehlgeschlagen", ex);
        }
    }

    /// <summary>Öffnet nach dem Start wieder die Playlist, die zuletzt offen war (mit den gespeicherten Titeln).</summary>
    void RestoreView()
    {
        if (_account.State != AccountState.LoggedIn || _view.OpenedId.Length == 0) return;
        _restoringScroll = true;
        var p = Items.FirstOrDefault(i => i.Id == _view.OpenedId) ?? new PlaylistInfo(_view.OpenedId, _view.OpenedTitle, _view.OpenedSub);
        Selected = p;
        _ = OpenPlaylist(keepScroll: true);
    }

    /// <summary>
    /// Die Ansicht meldet sich an und bekommt die gespeicherten Scroll-Stellen. Der Aufruf bekommt (true = Titelliste, Pixel von oben).
    /// Währenddessen werden Scroll-Meldungen nicht gespeichert, sonst überschreibt das Aufbauen der Liste die alte Stelle.
    /// </summary>
    public void AttachScroll(Action<bool, double> apply)
    {
        _applyScroll = apply;
        _restoringScroll = true;
        apply(false, _view.ListOffset);
        if (Opened != null) apply(true, _view.TracksOffset);
        _ = Task.Delay(2500).ContinueWith(_ => Dispatcher.UIThread.Post(() => _restoringScroll = false));
    }

    /// <summary>Merkt die Scroll-Stelle einer Liste (zeitverzögert gespeichert).</summary>
    public void NoteScroll(bool tracks, double offset)
    {
        if (_restoringScroll) return;
        if (tracks) _view.TracksOffset = offset;
        else _view.ListOffset = offset;
        SaveViewSoon();
    }

    void SaveViewSoon()
    {
        _saveCts?.Cancel();
        var cts = _saveCts = new CancellationTokenSource();
        _ = Task.Delay(1500, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Dispatcher.UIThread.Post(SaveView);
        });
    }

    void SaveView()
    {
        // Suchergebnisse sind keine Playlist und werden nicht wieder geöffnet.
        var open = IsSearch ? null : Opened;
        _view.OpenedId = open?.Id ?? "";
        _view.OpenedTitle = open?.Title ?? "";
        _view.OpenedSub = open?.Sub ?? "";
        try { _store.Save("yt-view", _view); }
        catch (Exception)
        {
            // Speichern darf die Oberfläche nie stören.
        }
    }

    public ObservableCollection<PlaylistInfo> Items { get; }
    public ObservableCollection<TrackItem> Tracks { get; } = [];
    public bool HasNotice => Notice.Length > 0;
    public bool CanRefresh => State != LoadState.Loading;
    public bool IsPlaylistOpen => Opened != null;
    public bool CanRefreshTracks => TracksState != LoadState.Loading;

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));
    partial void OnStateChanged(LoadState value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnOpenedChanged(PlaylistInfo? value)
    {
        OnPropertyChanged(nameof(IsPlaylistOpen));
        OnPropertyChanged(nameof(IsSearch));
        OnPropertyChanged(nameof(ArtistVisible));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowPlaylists));
        OnPropertyChanged(nameof(ShowHomePanel));
        SaveView();
        // Titel einer Playlist ändern sich nur, solange sie offen ist: ihren Eintrag danach neu einlesen.
        _plIndex.Remove(_lastOpenedId);
        _plIndex.Remove(value?.Id ?? "");
        _lastOpenedId = value?.Id ?? "";
        UpdatePlayingPlaylists();
    }
    partial void OnTracksStateChanged(LoadState value) => OnPropertyChanged(nameof(CanRefreshTracks));

    void RefreshAccountState()
    {
        var s = _account.State;
        ShowLoggedOut = s == AccountState.LoggedOut;
        ShowLogin = s == AccountState.LoginPanelOpen;
        ShowBusy = s is AccountState.Verifying or AccountState.LoggingOut;
        ShowLoggedIn = s == AccountState.LoggedIn;
        BusyText = s == AccountState.Verifying ? Loc.T("yt.busy.verify") : s == AccountState.LoggingOut ? Loc.T("yt.busy.logout") : "";
        LoginSurface = s == AccountState.LoginPanelOpen ? _host.LoginSurface : null;
        Notice = _account.Notice;
        if (s == AccountState.LoggedOut) CloseOpened();
        else if (s == AccountState.LoggedIn)
        {
            _ = AutoCheckAsync();
            if (HomeMode && NeedsHomeLoad) _ = LoadHomeAsync(reload: false);
        }
    }

    /// <summary>Die Startseite ist leer oder stammt noch aus dem Speicher und wurde in diesem Lauf nicht frisch geladen.</summary>
    bool NeedsHomeLoad => !_homeLoading && (HomeSections.Count == 0 || !_homeFresh);

    /// <summary>Zeigt die gespeicherte Startseite sofort an (frisch geladen wird danach im Hintergrund).</summary>
    void LoadCachedHome()
    {
        try
        {
            var cache = _library.LoadCachedHome();
            if (cache.Sections.Count == 0) return;
            AddHomeSections(cache.Sections);
            foreach (var c in cache.Chips) HomeChips.Add(new HomeChipVm(c.Title, c.Params, false));
            HomeState = LoadState.Ready;
        }
        catch (Exception)
        {
            // Ein kaputter Speicher darf den Start nicht stören.
        }
    }

    /// <summary>Nur für den Oberflächen-Selbsttest: zeigt die Startseite mit Beispieldaten, ohne Konto und ohne Netz.</summary>
    public void ApplyPreview(IEnumerable<ArtistSectionData> sections, IEnumerable<HomeChip> chips)
    {
        ShowLoggedOut = false;
        ShowLogin = false;
        ShowBusy = false;
        ShowLoggedIn = true;
        HomeMode = true;
        HomeSections.Clear();
        AddHomeSections(sections);
        HomeChips.Clear();
        foreach (var c in chips) HomeChips.Add(new HomeChipVm(c.Title, c.Params, c.Selected));
        OnPropertyChanged(nameof(HasHomeChips));
        OnPropertyChanged(nameof(HasHomeTracks));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowHomePanel));
        HomeState = LoadState.Ready;
    }

    /// <summary>Was die Kopfzeile der App in der Mitte zeigt: die Suche, solange man angemeldet ist.</summary>
    public object? HeaderViewModel => ShowLoggedIn ? _header : null;

    partial void OnShowLoggedInChanged(bool value) => OnPropertyChanged(nameof(HeaderViewModel));

    /// <summary>
    /// Zeigt die gespeicherten Playlists sofort und prüft danach einmal pro Start im Hintergrund auf Änderungen
    /// (nur wenn nichts abgespielt wird und die letzte Prüfung älter als 30 Minuten ist). Nur bei Änderung wird die Liste ersetzt.
    /// </summary>
    async Task AutoCheckAsync()
    {
        if (_autoChecked || _account.State != AccountState.LoggedIn) return;
        _autoChecked = true;
        try
        {
            await Task.Delay(5000);
            if (_account.State != AccountState.LoggedIn || State == LoadState.Loading) return;
            if (!HasRealItems)
            {
                // Noch nie geladen: sichtbar laden mit Anzeige.
                await Refresh();
                return;
            }
            if (DateTime.UtcNow - _library.LastChecked < YtLibrary.FreshFor) return;
            _checkCts?.Cancel();
            var cts = _checkCts = new CancellationTokenSource();
            BackgroundBusy = true;
            var result = await _library.CheckForUpdatesAsync(cts.Token);
            if (result is { Changed: true } r && !cts.IsCancellationRequested) ReplaceItems(r.Items);
        }
        catch (Exception)
        {
            // Die Prüfung im Hintergrund darf nie stören. Die gespeicherte Liste bleibt stehen.
        }
        finally
        {
            BackgroundBusy = false;
        }
    }

    void ReplaceItems(List<PlaylistInfo> items)
    {
        var keep = Selected?.Id;
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        EnsureLiked();
        Selected = keep == null ? null : Items.FirstOrDefault(i => i.Id == keep);
        State = HasRealItems ? LoadState.Ready : LoadState.Empty;
    }

    /// <summary>Hat die Liste echte Playlists (nicht nur die feste „Gefällt mir“-Zeile)?</summary>
    bool HasRealItems => Items.Any(i => i.Id != YtLibrary.LikedId);

    /// <summary>Hält die Playlist der gemochten Titel immer ganz oben, auch wenn die Bibliothek sie nicht liefert.</summary>
    void EnsureLiked()
    {
        if (Items.All(i => i.Id != YtLibrary.LikedId))
            Items.Insert(0, new PlaylistInfo(YtLibrary.LikedId, Loc.T("yt.liked"), YtLibrary.LikedSub));
    }

    [RelayCommand] Task SignIn() => Run(_account.SignInAsync);
    [RelayCommand] Task FinishLogin() => Run(_account.FinishLoginAsync);
    [RelayCommand] Task CancelLogin() => Run(_account.CancelLoginAsync);

    /// <summary>Spielt die markierte (oder geöffnete) Playlist von vorn.</summary>
    [RelayCommand]
    Task PlaySelected()
    {
        if ((Opened ?? Selected) is not { } p) return Task.CompletedTask;
        Info = Loc.T("yt.loading");
        return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, UrlNormalizer.WatchPlaylist(p.Id), p.Title)));
    }

    /// <summary>Spielt den markierten Titel der geöffneten Playlist. Danach läuft die Playlist weiter (bei Suchtreffern der Radio-Mix der Seite).</summary>
    [RelayCommand]
    Task PlayTrack()
    {
        if (Opened is not { } p || SelectedTrack is not { } t) return Task.CompletedTask;
        switch (t.Info.Kind)
        {
            case "playlist" or "album":
                // Alben und Playlists aus der Suche öffnen sich wie eine eigene Playlist.
                Selected = new PlaylistInfo(t.Info.Ref, t.Info.Title, t.Info.Artist);
                return OpenPlaylist(keepScroll: false);
            case "artist":
                return OpenArtistAsync(t.Info.Id);
        }
        Info = Loc.T("yt.loading");
        var url = IsSearch ? UrlNormalizer.ShareTrack(t.Info.Id) : UrlNormalizer.WatchTrack(t.Info.Id, p.Id);
        return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, url, t.Title, t.Artist)));
    }

    // ---- Startseite (Empfehlungen) ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowPlaylists), nameof(ShowHomePanel))] bool _homeMode;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HomeMessage))] LoadState _homeState = LoadState.Empty;

    /// <summary>Text der Startseite, solange sie leer ist oder lädt.</summary>
    public string HomeMessage => Loc.T(HomeState == LoadState.Loading ? "yt.home.loading" : "yt.home.empty");
    [ObservableProperty] string _homeError = "";

    public ObservableCollection<ArtistSectionVm> HomeSections { get; } = [];

    string? _homeNext;
    bool _homeLoading;
    bool _homeMoreLoading;
    bool _homeTokenTried;
    bool _homeFresh;
    CancellationTokenSource? _homeCts;

    /// <summary>Die Playlist-Liste ist zu sehen.</summary>
    public bool ShowPlaylists => ShowList && !HomeMode;

    /// <summary>Die Startseite ist zu sehen.</summary>
    public bool ShowHomePanel => ShowList && HomeMode;

    partial void OnHomeModeChanged(bool value)
    {
        _view.HomeMode = value;
        SaveView();
        if (value && _account.State == AccountState.LoggedIn && NeedsHomeLoad) _ = LoadHomeAsync(reload: false);
    }

    // ---- Stimmungen und Schnellzugriff (Datum und Uhrzeit stehen auf der Home-Seite der Shell) ----

    /// <summary>Die Stimmungen oben auf der Startseite (leer, solange YouTube keine liefert).</summary>
    public ObservableCollection<HomeChipVm> HomeChips { get; } = [];

    /// <summary>Wahr, wenn es Stimmungen gibt (sonst blendet die Seite die Reihe aus).</summary>
    public bool HasHomeChips => HomeChips.Count > 0;

    string? _homeChipParams;

    /// <summary>Klick auf eine Stimmung lädt die Startseite dazu, ein zweiter Klick auf dieselbe hebt den Filter auf.</summary>
    [RelayCommand]
    Task SelectChip(HomeChipVm? chip)
    {
        if (chip == null) return Task.CompletedTask;
        _homeChipParams = chip.Selected ? null : chip.Params;
        return LoadHomeAsync(reload: true);
    }

    /// <summary>Der erste Titel in den Zeilen-Bereichen der Startseite (Grundlage für „Mix starten“).</summary>
    TrackItem? FirstHomeTrack() => HomeSections.Where(s => s.IsRows).SelectMany(s => s.Items).FirstOrDefault(i => i.Info.Kind == "");

    /// <summary>Wahr, wenn „Mix starten“ und „Zufällig“ etwas zu spielen haben.</summary>
    public bool HasHomeTracks => FirstHomeTrack() != null;

    /// <summary>„Mix starten“: Radio-Mix des ersten Titels der Startseite.</summary>
    [RelayCommand]
    Task HomeMix() => FirstHomeTrack() is { } t ? StartTrackMixAsync(t.Info) : Task.CompletedTask;

    /// <summary>„Zufällig“: spielt einen zufälligen Titel aus den Zeilen-Bereichen der Startseite.</summary>
    [RelayCommand]
    Task HomeShuffle()
    {
        var tracks = HomeSections.Where(s => s.IsRows).SelectMany(s => s.Items).Where(i => i.Info.Kind == "").ToList();
        if (tracks.Count == 0) return Task.CompletedTask;
        var t = tracks[Random.Shared.Next(tracks.Count)];
        Info = Loc.Current["yt.loading"];
        return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, UrlNormalizer.ShareTrack(t.Info.Id), t.Title, t.Artist)));
    }

    /// <summary>Öffnet „Gefällt mir“ (die Liste der gemochten Titel).</summary>
    [RelayCommand]
    Task OpenLiked()
    {
        Selected = Items.FirstOrDefault(i => i.Id == YtLibrary.LikedId) ?? new PlaylistInfo(YtLibrary.LikedId, Loc.Current["yt.liked"], YtLibrary.LikedSub);
        return OpenPlaylist(keepScroll: false);
    }

    [RelayCommand] void ShowPlaylistsTab() => HomeMode = false;
    [RelayCommand] void ShowHomeTab() => HomeMode = true;
    [RelayCommand] Task ReloadHome() => LoadHomeAsync(reload: true);

    async Task LoadHomeAsync(bool reload)
    {
        _homeCts?.Cancel();
        var cts = _homeCts = new CancellationTokenSource();
        _homeLoading = true;
        var hadContent = HomeSections.Count > 0;
        if (reload || !hadContent) { HomeState = LoadState.Loading; HomeError = ""; }
        try
        {
            // Die gespeicherte Startseite steht schon. Frisch laden erst, wenn die Wiederherstellung der Wiedergabe durch ist,
            // und nie während YouTube spielt: beide teilen sich dieselbe Web-Ansicht, das ließ die Wiedergabe hängen.
            if (hadContent && !reload)
            {
                await Task.Delay(20000, cts.Token);
                if (_library.PlaybackBusy) return;
            }
            var (error, sections, next, chips) = await _library.LoadHomeAsync(null, cts.Token, _homeChipParams);
            for (var retry = 0; error != null && retry < 2 && !cts.IsCancellationRequested && !_library.PlaybackBusy; retry++)
            {
                await Task.Delay(4000, cts.Token);
                (error, sections, next, chips) = await _library.LoadHomeAsync(null, cts.Token, _homeChipParams);
            }
            if (cts.IsCancellationRequested) return;
            if (error != null)
            {
                // Gespeicherte Startseite bleibt stehen, der Fehler erscheint nur bei leerer Seite.
                if (hadContent && !reload) { HomeState = LoadState.Ready; return; }
                HomeError = error;
                HomeState = LoadState.Error;
                return;
            }
            _homeFresh = true;
            _homeTokenTried = false;
            if (_homeChipParams == null) _library.SaveCachedHome(sections, chips);
            HomeSections.Clear();
            AddHomeSections(sections);
            _homeNext = next;
            if (chips.Count > 0 || _homeChipParams == null)
            {
                HomeChips.Clear();
                foreach (var c in chips) HomeChips.Add(new HomeChipVm(c.Title, c.Params, c.Selected));
                OnPropertyChanged(nameof(HasHomeChips));
            }
            OnPropertyChanged(nameof(HasHomeTracks));
            HomeState = LoadState.Ready;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            HomeError = ex.Message;
            HomeState = LoadState.Error;
        }
        finally { _homeLoading = false; }
    }

    void AddHomeSections(IEnumerable<ArtistSectionData> sections)
    {
        foreach (var s in sections)
            HomeSections.Add(new ArtistSectionVm(s.Title, [.. s.Items.Select(i => new TrackItem(i, _thumbs, s.Layout == SectionLayout.Cards ? TrackItem.CardWidth : TrackItem.ListWidth))], s.Layout));
    }

    /// <summary>
    /// Hängt weitere Bereiche der Startseite an, wenn man ans Ende scrollt (wie bei der Suche).
    /// Steht nur die gespeicherte Startseite da (die frische Antwort wartet oder wird während der Wiedergabe nicht geholt), fehlt das
    /// Weiter-Zeichen: dann wird einmal die erste Seite geholt, nur um es zu bekommen. Die angezeigten Bereiche bleiben dabei stehen.
    /// </summary>
    public async Task LoadMoreHomeAsync()
    {
        if (!ShowHomePanel || _homeMoreLoading) return;
        var token = _homeNext;
        var tokenOnly = token == null;
        if (tokenOnly && (_homeFresh || _homeTokenTried || HomeSections.Count == 0)) return;
        _homeTokenTried |= tokenOnly;
        _homeMoreLoading = true;
        BackgroundBusy = true;
        var cts = _homeCts;
        try
        {
            var (error, sections, next, _) = await _library.LoadHomeAsync(token, cts?.Token ?? CancellationToken.None);
            if (cts?.IsCancellationRequested == true) return;
            if (error != null) { Info = error; _homeNext = null; return; }
            _homeNext = next;
            if (!tokenOnly) AddHomeSections(sections);
            AppLog.Info("YouTube", tokenOnly ? "Startseite: Weiter-Zeichen geholt" : $"Startseite: {sections.Count} weitere Bereiche");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Info = Loc.F("yt.home.moreFailed", ex.Message); _homeNext = null; }
        finally
        {
            _homeMoreLoading = false;
            BackgroundBusy = false;
        }
    }

    // ---- Künstler-Seite ----

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ArtistVisible), nameof(ShowList), nameof(ShowPlaylists), nameof(ShowHomePanel))] bool _artistOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubscribeText))] ArtistPage? _artist;
    [ObservableProperty] LoadState _artistState = LoadState.Empty;
    [ObservableProperty] string _artistError = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DescriptionLines), nameof(DescriptionToggleText))] bool _descriptionExpanded;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubscribeText))] bool _subscribed;

    readonly Stack<ArtistPage> _artistBack = [];
    CancellationTokenSource? _artistCts;

    public ObservableCollection<TrackItem> ArtistSongs { get; } = [];
    public ObservableCollection<ArtistSectionVm> ArtistSections { get; } = [];

    /// <summary>Die Künstler-Seite ist zu sehen (nicht, solange eine Playlist darüber geöffnet ist).</summary>
    public bool ArtistVisible => ArtistOpen && Opened == null;

    /// <summary>Die Liste der eigenen Playlists ist zu sehen.</summary>
    public bool ShowList => !ArtistOpen && Opened == null;

    public int DescriptionLines => DescriptionExpanded ? 0 : 3;
    public string DescriptionToggleText => Loc.T(DescriptionExpanded ? "yt.artist.less" : "yt.artist.more");
    public bool HasArtistSongs => ArtistSongs.Count > 0;

    public string SubscribeText => Subscribed ? Loc.T("yt.artist.subscribedBtn") : Artist is { Subscribers.Length: > 0 } a ? Loc.F("yt.artist.subscribeCount", a.Subscribers) : Loc.T("yt.artist.subscribe");

    [RelayCommand] void ToggleDescription() => DescriptionExpanded = !DescriptionExpanded;

    /// <summary>Das Plugin setzt hier das Umschalten auf die YouTube-Seite ein (wichtig, wenn die Künstler-Seite aus der Player-Leiste geöffnet wird).</summary>
    public Action? ShowPage { get; set; }

    /// <summary>
    /// Öffnet die Künstler-Seite zu einem Titel. Mit Kennung direkt, sonst wird der Name gesucht und der passendste Künstler genommen
    /// (bei mehreren Künstlern „A, B“ oder „A &amp; B“ der erste).
    /// </summary>
    public async Task OpenArtistOfAsync(string? artistId, string artistName)
    {
        ShowPage?.Invoke();
        if (!string.IsNullOrEmpty(artistId)) { await OpenArtistAsync(artistId); return; }
        var name = artistName.Split([", ", " & ", " feat.", " x "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (name.Length == 0) { Info = Loc.T("yt.artist.unknown"); return; }
        Info = Loc.F("yt.artist.searching", name);
        var (error, items, _) = await _library.SearchAsync(name, "artist", CancellationToken.None);
        if (error != null) { Info = error; return; }
        var hit = items.FirstOrDefault(i => string.Equals(i.Title, name, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        if (hit == null) { Info = Loc.F("yt.artist.notFound", name); return; }
        Info = "";
        await OpenArtistAsync(hit.Id);
    }

    /// <summary>Öffnet die Seite eines Künstlers. Kam man von einer anderen Künstler-Seite, führt „Zurück“ dorthin.</summary>
    public async Task OpenArtistAsync(string browseId)
    {
        _artistId = browseId;
        if (Opened != null) CloseOpened();
        if (ArtistOpen && Artist != null) _artistBack.Push(Artist);
        else if (!ArtistOpen) _artistBack.Clear();
        _artistCts?.Cancel();
        var cts = _artistCts = new CancellationTokenSource();
        ArtistOpen = true;
        ArtistState = LoadState.Loading;
        ArtistError = "";
        try
        {
            var (error, page) = await _library.LoadArtistAsync(browseId, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (error != null || page == null)
            {
                ArtistError = error ?? Loc.T("yt.artist.loadFailed");
                ArtistState = LoadState.Error;
                return;
            }
            ApplyArtist(page);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested) return;
            ArtistError = ex.Message;
            ArtistState = LoadState.Error;
        }
    }

    [ObservableProperty] Avalonia.Media.Imaging.Bitmap? _artistCover;

    /// <summary>Lädt das große Bild des Künstlers im Hintergrund (aus dem Bild-Cache).</summary>
    async Task LoadArtistCoverAsync(string url, string forId)
    {
        try
        {
            var bytes = await _thumbs.GetAsync(url);
            if (bytes == null) return;
            var bmp = await Task.Run(() =>
            {
                try { using var ms = new MemoryStream(bytes); return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 360); }
                catch (Exception) { return null; }
            });
            if (bmp != null && Artist?.Id == forId) ArtistCover = bmp;
        }
        catch (Exception)
        {
            // Ohne Bild bleibt die Fläche leer.
        }
    }

    void ApplyArtist(ArtistPage p)
    {
        Artist = p;
        ArtistCover = null;
        _ = LoadArtistCoverAsync(p.Thumb, p.Id);
        Subscribed = p.Subscribed;
        DescriptionExpanded = false;
        ArtistSongs.Clear();
        foreach (var t in p.TopSongs) ArtistSongs.Add(new TrackItem(t, _thumbs));
        ArtistSections.Clear();
        foreach (var s in p.Sections) ArtistSections.Add(new ArtistSectionVm(s.Title, [.. s.Items.Select(i => new TrackItem(i, _thumbs, TrackItem.CardWidth))]));
        OnPropertyChanged(nameof(HasArtistSongs));
        ArtistState = LoadState.Ready;
    }

    /// <summary>„Zurück“ auf der Künstler-Seite: zur vorigen Künstler-Seite oder zur Liste.</summary>
    [RelayCommand]
    void BackFromArtist()
    {
        _artistCts?.Cancel();
        if (_artistBack.Count > 0 && ArtistState == LoadState.Ready) { ApplyArtist(_artistBack.Pop()); return; }
        _artistBack.Clear();
        ArtistOpen = false;
        Artist = null;
        ArtistSongs.Clear();
        ArtistSections.Clear();
        ArtistState = LoadState.Empty;
    }

    string _artistId = "";

    [RelayCommand] Task ReloadArtist() => _artistId.Length > 0 ? OpenArtistAfterReload(_artistId) : Task.CompletedTask;

    /// <summary>Lädt dieselbe Seite nochmal, ohne sie auf den Zurück-Stapel zu legen.</summary>
    Task OpenArtistAfterReload(string id)
    {
        Artist = null;
        ArtistOpen = false;
        return OpenArtistAsync(id);
    }

    /// <summary>Eine Karte oder Zeile der Künstler-Seite wurde gewählt: Künstler öffnen, Album/Playlist öffnen, Titel spielen.</summary>
    public Task OpenCardAsync(TrackItem t)
    {
        switch (t.Info.Kind)
        {
            case "artist":
                return OpenArtistAsync(t.Info.Id);
            case "album" or "playlist":
                Selected = new PlaylistInfo(t.Info.Ref, t.Info.Title, t.Info.Artist);
                return OpenPlaylist(keepScroll: false);
            default:
                Info = Loc.T("yt.loading");
                return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, UrlNormalizer.ShareTrack(t.Info.Id), t.Title, t.Artist)));
        }
    }

    /// <summary>„Zufallsmix“ des Künstlers (die Playlist der Seite, sonst der Mix zum ersten Top-Song).</summary>
    [RelayCommand]
    Task ArtistShuffle() => PlayArtistUrl(Artist?.PlayPlaylist is { Length: > 0 } p ? UrlNormalizer.WatchPlaylist(p) : null);

    /// <summary>„Mix“ des Künstlers (Radio).</summary>
    [RelayCommand]
    Task ArtistMix() => PlayArtistUrl(Artist?.RadioPlaylist is { Length: > 0 } p ? UrlNormalizer.WatchPlaylist(p) : null);

    Task PlayArtistUrl(string? url)
    {
        if (Artist is not { } a) return Task.CompletedTask;
        url ??= a.TopSongs.Count > 0 ? UrlNormalizer.RadioMix(a.TopSongs[0].Id) : null;
        if (url == null) { Info = Loc.T("yt.artist.noMix"); return Task.CompletedTask; }
        Info = Loc.T("yt.loading");
        return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, url, a.Name)));
    }

    /// <summary>„Alle anzeigen“ bei den Top-Songs: öffnet die Playlist mit allen Titeln des Künstlers.</summary>
    [RelayCommand]
    Task ArtistAllSongs()
    {
        if (Artist is not { TopSongsMore.Length: > 0 } a) return Task.CompletedTask;
        Selected = new PlaylistInfo(a.TopSongsMore, Loc.F("yt.artist.allSongs", a.Name), "");
        return OpenPlaylist(keepScroll: false);
    }

    [RelayCommand]
    Task ToggleSubscribe()
    {
        if (Artist is not { } a) return Task.CompletedTask;
        var want = !Subscribed;
        return Edit(want ? Loc.F("yt.artist.subscribing", a.Name) : Loc.F("yt.artist.unsubscribing", a.Name),
            want ? Loc.F("yt.artist.subscribed", a.Name) : Loc.F("yt.artist.unsubscribed", a.Name), () => _library.SetSubscribedAsync(a.Id, want), () => Subscribed = want);
    }

    [RelayCommand]
    Task ArtistShare() => Artist is { } a ? ShareAsync("https://music.youtube.com/channel/" + Uri.EscapeDataString(a.Id)) : Task.CompletedTask;

    [RelayCommand]
    void ArtistOnYoutube()
    {
        if (Artist is { } a) OpenOnYoutube("https://www.youtube.com/channel/" + Uri.EscapeDataString(a.Id));
    }

    [ObservableProperty] string _searchText = "";
    [ObservableProperty] int _searchFilterIndex;

    static readonly string[] FilterKinds = ["", "artist", "album", "playlist"];

    /// <summary>Die Arten, nach denen man filtern kann (Titel, Künstler, Alben, Playlists).</summary>
    public string[] FilterNames { get; } = [Loc.T("yt.filter.songs"), Loc.T("yt.filter.artists"), Loc.T("yt.filter.albums"), Loc.T("yt.filter.playlists")];

    string? _searchNext;
    string _searchKind = "";
    bool _loadingMore;

    /// <summary>Zeigt die Titelliste gerade Suchergebnisse (keine echte Playlist)?</summary>
    public bool IsSearch => Opened?.Id == YtLibrary.SearchId;

    string _searchQuery = "";
    bool _settingFilter;

    /// <summary>Setzt den Filter, ohne dass dadurch eine neue Suche startet.</summary>
    void SetFilter(int index)
    {
        _settingFilter = true;
        SearchFilterIndex = index;
        _settingFilter = false;
    }

    /// <summary>Der Filter ist erst in den Treffern sichtbar. Ein Wechsel dort sucht dasselbe nochmal mit der neuen Art.</summary>
    partial void OnSearchFilterIndexChanged(int value)
    {
        if (!_settingFilter && IsSearch && _searchQuery.Length > 0) _ = RunSearch(_searchQuery);
    }

    /// <summary>Neue Suche aus dem Suchfeld. Sie beginnt immer bei „Titel“, danach lässt sich in den Treffern filtern.</summary>
    [RelayCommand]
    Task Search()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) return Task.CompletedTask;
        SetFilter(0);
        AddHistory(q);
        Suggestions.Clear();
        return RunSearch(q);
    }

    // ---- Suchvorschläge und Suchverlauf ----

    CancellationTokenSource? _suggestCts;

    /// <summary>Vorschläge unter dem Suchfeld: passende frühere Suchen zuerst, dann die von YouTube. Bei leerem Feld der Verlauf.</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    partial void OnSearchTextChanged(string value) => _ = UpdateSuggestionsAsync(value);

    async Task UpdateSuggestionsAsync(string text)
    {
        _suggestCts?.Cancel();
        var cts = _suggestCts = new CancellationTokenSource();
        try
        {
            var q = text.Trim();
            var history = _view.SearchHistory
                .Where(h => q.Length == 0 || h.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .Take(q.Length == 0 ? 8 : 4).ToList();
            SetSuggestions(history);
            if (q.Length < 2) return;
            await Task.Delay(250, cts.Token);
            var remote = await YtSuggest.GetAsync(q, 6, cts.Token);
            if (cts.IsCancellationRequested) return;
            SetSuggestions([.. history, .. remote.Where(r => !history.Contains(r, StringComparer.CurrentCultureIgnoreCase))]);
        }
        catch (OperationCanceledException) { }
    }

    void SetSuggestions(List<string> items)
    {
        // Nur bei Änderung austauschen, sonst klappt die Liste beim Tippen immer wieder zu.
        if (Suggestions.SequenceEqual(items)) return;
        Suggestions.Clear();
        foreach (var s in items) Suggestions.Add(s);
    }

    void AddHistory(string q)
    {
        _view.SearchHistory.RemoveAll(h => string.Equals(h, q, StringComparison.CurrentCultureIgnoreCase));
        _view.SearchHistory.Insert(0, q);
        if (_view.SearchHistory.Count > 20) _view.SearchHistory.RemoveRange(20, _view.SearchHistory.Count - 20);
        SaveView();
    }

    /// <summary>Löscht den Suchverlauf (Knopf in den Einstellungen).</summary>
    public void ClearSearchHistory()
    {
        _view.SearchHistory.Clear();
        Suggestions.Clear();
        SaveView();
        Info = Loc.T("yt.search.historyCleared");
    }

    /// <summary>Sucht bei YouTube Music (nach Titeln, Künstlern, Alben oder Playlists) und zeigt die Treffer in der Titelliste.</summary>
    async Task RunSearch(string q)
    {
        _searchQuery = q;
        // Merkt, wo man vor der Suche war (Playlist oder Startseite), damit „Zurück“ dorthin zurückführt.
        if (!IsSearch) _preSearch = Opened;
        _tracksCts?.Cancel();
        var cts = _tracksCts = new CancellationTokenSource();
        _searchKind = FilterKinds[Math.Clamp(SearchFilterIndex, 0, FilterKinds.Length - 1)];
        _searchNext = null;
        Opened = new PlaylistInfo(YtLibrary.SearchId, $"Suche ({FilterNames[Array.IndexOf(FilterKinds, _searchKind)]}): {q}", "");
        Tracks.Clear();
        SelectedTrack = null;
        TracksState = LoadState.Loading;
        TracksMessage = Loc.T("yt.search.busy");
        try
        {
            var (error, items, next) = await _library.SearchAsync(q, _searchKind, cts.Token);
            if (cts.IsCancellationRequested || !IsSearch) return;
            if (error != null) { TracksError = error; TracksState = LoadState.Error; return; }
            _searchNext = next;
            SetTracks(items);
            if (items.Count == 0) { TracksState = LoadState.Empty; TracksMessage = Loc.T("yt.search.none"); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsSearch) return;
            TracksError = ex.Message;
            TracksState = LoadState.Error;
        }
    }

    /// <summary>
    /// Lädt die nächste Seite Suchtreffer und hängt sie unten an. Die Ansicht ruft das auf, sobald man das Listenende erreicht
    /// (unendliches Nachladen). Tut nichts, wenn es nichts mehr gibt oder schon geladen wird.
    /// </summary>
    public async Task LoadMoreAsync()
    {
        if (!IsSearch || _searchNext is not { } token || _loadingMore) return;
        _loadingMore = true;
        BackgroundBusy = true;
        var cts = _tracksCts;
        try
        {
            var (error, items, next) = await _library.SearchMoreAsync(token, _searchKind, cts?.Token ?? CancellationToken.None);
            if (cts?.IsCancellationRequested == true || !IsSearch) return;
            if (error != null) { Info = error; _searchNext = null; return; }
            _searchNext = next;
            foreach (var t in items)
                if (Tracks.All(x => x.Info.Id != t.Id)) Tracks.Add(new TrackItem(t, _thumbs));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Info = Loc.F("yt.search.moreFailed", ex.Message); _searchNext = null; }
        finally
        {
            _loadingMore = false;
            BackgroundBusy = false;
        }
    }

    /// <summary>Die Ansicht setzt hier das Kopieren in die Zwischenablage ein (Teilen).</summary>
    public Func<string, Task>? CopyText { get; set; }

    /// <summary>Die Ansicht setzt hier das Öffnen einer Adresse im Browser ein.</summary>
    public Action<string>? OpenUrl { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDeleteAsk), nameof(DeleteAskText))] PlaylistInfo? _deleteAsk;
    bool _editing;

    public bool HasDeleteAsk => DeleteAsk != null;
    public string DeleteAskText => DeleteAsk == null ? "" : Loc.F("yt.delete.ask", DeleteAsk.Title);

    /// <summary>Spielt den Mix zu einem Titel (Titel in ähnlichem Stil).</summary>
    public Task StartTrackMixAsync(TrackInfo t)
    {
        Info = Loc.T("yt.loading");
        return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, UrlNormalizer.RadioMix(t.Id), t.Title, t.Artist)));
    }

    /// <summary>Spielt den Mix zu einer ganzen Playlist.</summary>
    public Task StartPlaylistMixAsync(PlaylistInfo p)
    {
        Info = Loc.T("yt.loading");
        return Run(() => _player.PlayAsync(new MediaItem(YtPlaybackSource.SourceKey, UrlNormalizer.PlaylistRadio(p.Id), p.Title)));
    }

    /// <summary>Kopiert den Link in die Zwischenablage.</summary>
    public async Task ShareAsync(string url)
    {
        if (CopyText == null) return;
        try { await CopyText(url); Info = Loc.T("yt.link.copied"); }
        catch (Exception ex) { Info = Loc.F("yt.link.copyFailed", ex.Message); }
    }

    /// <summary>Öffnet eine Adresse im Browser.</summary>
    public void OpenOnYoutube(string url) => OpenUrl?.Invoke(url);

    /// <summary>Fügt einen Titel einer Playlist hinzu.</summary>
    public Task AddTrackToAsync(TrackInfo t, PlaylistInfo target) =>
        Edit(Loc.F("yt.edit.adding", t.Title, target.Title), Loc.F("yt.edit.added", t.Title, target.Title), () => _library.AddTrackAsync(target.Id, t), () =>
        {
            // Die Liste sofort lokal ergänzen, ohne erneut bei YouTube nachzulesen.
            if (Opened?.Id == target.Id && Tracks.All(x => x.Info.Id != t.Id))
            {
                Tracks.Add(new TrackItem(YtLibrary.WithThumb(t), _thumbs));
                TracksState = LoadState.Ready;
            }
            BumpCount(target.Id, 1);
        });

    /// <summary>Fügt alle Titel einer Playlist einer anderen hinzu.</summary>
    public Task AddPlaylistToAsync(PlaylistInfo source, PlaylistInfo target) =>
        Edit(Loc.F("yt.edit.adding", source.Title, target.Title), Loc.F("yt.edit.addedList", source.Title, target.Title), () => _library.AddPlaylistAsync(target.Id, source.Id), () =>
        {
            var added = _library.LoadCachedTracks(source.Id).Count;
            if (Opened?.Id == target.Id && _library.LoadCachedTracks(target.Id) is { Count: > 0 } now) SetTracks(now);
            BumpCount(target.Id, added);
        });

    /// <summary>Ändert die Zahl in der Zeile unter dem Playlist-Namen (z. B. „12 Titel“) um <paramref name="delta"/>, ohne neu bei YouTube zu lesen.</summary>
    void BumpCount(string playlistId, int delta)
    {
        var i = -1;
        for (var k = 0; k < Items.Count; k++) if (Items[k].Id == playlistId) { i = k; break; }
        if (i < 0 || delta == 0) return;
        var old = Items[i];
        var m = System.Text.RegularExpressions.Regex.Match(old.Sub, @"\d[\d.]*");
        if (!m.Success || !int.TryParse(m.Value.Replace(".", ""), out var n)) return;
        var updated = old with { Sub = old.Sub[..m.Index] + Math.Max(0, n + delta).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.') + old.Sub[(m.Index + m.Length)..] };
        var keep = Selected?.Id;
        Items[i] = updated;
        Selected = keep == playlistId ? updated : Items.FirstOrDefault(x => x.Id == keep);
        if (Opened?.Id == playlistId) Opened = updated;
        _library.SaveCachedPlaylists(Items);
    }

    /// <summary>Setzt „Gefällt mir“ eines Titels der Liste oder nimmt es zurück.</summary>
    public Task LikeTrackAsync(TrackInfo t, bool like) =>
        Edit(like ? Loc.F("yt.edit.liking", t.Title) : Loc.F("yt.edit.unliking", t.Title),
            like ? Loc.F("yt.edit.liked", t.Title) : Loc.F("yt.edit.unliked", t.Title), () => _library.SetLikeAsync(t.Id, like), null);

    /// <summary>Entfernt einen Titel aus der geöffneten Playlist.</summary>
    public Task RemoveTrackAsync(TrackItem t)
    {
        if (Opened is not { } p) return Task.CompletedTask;
        return Edit(Loc.F("yt.edit.removing", t.Title), Loc.F("yt.edit.removed", t.Title, p.Title), () => _library.RemoveTrackAsync(p.Id, t.Info), () =>
        {
            Tracks.Remove(t);
            _library.SaveCachedTracks(p.Id, Tracks.Select(x => x.Info));
            BumpCount(p.Id, -1);
            if (Tracks.Count == 0) { TracksState = LoadState.Empty; TracksMessage = Loc.T("yt.songs.none"); }
        });
    }

    /// <summary>Legt eine neue Playlist an und zeigt sie oben in der Liste.</summary>
    public Task CreatePlaylistAsync(string name, string? videoId)
    {
        PlaylistInfo? created = null;
        return Edit(Loc.F("yt.edit.creating", name), Loc.F("yt.edit.created", name), async () =>
        {
            var (error, p) = await _library.CreatePlaylistAsync(name, videoId);
            created = p;
            return error;
        }, () =>
        {
            if (created == null) return;
            Items.Insert(0, created);
            _library.SaveCachedPlaylists(Items);
            State = LoadState.Ready;
        });
    }

    /// <summary>Fragt vor dem Löschen nach (die Leiste unten zeigt „Löschen“ und „Abbrechen“).</summary>
    public void AskDelete(PlaylistInfo p) => DeleteAsk = p;

    [RelayCommand] void CancelDelete() => DeleteAsk = null;

    [RelayCommand]
    Task ConfirmDelete()
    {
        if (DeleteAsk is not { } p) return Task.CompletedTask;
        DeleteAsk = null;
        return Edit(Loc.F("yt.edit.deleting", p.Title), Loc.F("yt.edit.deleted", p.Title), () => _library.DeletePlaylistAsync(p.Id), () =>
        {
            if (Opened?.Id == p.Id) CloseOpened();
            Items.Remove(Items.FirstOrDefault(i => i.Id == p.Id)!);
            if (Selected?.Id == p.Id) Selected = null;
            _library.SaveCachedPlaylists(Items);
            State = HasRealItems ? LoadState.Ready : LoadState.Empty;
        });
    }

    /// <summary>Führt eine Änderung bei YouTube aus (eine zur selben Zeit) und meldet das Ergebnis unten in der Info-Zeile.</summary>
    async Task Edit(string working, string done, Func<Task<string?>> action, Action? onSuccess)
    {
        if (_editing) { Info = Loc.T("yt.edit.busy"); return; }
        _editing = true;
        Info = working;
        try
        {
            var error = await action();
            if (error != null) { Info = error; return; }
            // Die Änderung bei YouTube ist durch. Ein Fehler beim lokalen Nachziehen der Anzeige darf das nicht verdecken.
            try { onSuccess?.Invoke(); }
            catch (Exception ex) { Info = Loc.F("yt.edit.notRefreshed", done, ex.Message); return; }
            Info = done;
        }
        finally { _editing = false; }
    }

    /// <summary>Öffnet die markierte Playlist: zeigt sofort die gespeicherten Titel und prüft im Hintergrund auf Änderungen.</summary>
    [RelayCommand]
    Task OpenPlaylist() => OpenPlaylist(keepScroll: false);

    async Task OpenPlaylist(bool keepScroll)
    {
        if (Selected is not { } p) return;
        if (!keepScroll) _view.TracksOffset = 0;
        _tracksCts?.Cancel();
        var cts = _tracksCts = new CancellationTokenSource();
        Opened = p;
        Tracks.Clear();
        SelectedTrack = null;
        var cached = _library.LoadCachedTracks(p.Id);
        SetTracks(cached);
        if (cached.Count == 0)
        {
            TracksState = LoadState.Loading;
            TracksMessage = Loc.T("yt.songs.loading");
        }
        // Die Titel kommen über die Schnittstelle, ohne die Musik zu stören. Nur wenn das nicht klappt und gerade etwas spielt,
        // wird nicht per Seitenwechsel gelesen (das würde die Musik unterbrechen): dann bleibt der Hinweis „Aktualisieren“.
        await LoadTracks(p, cts, background: cached.Count > 0 || _library.PlaybackBusy);
    }

    [RelayCommand]
    Task RefreshTracks()
    {
        if (Opened is not { } p) return Task.CompletedTask;
        _tracksCts?.Cancel();
        var cts = _tracksCts = new CancellationTokenSource();
        TracksState = LoadState.Loading;
        TracksMessage = Loc.T("yt.songs.loading");
        return LoadTracks(p, cts, background: false);
    }

    PlaylistInfo? _preSearch;

    [RelayCommand]
    void Back()
    {
        // Aus der Suche zurück: wieder in die Playlist, in der man vorher war (sonst zur Startseite).
        if (IsSearch && _preSearch is { } prev)
        {
            _preSearch = null;
            Selected = prev;
            _ = OpenPlaylist(keepScroll: false);
            return;
        }
        CloseOpened();
    }

    void CloseOpened()
    {
        _tracksCts?.Cancel();
        Opened = null;
        Tracks.Clear();
        SelectedTrack = null;
    }

    async Task LoadTracks(PlaylistInfo p, CancellationTokenSource cts, bool background)
    {
        try
        {
            if (background) BackgroundBusy = true;
            var progress = new Progress<string>(m => TracksMessage = m);
            var items = await _library.RefreshTracksAsync(p.Id, background, progress, cts.Token);
            if (cts.IsCancellationRequested || Opened?.Id != p.Id) return;
            if (items == null)
            {
                // Im Hintergrund nicht geprüft (es spielt gerade etwas): gespeicherte Liste bleibt.
                if (Tracks.Count == 0) { TracksState = LoadState.Empty; TracksMessage = Loc.T("yt.songs.refreshHint"); }
                else TracksState = LoadState.Ready;
                return;
            }
            var current = Tracks.Select(t => t.Info).ToList();
            if (items.Count > 0 && !YtLibrary.SameTracks(current, items)) SetTracks(items);
            else if (items.Count == 0 && Tracks.Count == 0) { TracksState = LoadState.Empty; TracksMessage = Loc.T("yt.songs.none"); }
            else TracksState = LoadState.Ready;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (Opened?.Id != p.Id) return;
            if (Tracks.Count > 0) { TracksState = LoadState.Ready; Info = Loc.F("yt.songs.refreshFailed", ex.Message); }
            else { TracksError = ex.Message; TracksState = LoadState.Error; }
        }
        finally
        {
            if (background) BackgroundBusy = false;
        }
    }

    void SetTracks(List<TrackInfo> items)
    {
        Tracks.Clear();
        foreach (var t in items) Tracks.Add(new TrackItem(t, _thumbs));
        TracksState = Tracks.Count == 0 ? LoadState.Empty : LoadState.Ready;
        if (Tracks.Count == 0) TracksMessage = Loc.T("yt.songs.none");
    }

    [RelayCommand]
    async Task Refresh()
    {
        _refreshCts?.Cancel();
        var cts = _refreshCts = new CancellationTokenSource();
        State = LoadState.Loading;
        Message = Loc.T("yt.playlists.loading");
        Info = "";
        try
        {
            var progress = new Progress<string>(m => Message = m);
            var (items, info) = await _library.RefreshAsync(progress, cts.Token);
            if (cts.IsCancellationRequested) return;
            ReplaceItems(items);
            Info = info;
            Message = Loc.T("yt.playlists.none");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            State = LoadState.Error;
        }
    }

    async Task Run(Func<Task> action)
    {
        try
        {
            await action();
            if (Info == Loc.T("yt.loading")) Info = "";
        }
        catch (Exception ex)
        {
            AppLog.Error("YouTube", "Aktion fehlgeschlagen", ex);
            Info = "Fehler: " + ex.Message;
        }
        Notice = _account.Notice;
    }
}
