using Avalonia.Controls;
using Avalonia.Input.Platform;
using Material.Icons;
using Material.Icons.Avalonia;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Modules.YtConnect.ViewModels;
using YTSPlayer.Ui.Controls;

namespace YTSPlayer.Modules.YtConnect.Views;

/// <summary>
/// Die Drei-Punkte-Menüs der YT-Seite: je Playlist, je Titel der Liste und für den laufenden Titel (Player-Leiste).
/// Sie liegen hier, damit die Liste und die Player-Leiste dieselben Einträge zeigen.
/// </summary>
internal static class YtMenus
{
    /// <summary>Verbindet das ViewModel mit Zwischenablage und Browser des Fensters, in dem das Menü gerade erscheint.</summary>
    static void Attach(YtViewModel vm, Control anchor)
    {
        var top = TopLevel.GetTopLevel(anchor);
        vm.CopyText = text => top?.Clipboard is { } c ? c.SetTextAsync(text) : Task.CompletedTask;
        vm.OpenUrl = url =>
        {
            if (top?.Launcher is { } l && Uri.TryCreate(url, UriKind.Absolute, out var uri)) _ = l.LaunchUriAsync(uri);
        };
    }

    /// <summary>Das Menü einer Playlist.</summary>
    public static void ShowPlaylist(YtViewModel vm, PlaylistInfo p, Control anchor, bool atPointer = false)
    {
        Attach(vm, anchor);
        var menu = new MenuFlyout();
        menu.Items.Add(Entry("Mix starten", MaterialIconKind.RadioTower, () => vm.StartPlaylistMixAsync(p)));
        menu.Items.Add(Chooser("Zu Playlist hinzufügen", vm, p.Id, target => vm.AddPlaylistToAsync(p, target), null));
        menu.Items.Add(Entry("Teilen (Link kopieren)", MaterialIconKind.ShareVariant, () => vm.ShareAsync(UrlNormalizer.SharePlaylist(p.Id))));
        menu.Items.Add(Entry("Auf YouTube öffnen", MaterialIconKind.OpenInNew, () => { vm.OpenOnYoutube(UrlNormalizer.YoutubePlaylist(p.Id)); return Task.CompletedTask; }));
        if (YtLibrary.CanDelete(p.Id))
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Entry("Löschen …", MaterialIconKind.Delete, () => { vm.AskDelete(p); return Task.CompletedTask; }));
        }
        menu.ShowAt(anchor, atPointer);
    }

    /// <summary>Das Menü eines Titels in der geöffneten Playlist.</summary>
    public static void ShowTrack(YtViewModel vm, TrackItem t, Control anchor, bool atPointer = false)
    {
        Attach(vm, anchor);
        var menu = new MenuFlyout();
        if (t.Info.Kind != "")
        {
            // Treffer der Suche, die keine Titel sind: Künstler, Alben, Playlists.
            var id = t.Info.Id;
            if (t.Info.Kind == "artist")
            {
                menu.Items.Add(Entry("Teilen (Link kopieren)", MaterialIconKind.ShareVariant, () => vm.ShareAsync("https://music.youtube.com/channel/" + Uri.EscapeDataString(id))));
                menu.Items.Add(Entry("Auf YouTube öffnen", MaterialIconKind.OpenInNew, () => { vm.OpenOnYoutube("https://www.youtube.com/channel/" + Uri.EscapeDataString(id)); return Task.CompletedTask; }));
            }
            else
            {
                var pl = new PlaylistInfo(t.Info.Ref, t.Info.Title, t.Info.Artist);
                menu.Items.Add(Entry("Mix starten", MaterialIconKind.RadioTower, () => vm.StartPlaylistMixAsync(pl)));
                menu.Items.Add(Chooser("Zu Playlist hinzufügen", vm, null, target => vm.AddPlaylistToAsync(pl, target), null));
                menu.Items.Add(Entry("Teilen (Link kopieren)", MaterialIconKind.ShareVariant, () => vm.ShareAsync(UrlNormalizer.SharePlaylist(pl.Id))));
                menu.Items.Add(Entry("Auf YouTube öffnen", MaterialIconKind.OpenInNew, () => { vm.OpenOnYoutube(UrlNormalizer.YoutubePlaylist(pl.Id)); return Task.CompletedTask; }));
            }
            menu.ShowAt(anchor, atPointer);
            return;
        }
        AddTrackEntries(menu, vm, t.Info, anchor);
        if (vm.Opened != null && !vm.IsSearch) menu.Items.Insert(2, Entry("Aus dieser Playlist entfernen", MaterialIconKind.PlaylistRemove, () => vm.RemoveTrackAsync(t)));
        // Der Zustand „Gefällt mir“ der Listen-Titel ist nicht bekannt, darum zwei Einträge statt eines Schalters.
        menu.Items.Insert(1, Entry("Gefällt mir", MaterialIconKind.ThumbUp, () => vm.LikeTrackAsync(t.Info, true)));
        menu.Items.Insert(2, Entry("Gefällt mir entfernen", MaterialIconKind.ThumbUpOutline, () => vm.LikeTrackAsync(t.Info, false)));
        menu.ShowAt(anchor, atPointer);
    }

    /// <summary>Die drei Punkte im Kopf der Künstler-Seite.</summary>
    public static void ShowArtist(YtViewModel vm, Control anchor)
    {
        Attach(vm, anchor);
        var menu = new MenuFlyout();
        menu.Items.Add(Entry("Teilen (Link kopieren)", MaterialIconKind.ShareVariant, () => { vm.ArtistShareCommand.Execute(null); return Task.CompletedTask; }));
        menu.Items.Add(Entry("Auf YouTube öffnen", MaterialIconKind.OpenInNew, () => { vm.ArtistOnYoutubeCommand.Execute(null); return Task.CompletedTask; }));
        menu.ShowAt(anchor);
    }

    /// <summary>Das Menü des laufenden Titels (aus der Player-Leiste oder dem Widget).</summary>
    public static void ShowCurrent(YtViewModel vm, PlaybackState st, string artistId, Control anchor)
    {
        Attach(vm, anchor);
        var menu = new MenuFlyout();
        if (UrlNormalizer.VideoId(st.Item?.Id) is not { } id)
            menu.Items.Add(new MenuItem { Header = "Zu diesem Titel gibt es kein Menü", IsEnabled = false });
        else
        {
            var dur = st.Duration.TotalSeconds > 0 ? $"{(int)st.Duration.TotalMinutes}:{st.Duration.Seconds:00}" : "";
            AddTrackEntries(menu, vm, new TrackInfo(id, st.Title, st.Artist, dur, st.CoverUrl ?? "", "", "", "", artistId), anchor);
            // Läuft der Titel aus der gerade geöffneten Playlist, kann man ihn gleich daraus entfernen.
            if (vm.Opened is { } opened && vm.Tracks.FirstOrDefault(x => x.Info.Id == id) is { } row)
                menu.Items.Insert(2, Entry($"Aus „{opened.Title}“ entfernen", MaterialIconKind.PlaylistRemove, () => vm.RemoveTrackAsync(row)));
        }
        menu.ShowAt(anchor);
    }

    static void AddTrackEntries(MenuFlyout menu, YtViewModel vm, TrackInfo t, Control anchor)
    {
        menu.Items.Add(Entry("Mix starten", MaterialIconKind.RadioTower, () => vm.StartTrackMixAsync(t)));
        // Die geöffnete Playlist fehlt nur dann in der Auswahl, wenn der Titel schon darin steht.
        var except = vm.Opened is { } open && vm.Tracks.Any(x => x.Info.Id == t.Id) ? open.Id : null;
        menu.Items.Add(Chooser("Zu Playlist hinzufügen", vm, except, target => vm.AddTrackToAsync(t, target), () => NewPlaylistAsync(vm, anchor, t.Id)));
        if (t.Artist.Length > 0 || t.ArtistId.Length > 0)
            menu.Items.Add(Entry("Zur Künstler-Seite", MaterialIconKind.AccountMusic, () => vm.OpenArtistOfAsync(t.ArtistId, t.Artist)));
        menu.Items.Add(Entry("Teilen (Link kopieren)", MaterialIconKind.ShareVariant, () => vm.ShareAsync(UrlNormalizer.ShareTrack(t.Id))));
        menu.Items.Add(Entry("Auf YouTube öffnen", MaterialIconKind.OpenInNew, () => { vm.OpenOnYoutube(UrlNormalizer.YoutubeTrack(t.Id)); return Task.CompletedTask; }));
    }

    /// <summary>Fragt nach einem Namen und legt eine neue Playlist an (mit <paramref name="videoId"/> als erstem Titel, wenn angegeben).</summary>
    public static async Task NewPlaylistAsync(YtViewModel vm, Control anchor, string? videoId)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner) return;
        var name = await TextPromptWindow.AskAsync(owner, "Neue Playlist", videoId == null ? "Name der neuen Playlist:" : "Name der neuen Playlist (der Titel wird gleich hinzugefügt):", "Anlegen");
        if (name != null) await vm.CreatePlaylistAsync(name, videoId);
    }

    static MenuItem Entry(string header, MaterialIconKind icon, Func<Task>? action = null)
    {
        var item = new MenuItem { Header = header, Icon = new MaterialIcon { Kind = icon } };
        if (action != null) item.Click += async (_, _) => await action();
        return item;
    }

    /// <summary>Eintrag mit Untermenü: alle Playlists außer <paramref name="exceptId"/>, dazu (wenn <paramref name="createNew"/> gesetzt ist) „Neue Playlist …“.</summary>
    static MenuItem Chooser(string header, YtViewModel vm, string? exceptId, Func<PlaylistInfo, Task> choose, Func<Task>? createNew)
    {
        var parent = Entry(header, MaterialIconKind.PlaylistPlus);
        if (createNew != null)
        {
            parent.Items.Add(Entry("Neue Playlist …", MaterialIconKind.PlaylistPlus, createNew));
            parent.Items.Add(new Separator());
        }
        var targets = vm.Items.Where(i => i.Id != exceptId && i.Id != YtLibrary.LikedId).ToList();
        if (targets.Count == 0) parent.Items.Add(new MenuItem { Header = "Keine andere Playlist", IsEnabled = false });
        foreach (var target in targets)
            parent.Items.Add(Entry(target.Title, MaterialIconKind.PlaylistMusic, () => choose(target)));
        return parent;
    }
}
