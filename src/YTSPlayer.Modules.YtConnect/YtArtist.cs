using System.Text.Json;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>Ein Bereich der Künstler-Seite (z. B. „Alben“, „Singles &amp; EPs“, „Fans mögen auch“) mit seinen Karten.</summary>
public sealed record ArtistSectionData(string Title, List<TrackInfo> Items);

/// <summary>
/// Die Künstler-Seite: Kopf (Name, Zuhörer, Beschreibung, Bild, Knöpfe), die Top-Songs und die Bereiche darunter.
/// <paramref name="PlayPlaylist"/> und <paramref name="RadioPlaylist"/> sind die Playlists hinter „Zufallsmix“ und „Mix“ (können leer sein).
/// </summary>
public sealed record ArtistPage(
    string Id, string Name, string Listeners, string Subscribers, bool Subscribed, string Description, string Thumb,
    string PlayPlaylist, string RadioPlaylist, string TopSongsTitle, List<TrackInfo> TopSongs, string TopSongsMore, List<ArtistSectionData> Sections);

/// <summary>Liest die Antwort der Künstler-Seite (InnerTube <c>browse</c> mit der Kennung UC…). Die Struktur ist aus der echten Seite abgeleitet, aber nicht gegen jedes Konto geprüft.</summary>
public static class YtArtistParser
{
    /// <summary>Gibt null zurück, wenn die Antwort keinen Künstler-Kopf enthält.</summary>
    public static ArtistPage? Parse(string json, string browseId)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var root = d.RootElement;
            var header = YtLibrary.FindProperty(root, "musicImmersiveHeaderRenderer") ?? YtLibrary.FindProperty(root, "musicVisualHeaderRenderer");
            if (header is not { } h) return null;

            var name = Text(h, "title");
            if (name.Length == 0) return null;
            var description = Text(h, "description");
            if (description.Length == 0 && YtLibrary.FindProperty(root, "musicDescriptionShelfRenderer") is { } desc) description = Text(desc, "description");
            var listeners = Text(h, "monthlyListenerCount");

            var subscribed = false;
            var subscribers = "";
            if (YtLibrary.FindProperty(h, "subscribeButtonRenderer") is { } sb)
            {
                subscribed = sb.TryGetProperty("subscribed", out var s) && s.ValueKind == JsonValueKind.True;
                subscribers = Text(sb, "subscriberCountText");
            }

            var play = WatchPlaylist(h, "playButton");
            var radio = WatchPlaylist(h, "startRadioButton");

