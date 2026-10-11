using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Modules.RadioPlayer.Data;
using YTSPlayer.Modules.RadioPlayer.Playback;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.RadioPlayer.ViewModels;

/// <summary>Radio-Seite: Sender suchen, Favoriten und Verlauf, Stream-Adresse per Hand. Alle Abfragen laufen asynchron.</summary>
public sealed partial class RadioViewModel : ObservableObject, IDisposable, YTSPlayer.Contracts.Plugins.IHeaderContent
{
    RadioSearchHeader? _header;

    /// <summary>Die Sendersuche in der Kopfzeile der App.</summary>
    public object? HeaderViewModel => _header ??= new RadioSearchHeader(this);

    static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan CountriesTtl = TimeSpan.FromDays(7);

    readonly RadioStore _store;
    readonly RadioBrowserClient _client;
    readonly IPlayer _player;
    readonly RadioStreamSource _source;
    readonly IPerfTracker _perf;
    readonly IThumbnailCache _thumbs;
    static readonly RadioCountry AllCountries = new(Loc.T("radio.countries.all"), "", 0);
    List<RadioStation> _results = [];
    List<RadioCountry> _allCountries = [];
    bool _applyingFilter;
    CancellationTokenSource? _searchCts;

    [ObservableProperty] string _searchText = "";
    [ObservableProperty] string _genre = "";
    [ObservableProperty] string _countryFilter = "";
    [ObservableProperty] RadioCountry? _selectedCountry;
    [ObservableProperty] LoadState _searchState = LoadState.Idle;
    [ObservableProperty] string _searchMessage = Loc.T("radio.search.none");
    [ObservableProperty] string _errorText = "";
    [ObservableProperty] string _noticeText = "";
    [ObservableProperty] bool _showResults;
    [ObservableProperty] bool _hasFavorites;
    [ObservableProperty] bool _hasRecent;

    public ObservableCollection<RadioCountry> Countries { get; } = [];
    public ObservableCollection<StationItem> Results { get; } = [];
    public ObservableCollection<StationItem> Favorites { get; } = [];
    public ObservableCollection<StationItem> Recent { get; } = [];

    public RadioViewModel(RadioStore store, RadioBrowserClient client, IPlayer player, RadioStreamSource source, IPerfTracker perf, IThumbnailCache thumbs, YTSPlayer.Contracts.Settings.ISettingsStore viewStore)
    {
        _viewStore = viewStore;
        _store = store;
        _client = client;
        _player = player;
        _source = source;
        _perf = perf;
        _thumbs = thumbs;
        _source.StatusChanged += OnStatusChanged;
        _player.StateChanged += OnPlayerState;
        OnPlayerState(_player.State);
    }

    /// <summary>Lädt Favoriten und Verlauf sofort. Die Länderliste holt es im Hintergrund, wenn sie älter als eine Woche ist.</summary>
    public void Initialize()
    {
        RefreshLists();
        ReloadCountries();
        RestoreView();
        var stale = _store.CountriesUpdated is not { } updated || DateTime.UtcNow - updated > CountriesTtl;
        if (stale) _ = LoadCountriesAsync();
    }

    readonly YTSPlayer.Contracts.Settings.ISettingsStore _viewStore;

    /// <summary>Stellt Land, Genre und Suchtext der letzten Sitzung wieder her (die Suche läuft danach von selbst).</summary>
    void RestoreView()
    {
        try
        {
            var s = _viewStore.Load<RadioViewState>("radio-view");
            if (s.CountryCode.Length > 0 && Countries.FirstOrDefault(c => c.Code == s.CountryCode) is { } country) SelectedCountry = country;
            Genre = s.Genre;
            SearchText = s.SearchText;
        }
        catch (Exception)
        {
            // Ein kaputter Speicher: Start mit den Favoriten.
        }
    }

    void SaveView()
    {
        try
        {
            _viewStore.Save("radio-view", new RadioViewState { SearchText = SearchText, Genre = Genre, CountryCode = SelectedCountry?.Code ?? "" });
        }
        catch (Exception)
        {
            // Speichern darf die Oberfläche nie stören.
        }
    }

    public void Dispose()
    {
        _source.StatusChanged -= OnStatusChanged;
        _player.StateChanged -= OnPlayerState;
        _searchCts?.Cancel();
    }

