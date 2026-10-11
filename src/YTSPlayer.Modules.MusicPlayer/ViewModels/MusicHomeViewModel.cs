using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Localization;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Modules.MusicPlayer.Library;
using YTSPlayer.Ui;

namespace YTSPlayer.Modules.MusicPlayer.ViewModels;

/// <summary>Eine Kachel im Musik-Abschnitt von Home: eine Playlist oder ein zuletzt gespielter Titel. Ein Klick spielt sie.</summary>
public sealed class MusicTile(string name, string subtitle, string icon, IRelayCommand play)
{
    public string Name { get; } = name;
    public string Subtitle { get; } = subtitle;
    public string Icon { get; } = icon;
    public IRelayCommand PlayCommand { get; } = play;
}

/// <summary>
/// Der Abschnitt „Lokale Musik“ auf der Home-Seite: die eigenen Playlists und die zuletzt gespielten lokalen Titel als Kacheln.
/// Der Abschnitt zeigt sich, sobald die Bibliothek Dateien hat (dann gibt es mindestens den Knopf „Zufällig“).
/// </summary>
public sealed class MusicHomeViewModel : ObservableObject, IHomeContent
{
    const int MaxItems = 12;
    const int MaxQueue = 1000;

    readonly LibraryService _lib;
    readonly IPlayer _player;
    readonly LibraryViewModel _vm;
    readonly IPlayHistory? _history;

    public MusicHomeViewModel(LibraryService lib, LibraryViewModel vm, IPlayer player, IPlayHistory? history)
    {
        _lib = lib;
        _vm = vm;
        _player = player;
        _history = history;
        vm.Playlists.CollectionChanged += OnPlaylistsChanged;
        lib.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
        if (history != null) history.Changed += Rebuild;
        Rebuild();
    }

    public ObservableCollection<MusicTile> Tiles { get; } = [];

    /// <summary>Wahr, wenn es Kacheln gibt oder die Bibliothek Dateien hat.</summary>
    public bool HasContent => Tiles.Count > 0 || _lib.FileCount > 0;

    /// <summary>Wahr, wenn keine Kachel da ist (der Abschnitt zeigt dann einen Hinweis).</summary>
    public bool IsEmpty => Tiles.Count == 0;

    void OnPlaylistsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    void Rebuild()
    {
        Tiles.Clear();
        foreach (var p in _vm.Playlists)
            Tiles.Add(new MusicTile(p.Name, Loc.P("music.tracks", p.Paths.Count), Icons.Playlist, new RelayCommand(() => _ = PlayAsync(p.Paths))));
        foreach (var e in (_history?.Entries ?? []).Where(e => e.SourceId == "local"))
        {
            var entry = e;
            Tiles.Add(new MusicTile(entry.Title, entry.Artist, Icons.Track, new RelayCommand(() => _ = PlayAsync([entry.ItemId]))));
        }
        while (Tiles.Count > MaxItems) Tiles.RemoveAt(Tiles.Count - 1);
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Knopf „Zufällig“: spielt die ganze Bibliothek in zufälliger Reihenfolge (höchstens 1000 Titel in der Warteschlange).</summary>
    public void StartShuffle()
    {
        var paths = _lib.Entries.Select(e => e.Path).OrderBy(_ => Random.Shared.Next()).Take(MaxQueue).ToList();
        _ = PlayAsync(paths);
    }

    async Task PlayAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        try
        {
            var queue = paths.Select(LibraryService.ToItem).ToList();
            await _player.PlayAsync(queue[0], queue);
        }
        catch (Exception ex)
        {
            AppLog.Error("Musik", "Home: Wiedergabe starten fehlgeschlagen", ex);
        }
    }
}
