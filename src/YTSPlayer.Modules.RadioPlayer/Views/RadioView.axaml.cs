using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using YTSPlayer.Modules.RadioPlayer.ViewModels;
using YTSPlayer.Ui.Controls;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.RadioPlayer.Views;

/// <summary>Radio-Seite. Der Dialog für die Stream-Adresse und der Doppelklick auf einen Sender liegen hier, weil es reine Oberfläche ist.</summary>
public partial class RadioView : UserControl
{
    public RadioView() => InitializeComponent();

    /// <summary>Ein Klick auf ein Land wählt es und schließt das Dropdown.</summary>
    void OnCountryTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() != null) CountryButton.Flyout?.Hide();
    }

    async void OnAddUrl(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RadioViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var address = await TextPromptWindow.AskAsync(owner, Loc.T("radio.add.title"), Loc.T("radio.add.prompt"), Loc.T("radio.add.ok"));
        if (address != null) await vm.AddByUrlAsync(address);
    }

    /// <summary>Doppelklick auf eine Sender-Zeile startet den Sender. Doppelklicks auf die Knöpfe der Zeile zählen nicht.</summary>
    void OnStationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source) return;
        if (source.FindAncestorOfType<Button>(includeSelf: true) != null) return;
        if (source.FindAncestorOfType<ListBoxItem>()?.DataContext is not StationItem item) return;
        if (item.PlayCommand.CanExecute(null)) item.PlayCommand.Execute(null);
    }
}
