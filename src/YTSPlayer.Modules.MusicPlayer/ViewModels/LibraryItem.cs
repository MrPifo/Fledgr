using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Modules.MusicPlayer.Library;
using YTSPlayer.Ui;

namespace YTSPlayer.Modules.MusicPlayer.ViewModels;

/// <summary>Eine Zeile der Bibliotheks-Liste (Ordner, Playlist, Interpret, Album oder Titel).</summary>
public sealed partial class LibraryItem : ObservableObject
{
    readonly LocalCovers? _covers;
    Bitmap? _cover;
    bool _coverRequested;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FavoriteIcon))] bool _isFavorite;

    public LibraryItem(MediaNode node, bool isFavorite, Action<LibraryItem> toggleFavorite, LocalCovers? covers = null)
    {
        Node = node;
        _isFavorite = isFavorite;
        _covers = covers;
        ToggleFavoriteCommand = new RelayCommand(() => toggleFavorite(this));
    }

    public MediaNode Node { get; }
    public string Title => Node.Title;
    public string Subtitle => Node.Subtitle;
    public bool IsTrack => Node.Kind == MediaNodeKind.Track;
    public string FavoriteIcon => IsFavorite ? Icons.Star : Icons.StarOutline;
    public IRelayCommand ToggleFavoriteCommand { get; }

    public string Icon => Node.Kind switch
    {
        MediaNodeKind.Folder => Icons.Folder,
        MediaNodeKind.Playlist => Icons.Playlist,
        MediaNodeKind.Station => Icons.Station,
        _ => Icons.Track,
    };

    /// <summary>Die Datei, aus der das Cover kommt: der Titel selbst, bei einem Album sein erster Titel.</summary>
    string? CoverFile => IsTrack ? Node.Id : Node.Item?.Id;

    public bool HasCover => _cover != null;

    /// <summary>
    /// Das Cover, wenn vorhanden. Die erste Abfrage startet das Laden im Hintergrund (nur sichtbare Zeilen fragen), danach meldet die Eigenschaft die Änderung.
    /// </summary>
    public Bitmap? Cover
    {
        get
        {
            if (!_coverRequested && _covers != null && CoverFile is { } file)
            {
                _coverRequested = true;
                _ = LoadCoverAsync(file);
            }
            return _cover;
        }
    }

    async Task LoadCoverAsync(string file)
    {
        try
        {
            var bmp = await _covers!.GetAsync(file);
            if (bmp == null) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _cover = bmp;
                OnPropertyChanged(nameof(Cover));
                OnPropertyChanged(nameof(HasCover));
            });
        }
        catch (Exception)
        {
            // Ohne Cover bleibt das Symbol stehen.
        }
    }
}
