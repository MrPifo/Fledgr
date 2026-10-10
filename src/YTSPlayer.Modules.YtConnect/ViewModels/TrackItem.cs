using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using YTSPlayer.Contracts.Media;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>
/// Eine Zeile in der Titelliste. Das Vorschaubild wird erst geladen, wenn die Zeile zum ersten Mal angezeigt wird
/// (die Liste ist virtualisiert, so liegen nur sichtbare Bilder im Speicher).
/// </summary>
public sealed class TrackItem(TrackInfo info, IThumbnailCache thumbs) : ObservableObject
{
    const int ThumbWidth = 96;

    Bitmap? _cover;
    bool _requested;

    public TrackInfo Info { get; } = info;
    public string Title => Info.Title;
    public string Artist => Info.Artist;
    public string Duration => Info.Duration;

    /// <summary>Vorschaubild. Die erste Abfrage startet das Laden im Hintergrund, danach meldet die Eigenschaft die Änderung.</summary>
    public Bitmap? Cover
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _cover;
        }
    }

    async Task LoadAsync()
    {
        try
        {
            var bytes = await thumbs.GetAsync(Info.Thumb);
            if (bytes == null) return;
            var bmp = await Task.Run(() =>
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    return Bitmap.DecodeToWidth(ms, ThumbWidth);
                }
                catch (Exception)
                {
                    return null;
                }
            });
            if (bmp == null) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _cover = bmp;
                OnPropertyChanged(nameof(Cover));
            });
        }
        catch (Exception)
        {
            // Ohne Bild bleibt die Fläche einfach leer.
        }
    }
}
