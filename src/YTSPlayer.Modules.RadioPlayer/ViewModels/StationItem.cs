using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Modules.RadioPlayer.Data;
using YTSPlayer.Ui;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.RadioPlayer.ViewModels;

/// <summary>
/// Eine Zeile in einer Sender-Liste. Das Logo wird erst geladen, wenn die Zeile zum ersten Mal angezeigt wird.
/// </summary>
public sealed class StationItem(RadioStation station, StationStatus status, bool favorite, IAsyncRelayCommand play, IAsyncRelayCommand toggleFavorite, IThumbnailCache thumbs) : PlayingRow
{
    const int LogoWidth = 48;

    Bitmap? _logo;
    bool _requested;

    public RadioStation Station { get; } = station;
    public string Name => Station.Name;
    public string Subtitle => string.Join(" · ", new[] { Station.Country, Station.TechnicalInfo, Station.Tags }.Where(x => x.Length > 0));
    public bool IsFavorite { get; } = favorite;
    public string FavoriteIcon => IsFavorite ? Icons.Star : Icons.StarOutline;
    public bool HasStatus => status != StationStatus.Ok;

    public string StatusText => status switch
    {
        StationStatus.Dead => Loc.T("radio.status.dead"),
        StationStatus.Gone => Loc.T("radio.status.gone"),
        _ => "",
    };

    public IAsyncRelayCommand PlayCommand { get; } = play;
    public IAsyncRelayCommand ToggleFavoriteCommand { get; } = toggleFavorite;

    public bool HasLogo => _logo != null;

    /// <summary>Logo des Senders. Die erste Abfrage startet das Laden im Hintergrund, danach meldet die Eigenschaft die Änderung.</summary>
    public Bitmap? Logo
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadLogoAsync();
            }
            return _logo;
        }
    }

    /// <summary>Erkennt PNG, JPEG, GIF, BMP und WebP an den ersten Bytes.</summary>
    static bool IsKnownImage(byte[] b) =>
        b.Length > 12 && (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            || b[0] == 0xFF && b[1] == 0xD8
            || b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46
            || b[0] == 0x42 && b[1] == 0x4D
            || b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50);

    async Task LoadLogoAsync()
    {
        if (Station.LogoUrl == null) return;
        try
        {
            var bytes = await thumbs.GetAsync(Station.LogoUrl);
            if (bytes == null) return;
            var bmp = await Task.Run(() =>
            {
                // Nur Formate, die Avalonia sicher liest (sonst wirft der Decoder bei jedem Logo, das keins ist).
                if (!IsKnownImage(bytes)) return null;
                try
                {
                    using var ms = new MemoryStream(bytes);
                    return Bitmap.DecodeToWidth(ms, LogoWidth);
                }
                catch (Exception)
                {
                    // Nicht jedes Logo ist ein Bild, das Avalonia lesen kann (z. B. .ico oder .svg). Dann bleibt das Symbol.
                    return null;
                }
            });
            if (bmp == null) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _logo = bmp;
                OnPropertyChanged(nameof(Logo));
                OnPropertyChanged(nameof(HasLogo));
            });
        }
        catch (Exception)
        {
            // Ohne Logo bleibt das Symbol.
        }
    }
}
