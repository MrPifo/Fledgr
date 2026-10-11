using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Modules.MusicPlayer.Library;
using YTSPlayer.Modules.MusicPlayer.Playback;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.MusicPlayer.ViewModels;

/// <summary>Bibliotheks-Seite: Ordner durchsuchen, Playlists, Favoriten, Suche. Alles asynchron mit Lade-Zustand.</summary>
public sealed partial class LibraryViewModel : ObservableObject, YTSPlayer.Contracts.Plugins.IHeaderContent
{
    const int MaxQueue = 2000;

    MusicSearchHeader? _header;

    /// <summary>Die Suche in der Kopfzeile der App.</summary>
    public object? HeaderViewModel => _header ??= new MusicSearchHeader(this);
    static string[] Modes => [Loc.T("music.mode.folders"), Loc.T("music.mode.artists"), Loc.T("music.mode.albums"), Loc.T("music.mode.playlists"), Loc.T("music.mode.favorites")];

    readonly LibraryService _lib;
    readonly LibraryProvider _provider;
    readonly IPlayer _player;
    readonly Stack<MediaNode?> _back = [];
    MediaNode? _current;
    CancellationTokenSource? _loadCts;
    CancellationTokenSource? _searchCts;
    CancellationTokenSource? _scanCts;

    [ObservableProperty] LibraryItem? _selectedItem;
    [ObservableProperty] LoadState _state = LoadState.Loading;
    [ObservableProperty] string _message = "";
    [ObservableProperty] string _errorText = "";
    [ObservableProperty] string _searchText = "";
    [ObservableProperty] int _modeIndex;
    [ObservableProperty] string _pathText = "";
    [ObservableProperty] bool _canGoUp;
    [ObservableProperty] bool _isScanning;
    [ObservableProperty] string _scanText = "";
    [ObservableProperty] bool _autoPreview;
    [ObservableProperty] string _newPlaylistName = "";
    [ObservableProperty] PlaylistDef? _targetPlaylist;
    [ObservableProperty] bool _inPlaylist;

    readonly LocalCovers _covers = new();

    readonly YTSPlayer.Contracts.Settings.ISettingsStore _store;
    bool _restoring;

