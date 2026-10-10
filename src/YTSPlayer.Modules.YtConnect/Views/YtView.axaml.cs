using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using YTSPlayer.Modules.YtConnect.ViewModels;

namespace YTSPlayer.Modules.YtConnect.Views;

public partial class YtView : UserControl
{
    public YtView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is YtViewModel vm) vm.AttachScroll(ApplyScroll);
        };
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
    }

    /// <summary>Enter im Suchfeld sucht (außer ein Vorschlag ist gerade ausgewählt, den übernimmt das Feld selbst).</summary>
    void OnSearchKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not YtViewModel vm || SearchBox.IsDropDownOpen) return;
        vm.SearchCommand.Execute(null);
    }

    /// <summary>Beim Anklicken des leeren Felds zeigt die Liste den Suchverlauf.</summary>
    void OnSearchGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is YtViewModel { Suggestions.Count: > 0 }) SearchBox.IsDropDownOpen = true;
    }

    /// <summary>Ein Klick auf einen Vorschlag setzt den Text und schließt die Liste: dann wird gesucht.</summary>
    void OnSearchDropDownClosed(object? sender, EventArgs e)
    {
        if (DataContext is not YtViewModel vm || SearchBox.SelectedItem is not string chosen) return;
        SearchBox.SelectedItem = null;
        vm.SearchText = chosen;
        vm.SearchCommand.Execute(null);
    }

    /// <summary>Der Doppelklick auf einen Menü-Knopf soll nicht die Playlist öffnen oder den Titel abspielen.</summary>
    void OnMenuButtonDoubleTapped(object? sender, TappedEventArgs e) => e.Handled = true;

    /// <summary>Das Drei-Punkte-Menü einer Playlist.</summary>
    void OnPlaylistMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is PlaylistInfo p && DataContext is YtViewModel vm) YtMenus.ShowPlaylist(vm, p, b);
    }

    /// <summary>Das Drei-Punkte-Menü eines Titels.</summary>
    void OnTrackMenu(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.DataContext is TrackItem t && DataContext is YtViewModel vm) YtMenus.ShowTrack(vm, t, b);
    }

    /// <summary>Rechtsklick auf eine Playlist: dasselbe Menü an der Mausposition.</summary>
    void OnPlaylistContext(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not PlaylistInfo p || DataContext is not YtViewModel vm) return;
        e.Handled = true;
        YtMenus.ShowPlaylist(vm, p, c, atPointer: true);
    }

    /// <summary>Rechtsklick auf einen Titel: dasselbe Menü an der Mausposition.</summary>
    void OnTrackContext(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not TrackItem t || DataContext is not YtViewModel vm) return;
        e.Handled = true;
        YtMenus.ShowTrack(vm, t, c, atPointer: true);
    }

    /// <summary>Knopf „Neue Playlist“: fragt nach dem Namen und legt die Playlist an.</summary>
    async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && DataContext is YtViewModel vm) await YtMenus.NewPlaylistAsync(vm, c, null);
    }

    /// <summary>Doppelklick auf eine Playlist öffnet sie (Titel anzeigen).</summary>
    void OnPlaylistDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is YtViewModel vm) vm.OpenPlaylistCommand.Execute(null);
    }

    /// <summary>Doppelklick auf einen Titel spielt ihn ab.</summary>
    void OnTrackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is YtViewModel vm) vm.PlayTrackCommand.Execute(null);
    }

    /// <summary>Meldet die Scroll-Stelle der Playlist- oder Titelliste an das ViewModel (zum Speichern).</summary>
    void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv || DataContext is not YtViewModel vm) return;
        // Startseite: nah am Ende kommen weitere Bereiche.
        if (sv.Name == "HomeScroll" && sv.Extent.Height - (sv.Offset.Y + sv.Viewport.Height) < 300) _ = vm.LoadMoreHomeAsync();
        var list = sv.FindAncestorOfType<ListBox>();
        if (list?.Name == "TrackList")
        {
            vm.NoteScroll(true, sv.Offset.Y);
            // Unendliches Nachladen: nah am Listenende kommt die nächste Seite Suchtreffer.
            if (vm.IsSearch && sv.Extent.Height - (sv.Offset.Y + sv.Viewport.Height) < 240) _ = vm.LoadMoreAsync();
        }
        else if (list?.Name == "List") vm.NoteScroll(false, sv.Offset.Y);
    }

    /// <summary>
    /// Springt zur gespeicherten Scroll-Stelle. Die Liste ist beim Start noch nicht gemessen, darum mehrere Versuche,
    /// bis sie hoch genug ist.
    /// </summary>
    async void ApplyScroll(bool tracks, double offset)
    {
        if (offset < 1) return;
        var list = this.FindControl<ListBox>(tracks ? "TrackList" : "List");
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(i == 0 ? 200 : 300);
            var sv = list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null || sv.Viewport.Height <= 0) continue;
            var max = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            if (max <= 0) continue;
            sv.Offset = new Vector(sv.Offset.X, Math.Min(offset, max));
            if (offset <= max) return;
        }
    }
}
