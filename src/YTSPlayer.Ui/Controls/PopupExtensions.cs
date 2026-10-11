using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace YTSPlayer.Ui.Controls;

/// <summary>Einheitliche Platzierung von Popups und Menüs.</summary>
public static class PopupExtensions
{
    /// <summary>
    /// Zeigt das Flyout bevorzugt über dem angeklickten Element. Ist oben zu wenig Platz, dreht Avalonia es automatisch nach unten.
    /// Am Mauszeiger (Rechtsklick) bleibt die Standard-Platzierung.
    /// </summary>
    public static void ShowAbove(this PopupFlyoutBase flyout, Control anchor, bool atPointer = false)
    {
        if (!atPointer) flyout.Placement = PlacementMode.Top;
        flyout.ShowAt(anchor, atPointer);
    }
}
