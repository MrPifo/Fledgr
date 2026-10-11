using Avalonia.Controls;

namespace YTSPlayer.Ui;

/// <summary>
/// Optional für Quellen mit einer Favoriten-Liste (Radio). Das Widget zeigt dann links ein Favoriten-Dropdown.
/// </summary>
public interface IFavoritesProvider
{
    /// <summary>Wahr, wenn es mindestens einen Favoriten gibt.</summary>
    bool HasFavorites { get; }

    /// <summary>Zeigt die Favoriten als Menü über <paramref name="anchor"/>. Ein Klick spielt den Sender.</summary>
    void ShowFavorites(Control anchor);
}
