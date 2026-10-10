using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class YtArtistParseTests
{
    const string Page = """
    { "header": { "musicImmersiveHeaderRenderer": {
        "title": { "runs": [ { "text": "Die Band" } ] },
        "description": { "runs": [ { "text": "Eine bekannte Band." } ] },
        "monthlyListenerCount": { "runs": [ { "text": "158 Mio. Zuschauer/Monat" } ] },
        "thumbnail": { "musicThumbnailRenderer": { "thumbnail": { "thumbnails": [ { "url": "http://t/small" }, { "url": "http://t/big" } ] } } },
        "playButton": { "buttonRenderer": { "navigationEndpoint": { "watchEndpoint": { "videoId": "V0", "playlistId": "RDAO1" } } } },
        "startRadioButton": { "buttonRenderer": { "navigationEndpoint": { "watchEndpoint": { "playlistId": "RDEM2" } } } },
        "subscriptionButton": { "subscribeButtonRenderer": { "subscribed": false, "subscriberCountText": { "runs": [ { "text": "12 Mio." } ] } } }
      } },
      "contents": { "singleColumnBrowseResultsRenderer": { "tabs": [ { "tabRenderer": { "content": { "sectionListRenderer": { "contents": [
        { "musicShelfRenderer": {
            "title": { "runs": [ { "text": "Top-Songs" } ] },
            "bottomEndpoint": { "browseEndpoint": { "browseId": "VLOLAK5uy_all" } },
            "contents": [ { "musicResponsiveListItemRenderer": {
                "playlistItemData": { "videoId": "S1" },
                "flexColumns": [
                  { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Hit Eins" } ] } } },
                  { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Die Band" } ] } } } ] } } ] } },
        { "musicCarouselShelfRenderer": {
            "header": { "musicCarouselShelfBasicHeaderRenderer": { "title": { "runs": [ { "text": "Alben" } ] } } },
            "contents": [
              { "musicTwoRowItemRenderer": { "title": { "runs": [ { "text": "Das Album" } ] }, "subtitle": { "runs": [ { "text": "Album • 2025" } ] },
                  "thumbnailRenderer": { "musicThumbnailRenderer": { "thumbnail": { "thumbnails": [ { "url": "http://c/1" } ] } } },
                  "navigationEndpoint": { "browseEndpoint": { "browseId": "MPREb_1" } },
                  "overlay": { "x": { "watchPlaylistEndpoint": { "playlistId": "OLAK5uy_a" } } } } },
              { "musicTwoRowItemRenderer": { "title": { "runs": [ { "text": "Ähnliche Band" } ] }, "subtitle": { "runs": [ { "text": "3 Mio. Abos" } ] },
                  "navigationEndpoint": { "browseEndpoint": { "browseId": "UCxyz" } } } },
              { "musicTwoRowItemRenderer": { "title": { "runs": [ { "text": "Ein Video" } ] }, "subtitle": { "runs": [ { "text": "1 Mio. Aufrufe" } ] },
                  "navigationEndpoint": { "watchEndpoint": { "videoId": "VID9" } } } }
            ] } }
      ] } } } } ] } } }
    """;

    [Fact]
    public void Kopf_Top_Songs_und_Bereiche_werden_gelesen()
    {
        var p = YtArtistParser.Parse(Page, "UCband");
        Assert.NotNull(p);
        Assert.Equal("Die Band", p!.Name);
        Assert.Equal("158 Mio. Zuschauer/Monat", p.Listeners);
        Assert.Equal("Eine bekannte Band.", p.Description);
        Assert.Equal("12 Mio.", p.Subscribers);
        Assert.False(p.Subscribed);
        Assert.Equal("RDAO1", p.PlayPlaylist);
        Assert.Equal("RDEM2", p.RadioPlaylist);
        Assert.Equal("http://t/big", p.Thumb);

        Assert.Equal("Top-Songs", p.TopSongsTitle);
        Assert.Equal("OLAK5uy_all", p.TopSongsMore);
        var song = Assert.Single(p.TopSongs);
        Assert.Equal(("S1", "Hit Eins"), (song.Id, song.Title));

        var section = Assert.Single(p.Sections);
        Assert.Equal("Alben", section.Title);
        Assert.Equal(3, section.Items.Count);
        Assert.Equal(("album", "OLAK5uy_a"), (section.Items[0].Kind, section.Items[0].Ref));
        Assert.Equal(("artist", "UCxyz"), (section.Items[1].Kind, section.Items[1].Id));
        Assert.Equal(("", "VID9"), (section.Items[2].Kind, section.Items[2].Id));
    }

    [Fact]
    public void Ohne_Kopf_oder_bei_kaputtem_JSON_gibt_es_null()
    {
        Assert.Null(YtArtistParser.Parse("{}", "UC1"));
        Assert.Null(YtArtistParser.Parse("kein json", "UC1"));
    }
}
