using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace YTSPlayer.Ui.Behaviors;

/// <summary>
/// Globale Regel für alle Fenster: Ein Klick außerhalb eines Eingabefelds nimmt diesem den Fokus (Tastatureingabe, Cursor).
/// Klicks in das Feld selbst oder in sein Dropdown (z. B. Vorschläge) zählen nicht.
/// </summary>
public static class GlobalDefocus
{
    static bool _installed;

    /// <summary>Hängt die Regel an alle Fenster der App. Mehrfaches Aufrufen ist harmlos.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    static void OnPressed(TopLevel top, PointerPressedEventArgs e)
    {
        if (top.FocusManager?.GetFocusedElement() is not Control focused || !IsInput(focused)) return;
        if (e.Source is ILogical source && source.GetSelfAndLogicalAncestors().Any(a => ReferenceEquals(a, focused))) return;
        if (e.Source is Visual visual && visual.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, focused))) return;
        top.FocusManager.Focus(null);
    }

    static bool IsInput(Control c) => c is TextBox or AutoCompleteBox or NumericUpDown || c.FindAncestorOfType<AutoCompleteBox>() != null || c.FindAncestorOfType<TextBox>() != null;
}
