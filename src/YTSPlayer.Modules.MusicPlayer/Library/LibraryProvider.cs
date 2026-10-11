using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.MusicPlayer.Library;

/// <summary>Medien-Baum der lokalen Bibliothek: Ordner, Playlists, Favoriten, Titel.</summary>
public sealed class LibraryProvider(LibraryService library) : IMediaProvider
{
    /// <summary>Knoten, der alle Playlists enthält.</summary>
    public static MediaNode PlaylistsNode => new("@playlists", Loc.T("music.mode.playlists"), MediaNodeKind.Folder);

    /// <summary>Knoten, der alle Favoriten enthält.</summary>
    public static MediaNode FavoritesNode => new("@favorites", Loc.T("music.mode.favorites"), MediaNodeKind.Folder);

    /// <summary>Knoten, der alle Interpreten enthält (aus den Tags).</summary>
    public static MediaNode ArtistsNode => new("@artists", Loc.T("music.mode.artists"), MediaNodeKind.Folder);

    /// <summary>Knoten, der alle Alben enthält (aus den Tags).</summary>
    public static MediaNode AlbumsNode => new("@albums", Loc.T("music.mode.albums"), MediaNodeKind.Folder);

    static string NoArtist => Loc.T("music.unknownArtist");
    static string NoAlbum => Loc.T("music.unknownAlbum");
    const string ArtistPrefix = "@artist:";
    const string AlbumPrefix = "@album:";
    const char Sep = '\u001f';

    public string Id => "local";
    public string Title => Loc.T("music.source");

    static string ArtistOf(LibraryEntry e) => e.Artist.Length > 0 ? e.Artist : NoArtist;
    static string AlbumOf(LibraryEntry e) => e.Album.Length > 0 ? e.Album : NoAlbum;

    /// <summary>Gibt es gelesene Tags? Ohne sie bleiben die Ansichten „Interpreten“ und „Alben“ leer.</summary>
    public bool HasTags => library.Entries.Any(e => e.Artist.Length > 0 || e.Album.Length > 0);

