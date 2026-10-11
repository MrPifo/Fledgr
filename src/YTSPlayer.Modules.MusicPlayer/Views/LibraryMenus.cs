using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using YTSPlayer.Contracts.Localization;
using YTSPlayer.Modules.MusicPlayer.ViewModels;
using YTSPlayer.Ui.Controls;

namespace YTSPlayer.Modules.MusicPlayer.Views;

/// <summary>Das Drei-Punkte-Menü der Titelzeilen in der lokalen Bibliothek.</summary>
internal static class LibraryMenus
{
    /// <summary>Menü für <paramref name="tracks"/> (die Auswahl oder die einzelne Zeile), verankert am Knopf.</summary>
    public static void ShowTrack(LibraryViewModel vm, IReadOnlyList<LibraryItem> tracks, LibraryItem row, Control anchor)
    {
        var menu = new MenuFlyout { ShowMode = FlyoutShowMode.Transient };
        menu.Items.Add(Entry(Loc.T("music.menu.play"), MaterialIconKind.Play, () => _ = vm.PlaySelectionAsync(tracks)));

        var add = Entry(Loc.T("music.menu.addToPlaylist"), MaterialIconKind.PlaylistPlus);
        add.Items.Add(Entry(Loc.T("music.menu.newPlaylist"), MaterialIconKind.PlaylistPlus, () => _ = CreateAsync(vm, tracks, anchor)));
        add.Items.Add(new Separator());
        if (vm.Playlists.Count == 0) add.Items.Add(new MenuItem { Header = Loc.T("music.menu.noPlaylist"), IsEnabled = false });
        foreach (var p in vm.Playlists.ToList())
            add.Items.Add(Entry(p.Name, MaterialIconKind.PlaylistMusic, () => vm.AddToPlaylist(tracks, p)));
        menu.Items.Add(add);

        if (vm.InPlaylist)
            menu.Items.Add(Entry(Loc.T("music.view.removeFrom"), MaterialIconKind.PlaylistRemove, () => _ = vm.RemoveFromPlaylistAsync(tracks)));

        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("music.menu.showInFolder"), MaterialIconKind.FolderOpen, () => _ = ShowInFolderAsync(row, anchor)));
        menu.ShowAbove(anchor);
    }

    static async Task CreateAsync(LibraryViewModel vm, IReadOnlyList<LibraryItem> tracks, Control anchor)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner) return;
        var name = await TextPromptWindow.AskAsync(owner, Loc.T("music.menu.newPlaylist.title"), Loc.T("music.menu.newPlaylist.prompt"), Loc.T("music.menu.newPlaylist.ok"));
        if (name != null) vm.CreatePlaylistWith(name, tracks);
    }

    /// <summary>Öffnet den Ordner der Datei (der Datei-Manager des Systems).</summary>
    static async Task ShowInFolderAsync(LibraryItem row, Control anchor)
    {
        var dir = Path.GetDirectoryName(row.Node.Id);
        if (dir == null || !Directory.Exists(dir) || TopLevel.GetTopLevel(anchor)?.Launcher is not { } launcher) return;
        try { await launcher.LaunchUriAsync(new Uri(dir)); }
        catch { /* kein Datei-Manager verfügbar */ }
    }

    static MenuItem Entry(string header, MaterialIconKind icon, Action? action = null)
    {
        var item = new MenuItem { Header = header, Icon = new MaterialIcon { Kind = icon } };
        if (action != null) item.Click += (_, _) => action();
        return item;
    }
}