    public LibraryViewModel(LibraryService lib, LibraryProvider provider, IPlayer player, YTSPlayer.Contracts.Settings.ISettingsStore store)
    {
        _lib = lib;
        _provider = provider;
        _player = player;
        _store = store;
        RestoreView();
        player.StateChanged += OnPlayerState;
        OnPlayerState(player.State);
        _autoPreview = lib.Settings.AutoPreview;
        Playlists = [.. lib.Settings.Playlists];
        TargetPlaylist = Playlists.FirstOrDefault();
        // Dateien im Ordner geändert (neu, gelöscht, umbenannt): die Liste zeigt es ohne Neu-Scan.
        lib.FilesChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (IsScanning) return;
            if (!string.IsNullOrWhiteSpace(SearchText)) _ = SearchDebouncedAsync(SearchText);
            else _ = LoadAsync();
        });
    }

    public ObservableCollection<LibraryItem> Items { get; } = [];
    public ObservableCollection<PlaylistDef> Playlists { get; }
    public string[] ModeNames => Modes;

    /// <summary>Lädt die Startansicht und startet im Hintergrund den Scan.</summary>
    public async Task InitializeAsync()
    {
        await LoadAsync();
        if (_lib.Settings.Folders.Count > 0) _ = RescanAsync();
    }

    /// <summary>Stellt Ansicht und geöffneten Ordner der letzten Sitzung wieder her (geladen wird in <see cref="InitializeAsync"/>).</summary>
    void RestoreView()
    {
        try
        {
            var s = _store.Load<MusicViewState>("music-view");
            _restoring = true;
            ModeIndex = Math.Clamp(s.ModeIndex, 0, Modes.Length - 1);
            if (s.CurrentId.Length > 0) _current = new MediaNode(s.CurrentId, s.CurrentTitle, (MediaNodeKind)s.CurrentKind);
            // Ein Ebene tiefer in Künstlern, Alben oder Playlists: „Zurück“ führt zur Wurzel der Ansicht.
            if (ModeIndex > 0 && _current != null && ModeRoot(ModeIndex) is { } root && root.Id != _current.Id) _back.Push(root);
        }
        catch (Exception)
        {
            // Ein kaputter Speicher: Start in der Ordneransicht.
        }
        finally
        {
            _restoring = false;
        }
    }

    void SaveView()
    {
        if (_restoring) return;
        try
        {
            _store.Save("music-view", new MusicViewState
            {
                ModeIndex = ModeIndex,
                CurrentId = _current?.Id ?? "",
                CurrentTitle = _current?.Title ?? "",
                CurrentKind = (int)(_current?.Kind ?? MediaNodeKind.Folder),
            });
        }
        catch (Exception)
        {
            // Speichern darf die Oberfläche nie stören.
        }
    }

    partial void OnModeIndexChanged(int value)
    {
        if (_restoring) return;
        _back.Clear();
        _current = ModeRoot(value);
        SearchText = "";
        _ = LoadAsync();
    }

    static MediaNode? ModeRoot(int mode) => mode switch
    {
        1 => LibraryProvider.ArtistsNode,
        2 => LibraryProvider.AlbumsNode,
        3 => LibraryProvider.PlaylistsNode,
        4 => LibraryProvider.FavoritesNode,
        _ => null,
    };

    partial void OnAutoPreviewChanged(bool value)
    {
        _lib.Settings.AutoPreview = value;
        _lib.Save();
    }

    partial void OnSelectedItemChanged(LibraryItem? value)
    {
        if (!_syncingSelection && AutoPreview && value is { IsTrack: true } && value.Node.Item is { } item)
            _ = Run(() => _player.PlayAsync(item));
    }

    partial void OnSearchTextChanged(string value) => _ = SearchDebouncedAsync(value);

    async Task SearchDebouncedAsync(string text)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            if (string.IsNullOrWhiteSpace(text)) { await LoadAsync(); return; }
            _loadCts?.Cancel();
            State = LoadState.Loading;
            Message = Loc.T("music.search.busy");
            var nodes = await _provider.SearchAsync(text, cts.Token);
            if (cts.IsCancellationRequested) return;
            Fill(nodes);
            PathText = Loc.F("music.search.count", nodes.Count + (nodes.Count >= 500 ? "+" : ""));
            CanGoUp = false;
            State = nodes.Count == 0 ? LoadState.Empty : LoadState.Ready;
            Message = Loc.T("music.search.none");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            State = LoadState.Error;
        }
    }

    [RelayCommand]
    async Task Reload() => await LoadAsync();

    async Task LoadAsync()
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        State = LoadState.Loading;
        Message = Loc.T("music.loading");
        try
        {
            var nodes = await _provider.GetChildrenAsync(_current, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (ModeIndex == 0 && _current == null) nodes = nodes.Where(n => !n.Id.StartsWith('@')).ToList();
            Fill(nodes);
            PathText = _current?.Title ?? Loc.T("music.mode.folders");
            CanGoUp = _back.Count > 0 || _current != null && ModeIndex == 0;
            InPlaylist = _current?.Kind == MediaNodeKind.Playlist;
            if (Items.Count == 0)
            {
                Message = _current?.Id switch
                {
                    "@playlists" => Loc.T("music.empty.playlists"),
                    "@favorites" => Loc.T("music.empty.favorites"),
                    "@artists" or "@albums" => _lib.Settings.SearchTags
                        ? Loc.T("music.empty.noTags")
                        : Loc.T("music.empty.needTags"),
                    null => ModeIndex == 0 ? Loc.T("music.empty.noFolder") : Loc.T("music.empty.nothing"),
                    _ => Loc.T("music.empty.nothing"),
                };
            }
            State = Items.Count == 0 ? LoadState.Empty : LoadState.Ready;
            SaveView();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            State = LoadState.Error;
        }
    }

    void Fill(IReadOnlyList<MediaNode> nodes)
    {
        Items.Clear();
        foreach (var n in nodes) Items.Add(new LibraryItem(n, n.Kind == MediaNodeKind.Track && _lib.IsFavorite(n.Id), ToggleFavorite, _covers));
        MarkCurrent(select: true);
    }

    string _curId = "";
    bool _curPlaying;
    bool _syncingSelection;

    void OnPlayerState(PlaybackState st)
    {
        var id = st.Item is { } it && it.SourceId == LibVlcSource.SourceKey ? it.Id : "";
        var playing = id.Length > 0 && st.IsPlaying;
        if (id == _curId && playing == _curPlaying) return;
        var idChanged = id != _curId;
        _curId = id;
        _curPlaying = playing;
        MarkCurrent(select: idChanged);
    }

    /// <summary>Markiert den laufenden Titel in der Liste (Balken) und wählt ihn aus. Die Auswahl löst keine Vorschau aus.</summary>
    void MarkCurrent(bool select)
    {
        if (_curId.Length == 0 && !Items.Any(i => i.IsCurrent)) return;
        LibraryItem? row = null;
        foreach (var i in Items)
        {
            var current = _curId.Length > 0 && i.IsTrack && i.Node.Id == _curId;
            i.SetPlaying(current, _curPlaying);
            if (current) row = i;
        }
        if (select && row != null && !ReferenceEquals(SelectedItem, row))
        {
            _syncingSelection = true;
            try { SelectedItem = row; }
            finally { _syncingSelection = false; }
        }
    }

    void ToggleFavorite(LibraryItem item)
    {
        _lib.ToggleFavorite(item.Node.Id);
        item.IsFavorite = _lib.IsFavorite(item.Node.Id);
        if (_current?.Id == LibraryProvider.FavoritesNode.Id) _ = LoadAsync();
    }

    /// <summary>Doppelklick oder Enter: Ordner und Playlists öffnen, Titel abspielen.</summary>
    [RelayCommand]
    async Task OpenSelected()
    {
        if (SelectedItem is not { } item) return;
        if (item.Node.HasChildren)
        {
            _back.Push(_current);
            _current = item.Node;
            SearchText = "";
            await LoadAsync();
            return;
        }
        if (item.Node.Item is not { } media) return;
        var queue = Items.Where(i => i.IsTrack && i.Node.Item != null).Take(MaxQueue).Select(i => i.Node.Item!).ToList();
        await Run(() => _player.PlayAsync(media, queue));
    }

    [RelayCommand]
    async Task GoUp()
    {
        if (_back.Count > 0) _current = _back.Pop();
        else if (_current != null && ModeIndex == 0)
        {
            var parent = Path.GetDirectoryName(_current.Id);
            var isRoot = _lib.Settings.Folders.Contains(_current.Id, StringComparer.OrdinalIgnoreCase);
            _current = isRoot || parent == null ? null : new MediaNode(parent, Path.GetFileName(parent), MediaNodeKind.Folder);
        }
        await LoadAsync();
    }

    public async Task AddFolderAsync(string path)
    {
        _lib.AddFolder(path);
        ModeIndex = 0;
        _back.Clear();
        _current = null;
        await LoadAsync();
        _ = RescanAsync();
    }

    [RelayCommand]
    async Task RemoveSelectedFolder()
    {
        if (SelectedItem is { Node: { Kind: MediaNodeKind.Folder } n } && _current == null && ModeIndex == 0 && !n.Id.StartsWith('@'))
        {
            _lib.RemoveFolder(n.Id);
            await LoadAsync();
        }
    }

    [RelayCommand]
    async Task Rescan() => await RescanAsync();

    /// <summary>Liest den Index neu (z. B. nach dem Ändern der Einstellung „Tags in der Suche“).</summary>
    public Task RescanNowAsync() => RescanAsync();

    async Task RescanAsync()
    {
        if (_lib.IsScanning) return;
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanText = Loc.T("music.scan.start");
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                ScanText = p.Tags ? Loc.F("music.scan.tags", Loc.Current.Integer(p.Files), Loc.Current.Integer(p.Total)) : p.Files == 0 ? Loc.T("music.scan.running") : Loc.P("music.scan.found", p.Files));
            await _lib.ScanAsync(progress, cts.Token);
            ScanText = Loc.P("music.scan.done", _lib.FileCount);
        }
        catch (Exception ex)
        {
            ScanText = Loc.F("music.scan.error", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    void CreatePlaylist()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;
        var p = _lib.CreatePlaylist(NewPlaylistName);
        Playlists.Add(p);
        TargetPlaylist = p;
        NewPlaylistName = "";
        if (ModeIndex == 3) _ = LoadAsync();
    }

    /// <summary>Nur die Titel der Auswahl, in der Reihenfolge der Liste (nicht in der Reihenfolge des Anklickens).</summary>
    IReadOnlyList<LibraryItem> Tracks(IEnumerable<LibraryItem> selection) =>
        [.. selection.Where(i => i.IsTrack).OrderBy(i => Items.IndexOf(i))];

    /// <summary>Fügt alle markierten Titel der Playlist <paramref name="target"/> (sonst der gewählten Playlist) hinzu.</summary>
    public void AddToPlaylist(IEnumerable<LibraryItem> selection, PlaylistDef? target = null)
    {
        if ((target ?? TargetPlaylist) is not { } p) return;
        var tracks = Tracks(selection);
        foreach (var item in tracks) _lib.AddToPlaylist(p, item.Node.Id);
        ScanText = tracks.Count == 0 ? Loc.T("music.noSelection") : Loc.P("music.added", tracks.Count, p.Name);
        if (ModeIndex == 3) _ = LoadAsync();
    }

    /// <summary>Legt eine neue Playlist an und fügt die Titel gleich hinzu (Menü „Neue Playlist …“).</summary>
    public void CreatePlaylistWith(string name, IEnumerable<LibraryItem> selection)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var p = _lib.CreatePlaylist(name.Trim());
        Playlists.Add(p);
        TargetPlaylist = p;
        AddToPlaylist(selection, p);
    }

    /// <summary>Entfernt alle markierten Titel aus der geöffneten Playlist.</summary>
    public async Task RemoveFromPlaylistAsync(IEnumerable<LibraryItem> selection)
    {
        if (!InPlaylist || _lib.Settings.Playlists.FirstOrDefault(p => "pl:" + p.Id == _current!.Id) is not { } pl) return;
        foreach (var item in Tracks(selection)) _lib.RemoveFromPlaylist(pl, item.Node.Id);
        await LoadAsync();
    }

    /// <summary>Spielt die markierten Titel als Warteschlange (in der Reihenfolge der Liste).</summary>
    public async Task PlaySelectionAsync(IEnumerable<LibraryItem> selection)
    {
        var queue = Tracks(selection).Where(i => i.Node.Item != null).Select(i => i.Node.Item!).ToList();
        if (queue.Count == 0) { ScanText = Loc.T("music.noSelection"); return; }
        await Run(() => _player.PlayAsync(queue[0], queue));
    }

    /// <summary>
    /// Reaktion auf Dateien oder Ordner, die ins Fenster gezogen wurden: Ordner kommen in die Bibliothek (mit Scan),
    /// Audio-Dateien spielen sofort als Warteschlange.
    /// </summary>
    public async Task HandleDropAsync(IReadOnlyList<string> paths)
    {
        var folders = paths.Where(Directory.Exists).ToList();
        var files = paths.Where(p => File.Exists(p) && LibraryService.IsAudio(p)).ToList();
        if (folders.Count == 0 && files.Count == 0) { ScanText = Loc.T("music.drop.nothing"); return; }
        if (folders.Count > 0)
        {
            foreach (var f in folders) _lib.AddFolder(f);
            ModeIndex = 0;
            _back.Clear();
            _current = null;
            await LoadAsync();
            _ = RescanAsync();
        }
        if (files.Count > 0)
        {
            var queue = files.Select(LibraryService.ToItem).ToList();
            await Run(() => _player.PlayAsync(queue[0], queue));
        }
    }

    [RelayCommand]
    async Task DeletePlaylist()
    {
        if (!InPlaylist || _lib.Settings.Playlists.FirstOrDefault(p => "pl:" + p.Id == _current!.Id) is not { } pl) return;
        _lib.DeletePlaylist(pl);
        Playlists.Remove(pl);
        TargetPlaylist = Playlists.FirstOrDefault();
        _current = _back.Count > 0 ? _back.Pop() : LibraryProvider.PlaylistsNode;
        await LoadAsync();
    }

    async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            State = LoadState.Error;
        }
    }
}