    partial void OnSearchTextChanged(string value) => _ = ScheduleSearchAsync();

    partial void OnGenreChanged(string value) => _ = ScheduleSearchAsync();

    RadioCountry? _lastCountry;

    partial void OnSelectedCountryChanged(RadioCountry? value)
    {
        // Die Liste im Dropdown schreibt beim Leeren/Schließen null in die Auswahl. Dann gilt wieder das letzte Land (sonst „Alle Länder“).
        if (value == null)
        {
            Dispatcher.UIThread.Post(() => SelectedCountry ??= _lastCountry ?? Countries.FirstOrDefault());
            return;
        }
        _lastCountry = value;
        // Ändert der Länderfilter die Auswahl, sucht ApplyCountryFilter selbst neu, wenn sich das Land wirklich geändert hat.
        if (!_applyingFilter) _ = ScheduleSearchAsync();
    }

    partial void OnCountryFilterChanged(string value) => ApplyCountryFilter();

    [RelayCommand]
    Task RetrySearchAsync() => ScheduleSearchAsync();

    /// <summary>Zurück zur normalen Ansicht: Suche, Genre und Land zurücksetzen, Favoriten und Verlauf zeigen.</summary>
    [RelayCommand]
    void ShowFavorites()
    {
        _searchCts?.Cancel();
        CountryFilter = "";
        SearchText = "";
        Genre = "";
        SelectedCountry = Countries[0];
        _results = [];
        Results.Clear();
        ShowResults = false;
        SearchState = LoadState.Idle;
    }

    /// <summary>Fügt eine Stream-Adresse (oder eine Playlist) zu den Favoriten hinzu und spielt sie.</summary>
    public async Task AddByUrlAsync(string input)
    {
        var address = input.Trim();
        if (!RadioUrls.IsHttp(address))
        {
            NoticeText = "Bitte eine Adresse mit http:// oder https:// eingeben.";
            return;
        }
        try
        {
            var streamUrl = RadioUrls.IsPlaylist(address)
                ? PlaylistResolver.FirstStreamUrl(await _client.DownloadPlaylistAsync(address, CancellationToken.None))
                : address;
            if (streamUrl == null)
            {
                NoticeText = Loc.T("radio.add.noStream");
                return;
            }
            var known = _store.Find(streamUrl);
            var station = known != null ? ToStation(known) : new RadioStation(RadioIds.NewManual(), new Uri(address).Host, streamUrl);
            NoticeText = "";
            _store.SetFavorite(station, true);
            RefreshLists();
            await PlayAsync(station, Favorites.Select(i => i.Station).ToList());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            NoticeText = Loc.T("radio.add.failed");
        }
    }

