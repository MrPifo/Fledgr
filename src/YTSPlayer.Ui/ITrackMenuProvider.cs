using Avalonia.Controls;

namespace YTSPlayer.Ui;

/// <summary>
/// Optional für Quellen, die zum laufenden Titel ein Menü anbieten (z. B. „Zu Playlist hinzufügen“).
/// Die Player-Leiste zeigt dafür einen Drei-Punkte-Knopf (Flag <c>PlaybackCapabilities.TrackMenu</c>) und ruft das hier auf.
/// </summary>
public interface ITrackMenuProvider
{
    /// <summary>Zeigt das Menü des laufenden Titels unter <paramref name="anchor"/>.</summary>
    void ShowTrackMenu(Control anchor);
}
