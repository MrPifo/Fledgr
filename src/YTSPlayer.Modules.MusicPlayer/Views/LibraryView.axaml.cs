using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YTSPlayer.Modules.MusicPlayer.ViewModels;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.MusicPlayer.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
    }

    LibraryViewModel? Vm => DataContext as LibraryViewModel;

    async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null || Vm == null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("music.pickFolder"), AllowMultiple = false });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path != null) await Vm.AddFolderAsync(path);
    }

    /// <summary>Die markierten Zeilen der Liste (bei Mehrfachauswahl alle).</summary>
    List<LibraryItem> Selected => [.. List.SelectedItems?.OfType<LibraryItem>() ?? []];

    /// <summary>Drei-Punkte-Menü einer Zeile. Gehört die Zeile zur Auswahl, gilt das Menü für die ganze Auswahl, sonst nur für sie.</summary>
    void OnRowMenu(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control anchor || anchor.DataContext is not LibraryItem row) return;
        var sel = Selected;
        LibraryMenus.ShowTrack(vm, sel.Contains(row) ? sel : [row], row, anchor);
        e.Handled = true;
    }

    void OnPlaySelection(object? sender, RoutedEventArgs e) => _ = Vm?.PlaySelectionAsync(Selected);

    void OnAddToPlaylist(object? sender, RoutedEventArgs e) => Vm?.AddToPlaylist(Selected);

    void OnRemoveFromPlaylist(object? sender, RoutedEventArgs e) => _ = Vm?.RemoveFromPlaylistAsync(Selected);

    /// <summary>Nur Dateien und Ordner dürfen auf die Bibliothek fallen.</summary>
    void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    /// <summary>Abgelegte Ordner kommen in die Bibliothek, abgelegte Audio-Dateien spielen sofort.</summary>
    async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm == null || e.DataTransfer.TryGetFiles() is not { } items) return;
        var paths = items.Select(i => i.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
        await Vm.HandleDropAsync(paths);
    }

    void OnDoubleTapped(object? sender, TappedEventArgs e) => Vm?.OpenSelectedCommand.Execute(null);

    void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm?.OpenSelectedCommand.Execute(null);
        e.Handled = true;
    }
}