    async Task ScheduleSearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(SearchDelay, cts.Token);
            await SearchAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Der Nutzer hat weitergetippt: diese Suche ist überholt.
        }
    }

    async Task SearchAsync(CancellationToken ct)
    {
        var name = SearchText.Trim();
        var tag = Genre.Trim();
        var code = SelectedCountry?.Code ?? "";
        SaveView();
        // Nur Text oder Genre löst eine Suche aus. Ein gewähltes Land allein zeigt weiter die Favoriten.
        ShowResults = name.Length > 0 || tag.Length > 0;
        if (!ShowResults)
        {
            _results = [];
            Results.Clear();
            SearchState = LoadState.Idle;
            return;
        }

        using var scope = _perf.Begin("Radio.Suche");
        SearchState = LoadState.Loading;
        ErrorText = "";
        try
        {
            var stations = await _client.SearchAsync(name, code, tag, ct);
            if (ct.IsCancellationRequested) return;
            _results = stations.ToList();
            SearchState = _results.Count == 0 ? LoadState.Empty : LoadState.Ready;
            RebuildResults();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Abgebrochen, weil eine neuere Suche läuft.
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            scope.Fail();
            _results = [];
            Results.Clear();
            SearchState = LoadState.Error;
            ErrorText = Loc.T("radio.error.noDb");
        }
    }

    async Task LoadCountriesAsync()
    {
        try
        {
            _store.SetCountries(await _client.CountriesAsync(CancellationToken.None));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Ohne neue Liste bleibt die alte. Die Suche geht trotzdem.
            return;
        }
        Dispatcher.UIThread.Post(ReloadCountries);
    }

    void ReloadCountries()
    {
        _allCountries = [AllCountries, .. RadioCountries.Prepare(_store.Countries)];
        ApplyCountryFilter();
    }

    /// <summary>
    /// Zeigt nur die Länder, die zum Suchtext passen. „Alle Länder“ bleibt immer da. Die Auswahl bleibt, wenn das Land noch passt.
    /// Verschwindet es, springt die Auswahl auf „Alle Länder“ und die Suche läuft neu.
    /// </summary>
    void ApplyCountryFilter()
    {
        var before = SelectedCountry?.Code ?? "";
        _applyingFilter = true;
        try
        {
            Countries.Clear();
            foreach (var c in _allCountries.Where(c => RadioCountries.Matches(c, CountryFilter))) Countries.Add(c);
            SelectedCountry = Countries.FirstOrDefault(c => c.Code == before) ?? Countries[0];
        }
        finally
        {
            _applyingFilter = false;
        }
        if ((SelectedCountry?.Code ?? "") != before) _ = ScheduleSearchAsync();
    }

    void OnStatusChanged() => Dispatcher.UIThread.Post(RefreshLists);

    void RefreshLists()
    {
        Favorites.Clear();
        foreach (var s in _store.Favorites) Favorites.Add(CreateItem(ToStation(s), Favorites));
        HasFavorites = Favorites.Count > 0;

        Recent.Clear();
        foreach (var s in _store.Recent) Recent.Add(CreateItem(ToStation(s), Recent));
        HasRecent = Recent.Count > 0;

        RebuildResults();
    }

    void RebuildResults()
    {
        Results.Clear();
        foreach (var s in _results) Results.Add(CreateItem(s, Results));
    }

    /// <summary>Eine Zeile mit Status und Favorit aus dem Speicher. Spielen nimmt die ganze Liste als Warteschlange.</summary>
    StationItem CreateItem(RadioStation station, ObservableCollection<StationItem> list)
    {
        var item = CreateItemCore(station, list);
        item.SetPlaying(_curUrl.Length > 0 && station.StreamUrl == _curUrl, _curPlaying);
        return item;
    }

    string _curUrl = "";
    bool _curPlaying;

    void OnPlayerState(PlaybackState st)
    {
        var url = st.Item is { } it && it.SourceId == RadioStreamSource.SourceKey ? it.Id : "";
        var playing = url.Length > 0 && st.IsPlaying;
        if (url == _curUrl && playing == _curPlaying) return;
        _curUrl = url;
        _curPlaying = playing;
        foreach (var list in new[] { Results, Favorites, Recent })
            foreach (var i in list) i.SetPlaying(url.Length > 0 && i.Station.StreamUrl == url, playing);
    }

    StationItem CreateItemCore(RadioStation station, ObservableCollection<StationItem> list)
    {
        var saved = _store.Find(station.StreamUrl);
        return new StationItem(
            station,
            saved?.Status ?? StationStatus.Ok,
            saved?.Favorite == true,
            new AsyncRelayCommand(() => PlayAsync(station, list.Select(i => i.Station).ToList())),
            new AsyncRelayCommand(() => ToggleFavoriteAsync(station)),
            _thumbs);
    }

    async Task PlayAsync(RadioStation station, IReadOnlyList<RadioStation> queue)
    {
        // Die Warteschlange zuerst festhalten: Die Listen werden danach neu aufgebaut.
        var items = queue.Select(RadioStreamSource.ToItem).ToList();
        _store.MarkPlayed(station);
        RefreshLists();
        try
        {
            await _player.PlayAsync(RadioStreamSource.ToItem(station), items);
        }
        catch (InvalidOperationException)
        {
            NoticeText = Loc.T("radio.error.inactive");
        }
    }

    Task ToggleFavoriteAsync(RadioStation station)
    {
        var favorite = _store.Find(station.StreamUrl)?.Favorite == true;
        _store.SetFavorite(station, !favorite);
        RefreshLists();
        return Task.CompletedTask;
    }

    static RadioStation ToStation(SavedStation s) => new(s.Id, s.Name, s.StreamUrl, s.Country, s.Tags, s.Codec, s.Bitrate, s.LogoUrl);
}
