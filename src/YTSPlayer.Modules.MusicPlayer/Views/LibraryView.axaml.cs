using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using YTSPlayer.Modules.MusicPlayer.ViewModels;

namespace YTSPlayer.Modules.MusicPlayer.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new FocusSearchCommand(this) });
    }

    LibraryViewModel? Vm => DataContext as LibraryViewModel;

    async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null || Vm == null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Musik- oder Sound-Ordner wählen", AllowMultiple = false });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path != null) await Vm.AddFolderAsync(path);
    }

    /// <summary>Die markierten Zeilen der Liste (bei Mehrfachauswahl alle).</summary>
    List<LibraryItem> Selected => [.. List.SelectedItems?.OfType<LibraryItem>() ?? []];

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

    sealed class FocusSearchCommand(LibraryView view) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            view.SearchBox.Focus();
            view.SearchBox.SelectAll();
        }
    }
}
