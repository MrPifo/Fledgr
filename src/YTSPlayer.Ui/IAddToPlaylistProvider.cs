using Avalonia.Controls;

namespace YTSPlayer.Ui;

/// <summary>
/// Optional für Quellen, die den laufenden Titel zu einer Playlist hinzufügen können (Flag <c>PlaybackCapabilities.AddToPlaylist</c>).
/// Die Player-Leiste zeigt dafür einen eigenen Knopf neben „Weiter“.
/// </summary>
public interface IAddToPlaylistProvider
{
    /// <summary>Zeigt die Playlist-Auswahl für den laufenden Titel unter <paramref name="anchor"/>.</summary>
    void ShowAddToPlaylist(Control anchor);
}