            var top = new List<TrackInfo>();
            var topTitle = "";
            var topMore = "";
            var sections = new List<ArtistSectionData>();
            if (YtLibrary.FindProperty(root, "sectionListRenderer") is { } list && list.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in contents.EnumerateArray())
                {
                    if (block.TryGetProperty("musicShelfRenderer", out var shelf))
                    {
                        var rows = Rows(shelf);
                        if (rows.Count == 0) continue;
                        if (top.Count == 0)
                        {
                            top = rows;
                            topTitle = Text(shelf, "title");
                            // „Alle anzeigen“ führt zu einer Playlist mit allen Titeln (VL…).
                            if (YtLibrary.FindString(shelf, "browseId") is { } more && more.StartsWith("VL", StringComparison.Ordinal)) topMore = more[2..];
                        }
                        else sections.Add(new ArtistSectionData(Text(shelf, "title"), rows));
                    }
                    else if (block.TryGetProperty("musicCarouselShelfRenderer", out var car))
                    {
                        var title = car.TryGetProperty("header", out var hd) && YtLibrary.FindProperty(hd, "title") is { } t ? Join(t) : "";
                        var cards = Cards(car);
                        if (cards.Count > 0) sections.Add(new ArtistSectionData(title, cards));
                    }
                }
            }
            return new ArtistPage(browseId, name, listeners, subscribers, subscribed, description, YtLibrary.ThumbOf(h), play, radio, topTitle, top, topMore, sections);
        }
        catch
        {
            return null;
        }
    }

    static List<TrackInfo> Rows(JsonElement shelf)
    {
        var rows = new List<TrackInfo>();
        if (shelf.TryGetProperty("contents", out var c) && c.ValueKind == JsonValueKind.Array)
            foreach (var e in c.EnumerateArray())
                if (e.TryGetProperty("musicResponsiveListItemRenderer", out var r) && YtLibrary.ToTrack(r) is { } t) rows.Add(t);
        return rows;
    }

    /// <summary>Die Karten eines Bereichs: Alben, Playlists, Künstler, Videos. Videos zählen wie Titel (Art leer).</summary>
    internal static List<TrackInfo> Cards(JsonElement carousel)
    {
        var cards = new List<TrackInfo>();
        if (!carousel.TryGetProperty("contents", out var c) || c.ValueKind != JsonValueKind.Array) return cards;
        foreach (var e in c.EnumerateArray())
        {
            // Auf der Startseite stehen in manchen Bereichen („Schnelle Auswahl“) Titel-Zeilen statt Karten.
            if (e.TryGetProperty("musicResponsiveListItemRenderer", out var row))
            {
                if (YtLibrary.ToTrack(row) is { } track) cards.Add(track);
                continue;
            }
            if (!e.TryGetProperty("musicTwoRowItemRenderer", out var r)) continue;
            var title = Text(r, "title");
            if (title.Length == 0) continue;
            var sub = Text(r, "subtitle");
            var thumb = r.TryGetProperty("thumbnailRenderer", out var tr) ? YtLibrary.ThumbOf(tr.TryGetProperty("musicThumbnailRenderer", out _) ? Wrap(tr) : tr) : "";
            var nav = r.TryGetProperty("navigationEndpoint", out var n) ? n : default;
            var browse = nav.ValueKind == JsonValueKind.Object && YtLibrary.FindProperty(nav, "browseEndpoint") is { } be && be.TryGetProperty("browseId", out var bid) ? bid.GetString() ?? "" : "";
            if (browse.StartsWith("MPRE", StringComparison.Ordinal))
            {
                if (YtLibrary.FindString(r, "playlistId") is { Length: > 0 } album) cards.Add(new TrackInfo(album, title, sub, "", thumb, "", "album", album));
            }
            else if (browse.StartsWith("UC", StringComparison.Ordinal)) cards.Add(new TrackInfo(browse, title, sub, "", thumb, "", "artist", title));
            else if (browse.StartsWith("VL", StringComparison.Ordinal)) cards.Add(new TrackInfo(browse[2..], title, sub, "", thumb, "", "playlist", browse[2..]));
            else if (nav.ValueKind == JsonValueKind.Object && YtLibrary.FindString(nav, "videoId") is { Length: > 0 } vid) cards.Add(new TrackInfo(vid, title, sub, "", thumb));
        }
        return cards;
    }

    /// <summary>ThumbOf erwartet ein Objekt, das „thumbnail.musicThumbnailRenderer“ enthält. Hier hat das Element schon „musicThumbnailRenderer“ direkt.</summary>
    static JsonElement Wrap(JsonElement withRenderer)
    {
        using var doc = JsonDocument.Parse("{\"thumbnail\":" + withRenderer.GetRawText() + "}");
        return doc.RootElement.Clone();
    }

    static string WatchPlaylist(JsonElement header, string button) =>
        YtLibrary.FindProperty(header, button) is { } b && YtLibrary.FindProperty(b, "watchEndpoint") is { } w && w.TryGetProperty("playlistId", out var p) ? p.GetString() ?? "" : "";

    /// <summary>Der Text einer Eigenschaft (Teile zusammengesetzt) oder leer.</summary>
    static string Text(JsonElement e, string property) => e.TryGetProperty(property, out var p) ? Join(p) : "";

    static string Join(JsonElement t)
    {
        if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("simpleText", out var s)) return s.GetString() ?? "";
        if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            return string.Concat(runs.EnumerateArray().Select(x => x.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "")).Trim();
        return "";
    }
}
