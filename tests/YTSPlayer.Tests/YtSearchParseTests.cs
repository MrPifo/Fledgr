using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class YtSearchParseTests
{
    const string Response = """
    { "contents": { "x": [
      { "musicResponsiveListItemRenderer": {
          "playlistItemData": { "videoId": "VID1" },
          "thumbnail": { "musicThumbnailRenderer": { "thumbnail": { "thumbnails": [ { "url": "http://a" }, { "url": "http://b" } ] } } },
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Mein Lied" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Band" }, { "text": " & " }, { "text": "Gast" }, { "text": " • " }, { "text": "Album X" }, { "text": " • " }, { "text": "3:45" } ] } } }
          ] } },
      { "musicResponsiveListItemRenderer": {
          "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Nur ein Künstler" } ] } } } ] } },
      { "musicResponsiveListItemRenderer": {
          "playlistItemData": { "videoId": "VID1" },
          "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Doppelt" } ] } } } ] } }
    ] } }
    """;

    [Fact]
    public void Song_Treffer_werden_gelesen_Zeilen_ohne_Video_Id_und_Doppelte_nicht()
    {
        var items = YtLibrary.ParseSearch(Response);
        var t = Assert.Single(items);
        Assert.Equal("VID1", t.Id);
        Assert.Equal("Mein Lied", t.Title);
        Assert.Equal("Band & Gast", t.Artist);
        Assert.Equal("3:45", t.Duration);
        Assert.Equal("http://b", t.Thumb);
    }

    const string Mixed = """
    { "x": [
      { "musicResponsiveListItemRenderer": { "navigationEndpoint": { "browseEndpoint": { "browseId": "UCabc" } },
          "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Die Band" } ] } } },
                           { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Künstler" }, { "text": " • " }, { "text": "1 Mio. Abos" } ] } } } ] } },
      { "musicResponsiveListItemRenderer": { "navigationEndpoint": { "browseEndpoint": { "browseId": "MPREb_1" } },
          "overlay": { "x": { "watchPlaylistEndpoint": { "playlistId": "OLAK5uy_x" } } },
          "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Das Album" } ] } } } ] } },
      { "musicResponsiveListItemRenderer": { "navigationEndpoint": { "browseEndpoint": { "browseId": "VLPLxyz" } },
          "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Meine Liste" } ] } } } ] } },
      { "continuationItemRenderer": { "continuationEndpoint": { "continuationCommand": { "token": "MORE" } } } }
    ] }
    """;

    [Fact]
    public void Kuenstler_Alben_und_Playlists_werden_nach_Art_ausgewaehlt()
    {
        var a = Assert.Single(YtLibrary.ParseSearch(Mixed, "artist"));
        Assert.Equal(("UCabc", "artist", "Die Band"), (a.Id, a.Kind, a.Title));
        Assert.Equal("Künstler • 1 Mio. Abos", a.Artist);
        var al = Assert.Single(YtLibrary.ParseSearch(Mixed, "album"));
        Assert.Equal(("OLAK5uy_x", "album"), (al.Ref, al.Kind));
        var p = Assert.Single(YtLibrary.ParseSearch(Mixed, "playlist"));
        Assert.Equal(("PLxyz", "playlist"), (p.Ref, p.Kind));
    }

    [Fact]
    public void Zeichen_fuer_die_naechste_Seite_wird_gelesen()
    {
        Assert.Equal("MORE", YtLibrary.ParseSearchPage(Mixed, "artist").Next);
        Assert.Null(YtLibrary.ParseSearchPage(Response).Next);
    }

    [Fact]
    public void Kaputte_Suchantwort_gibt_leere_Liste()
    {
        Assert.Empty(YtLibrary.ParseSearch("kein json"));
    }
}
