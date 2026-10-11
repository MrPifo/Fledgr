using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using YTSPlayer.Contracts.Media;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>
/// Eine Zeile in der Titelliste. Das Vorschaubild wird erst geladen, wenn die Zeile zum ersten Mal angezeigt wird
/// (die Liste ist virtualisiert, so liegen nur sichtbare Bilder im Speicher).
/// </summary>
public sealed class TrackItem(TrackInfo info, IThumbnailCache thumbs, int thumbWidth = TrackItem.ListWidth) : YTSPlayer.Ui.PlayingRow
{
    /// <summary>Breite des Vorschaubilds in Listenzeilen (klein, spart Speicher).</summary>
    public const int ListWidth = 96;

    /// <summary>Breite für Karten (120 px groß, auch bei höherer Bildschirmskalierung scharf; daher mehr Pixel, sonst wirkt das Bild unscharf).</summary>
    public const int CardWidth = 192;

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

    /// <summary>Fragt bei Google-Bildern (Größe im Adress-Ende) die Kartengröße an. Andere Adressen bleiben.</summary>
    static string Larger(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(url, @"=w\d+-h\d+[^?]*$", $"=w{CardWidth}-h{CardWidth}-l90-rj")
            : url;

    async Task LoadAsync()
    {
        try
        {
            var bytes = await thumbs.GetAsync(thumbWidth > ListWidth ? Larger(Info.Thumb) : Info.Thumb);
            if (bytes == null) return;
            var bmp = await Task.Run(() =>
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    return Bitmap.DecodeToWidth(ms, thumbWidth);
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
