using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class YtTrackParseTests
{
    const string Page = """
    { "contents": { "x": [
      { "musicPlaylistShelfRenderer": { "contents": [
        { "musicResponsiveListItemRenderer": {
            "playlistItemData": { "videoId": "AAA", "playlistSetVideoId": "S1" },
            "thumbnail": { "musicThumbnailRenderer": { "thumbnail": { "thumbnails": [ { "url": "http://small" }, { "url": "http://big" } ] } } },
            "flexColumns": [
              { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Titel A" } ] } } },
              { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Künstler" }, { "text": " & " }, { "text": "Gast" } ] } } }
            ],
            "fixedColumns": [ { "musicResponsiveListItemFixedColumnRenderer": { "text": { "runs": [ { "text": "3:21" } ] } } } ] } },
        { "musicResponsiveListItemRenderer": { "flexColumns": [] } },
        { "continuationItemRenderer": { "continuationEndpoint": { "continuationCommand": { "token": "NEXT" } } } }
      ] } },
      { "musicCarouselShelfRenderer": { "contents": [
        { "musicResponsiveListItemRenderer": { "playlistItemData": { "videoId": "SUGG" },
          "flexColumns": [ { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Vorschlag" } ] } } } ] } }
      ] } }
    ] } }
    """;

    [Fact]
    public void Titel_der_Playlist_werden_gelesen_Vorschlaege_nicht()
    {
        var (items, next) = YtLibrary.ParseTracks(Page);
        var t = Assert.Single(items);
        Assert.Equal("AAA", t.Id);
        Assert.Equal("S1", t.SetId);
        Assert.Equal("Titel A", t.Title);
        Assert.Equal("Künstler & Gast", t.Artist);
        Assert.Equal("3:21", t.Duration);
        Assert.Equal("http://big", t.Thumb);
        Assert.Equal("NEXT", next);
    }

    [Fact]
    public void Kuenstler_Kennung_einer_Titel_Zeile_wird_gelesen()
    {
        const string json = """
        { "musicPlaylistShelfRenderer": { "contents": [ { "musicResponsiveListItemRenderer": {
            "playlistItemData": { "videoId": "AAA" },
            "flexColumns": [
              { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Titel" } ] } } },
              { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [
                  { "text": "Die Band", "navigationEndpoint": { "browseEndpoint": { "browseId": "UCband" } } } ] } } } ] } } ] } }
        """;
        var t = Assert.Single(YtLibrary.ParseTracks(json).Items);
        Assert.Equal("UCband", t.ArtistId);
    }

    [Fact]
    public void Kaputte_Antwort_gibt_leere_Liste()
    {
        var (items, next) = YtLibrary.ParseTracks("kein json");
        Assert.Empty(items);
        Assert.Null(next);
    }
}