    /// <summary>Titel in Album-Reihenfolge: nach Titelnummer, sonst nach Dateiname.</summary>
    static IEnumerable<LibraryEntry> InAlbumOrder(IEnumerable<LibraryEntry> tracks) =>
        tracks.OrderBy(e => e.TrackNo == 0 ? int.MaxValue : e.TrackNo).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase);

    static MediaNode EntryNode(LibraryEntry e)
    {
        var title = e.Title.Length > 0 ? e.Title : e.Name;
        var sub = e.Artist.Length > 0 ? e.Artist : Path.GetFileName(Path.GetDirectoryName(e.Path) ?? "");
        return new MediaNode(e.Path, title, MediaNodeKind.Track, sub, LibraryService.ToItem(e.Path) with { Title = title, Artist = e.Artist, Album = e.Album });
    }

    /// <summary>Interpreten, Alben eines Interpreten, alle Alben und die Titel eines Albums. Aus dem Index, ohne Dateizugriff.</summary>
    IReadOnlyList<MediaNode>? VirtualChildren(MediaNode parent)
    {
        var entries = library.Entries;
        if (parent.Id == ArtistsNode.Id)
            return [.. entries.GroupBy(ArtistOf).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new MediaNode(ArtistPrefix + g.Key, g.Key, MediaNodeKind.Folder, Loc.F("music.artist.sub", g.Select(AlbumOf).Distinct().Count(), g.Count())))];
        if (parent.Id.StartsWith(ArtistPrefix, StringComparison.Ordinal))
        {
            var artist = parent.Id[ArtistPrefix.Length..];
            return [.. entries.Where(e => ArtistOf(e) == artist).GroupBy(AlbumOf).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => AlbumNode(artist, g.Key, g))];
        }
        if (parent.Id == AlbumsNode.Id)
            return [.. entries.GroupBy(e => (Artist: ArtistOf(e), Album: AlbumOf(e))).OrderBy(g => g.Key.Album, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => AlbumNode(g.Key.Artist, g.Key.Album, g, withArtist: true))];
        if (parent.Id.StartsWith(AlbumPrefix, StringComparison.Ordinal))
        {
            var key = parent.Id[AlbumPrefix.Length..].Split(Sep);
            if (key.Length != 2) return [];
            return [.. InAlbumOrder(entries.Where(e => ArtistOf(e) == key[0] && AlbumOf(e) == key[1])).Select(EntryNode)];
        }
        return null;
    }

    /// <summary>Ein Album als Knoten. Der Knoten trägt den ersten Titel mit (für das Cover in der Liste).</summary>
    static MediaNode AlbumNode(string artist, string album, IEnumerable<LibraryEntry> tracks, bool withArtist = false)
    {
        var list = tracks as IReadOnlyCollection<LibraryEntry> ?? [.. tracks];
        var first = InAlbumOrder(list).First();
        return new MediaNode(AlbumPrefix + artist + Sep + album, album, MediaNodeKind.Folder,
            (withArtist ? artist + " · " : "") + Loc.P("music.tracks", list.Count), LibraryService.ToItem(first.Path));
    }

    public async Task<IReadOnlyList<MediaNode>> GetChildrenAsync(MediaNode? parent, CancellationToken ct = default)
    {
        if (parent == null)
        {
            var roots = library.Settings.Folders
                .Select(f => new MediaNode(f, FolderName(f), MediaNodeKind.Folder, f))
                .ToList();
            roots.Add(ArtistsNode);
            roots.Add(AlbumsNode);
            roots.Add(PlaylistsNode);
            roots.Add(FavoritesNode);
            return roots;
        }
        if (await Task.Run(() => VirtualChildren(parent), ct) is { } virtualNodes) return virtualNodes;
        if (parent.Id == PlaylistsNode.Id)
            return library.Settings.Playlists
                .Select(p => new MediaNode("pl:" + p.Id, p.Name, MediaNodeKind.Playlist, Loc.P("music.tracks", p.Paths.Count)))
                .ToList();
        if (parent.Id == FavoritesNode.Id)
            return library.Settings.Favorites.Where(File.Exists).Select(TrackNode).ToList();
        if (parent.Id.StartsWith("pl:"))
        {
            var pl = library.Settings.Playlists.FirstOrDefault(p => "pl:" + p.Id == parent.Id);
            return pl == null ? [] : pl.Paths.Where(File.Exists).Select(TrackNode).ToList();
        }
        var (dirs, files) = await library.BrowseAsync(parent.Id, ct);
        var nodes = new List<MediaNode>(dirs.Count + files.Count);
        nodes.AddRange(dirs.Select(d => new MediaNode(d, FolderName(d), MediaNodeKind.Folder)));
        nodes.AddRange(files.Select(TrackNode));
        return nodes;
    }

    public async Task<IReadOnlyList<MediaNode>> SearchAsync(string query, CancellationToken ct = default)
    {
        var hits = await library.SearchAsync(query, 500, ct);
        return hits.Select(h =>
        {
            // Mit gelesenen Tags: Titel und Interpret statt Dateiname.
            var title = h.Title.Length > 0 ? h.Title : h.Name;
            var dir = Path.GetDirectoryName(h.Relative) ?? "";
            var sub = h.Artist.Length > 0 ? (dir.Length > 0 ? h.Artist + " · " + dir : h.Artist) : dir;
            return new MediaNode(h.Path, title, MediaNodeKind.Track, sub, LibraryService.ToItem(h.Path) with { Title = title, Artist = h.Artist });
        }).ToList();
    }

    static MediaNode TrackNode(string path) =>
        new(path, Path.GetFileNameWithoutExtension(path), MediaNodeKind.Track, Path.GetFileName(Path.GetDirectoryName(path) ?? ""), LibraryService.ToItem(path));

    static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
