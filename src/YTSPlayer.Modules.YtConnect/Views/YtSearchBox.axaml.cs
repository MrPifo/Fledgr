using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YTSPlayer.Modules.YtConnect.ViewModels;

namespace YTSPlayer.Modules.YtConnect.Views;

/// <summary>Suchfeld in der Kopfzeile der App. Das ViewModel ist ein <see cref="YtSearchHeader"/>, das auf das <see cref="YtViewModel"/> zeigt.</summary>
public partial class YtSearchBox : UserControl
{
    public YtSearchBox() => InitializeComponent();

    YtViewModel? Vm => (DataContext as YtSearchHeader)?.Vm;

    /// <summary>Enter im Suchfeld sucht (außer ein Vorschlag ist gerade ausgewählt, den übernimmt das Feld selbst).</summary>
    void OnSearchKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Vm is not { } vm || SearchBox.IsDropDownOpen) return;
        vm.SearchCommand.Execute(null);
    }

    bool _openEmpty;

    /// <summary>Beim Anklicken oder Antabulatorn des leeren Felds zeigt die Liste den Suchverlauf (nicht, wenn der Fokus per Code kommt, z. B. nach dem Leeren mit dem X).</summary>
    void OnSearchGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod == NavigationMethod.Unspecified || Vm is not { Suggestions.Count: > 0 }) return;
        _openEmpty = true;
        try { SearchBox.IsDropDownOpen = true; }
        finally { _openEmpty = false; }
    }

    /// <summary>Bei leerem Feld öffnet sich die Liste nur durch einen Klick ins Feld, nicht von selbst (z. B. nach dem Leeren).</summary>
    void OnSearchDropDownOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text) && !_openEmpty) e.Cancel = true;
    }

    /// <summary>Ein Klick auf einen Vorschlag setzt den Text und startet gleich die Suche.</summary>
    void OnSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is not { } vm || e.AddedItems.Count == 0 || e.AddedItems[0] is not string chosen || chosen.Length == 0) return;
        SearchBox.SelectedItem = null;
        SearchBox.IsDropDownOpen = false;
        vm.SearchText = chosen;
        vm.SearchCommand.Execute(null);
    }
}
