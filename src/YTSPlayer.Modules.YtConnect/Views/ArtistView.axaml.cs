using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Material.Icons;
using YTSPlayer.Modules.YtConnect.ViewModels;

namespace YTSPlayer.Modules.YtConnect.Views;

public partial class ArtistView : UserControl
{
    public ArtistView() => InitializeComponent();

    YtViewModel? Vm => DataContext as YtViewModel;

    /// <summary>Doppelklick auf einen Top-Song spielt ihn ab.</summary>
    void OnArtistSongDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm != null && (sender as ListBox)?.SelectedItem is TrackItem t) _ = Vm.OpenCardAsync(t);
    }

    void OnTrackMenu(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && sender is Button b && b.DataContext is TrackItem t) YtMenus.ShowTrack(Vm, t, b);
    }

    void OnTrackContext(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm == null || sender is not Control c || c.DataContext is not TrackItem t) return;
        e.Handled = true;
        YtMenus.ShowTrack(Vm, t, c, atPointer: true);
    }

    void OnMenuButtonDoubleTapped(object? sender, TappedEventArgs e) => e.Handled = true;

    /// <summary>Die drei Punkte im Kopf: Link kopieren, auf YouTube öffnen.</summary>
    void OnArtistMore(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || sender is not Control c) return;
        YtMenus.ShowArtist(Vm, c);
    }
}
