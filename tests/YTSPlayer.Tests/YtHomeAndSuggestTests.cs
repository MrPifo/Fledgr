using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class YtHomeAndSuggestTests
{
    [Fact]
    public void Vorschlaege_werden_gelesen_und_begrenzt()
    {
        var list = YtSuggest.Parse("[\"foo\",[\"foo fighters\",\"foo bar\",\"foo test\"]]", 2);
        Assert.Equal(["foo fighters", "foo bar"], list);
        Assert.Empty(YtSuggest.Parse("kein json", 5));
        Assert.Empty(YtSuggest.Parse("{}", 5));
    }

    const string Home = """
    { "contents": { "x": { "sectionListRenderer": {
        "contents": [
          { "musicCarouselShelfRenderer": {
              "header": { "musicCarouselShelfBasicHeaderRenderer": { "title": { "runs": [ { "text": "Schnelle Auswahl" } ] } } },
              "contents": [ { "musicResponsiveListItemRenderer": {
                  "playlistItemData": { "videoId": "Q1" },
                  "flexColumns": [
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Ein Lied" } ] } } },
                    { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Eine Band" } ] } } } ] } } ] } },
          { "musicCarouselShelfRenderer": {
              "header": { "musicCarouselShelfBasicHeaderRenderer": { "title": { "runs": [ { "text": "Mixes für dich" } ] } } },
              "contents": [ { "musicTwoRowItemRenderer": { "title": { "runs": [ { "text": "Mein Mix" } ] }, "subtitle": { "runs": [ { "text": "Playlist" } ] },
                  "navigationEndpoint": { "browseEndpoint": { "browseId": "VLRDCLAK1" } } } } ] } },
          { "musicCarouselShelfRenderer": { "header": { "musicCarouselShelfBasicHeaderRenderer": { "title": { "runs": [ { "text": "Leer" } ] } } }, "contents": [] } }
        ],
        "continuations": [ { "nextContinuationData": { "continuation": "MORE_HOME" } } ]
    } } } }
    """;

    [Fact]
    public void Startseite_Bereiche_und_Fortsetzung_werden_gelesen()
    {
        var (sections, next) = YtHomeParser.Parse(Home);
        Assert.Equal(2, sections.Count);
        Assert.Equal("Schnelle Auswahl", sections[0].Title);
        Assert.Equal(("Q1", ""), (sections[0].Items[0].Id, sections[0].Items[0].Kind));
        Assert.Equal("Mixes für dich", sections[1].Title);
        Assert.Equal(("playlist", "RDCLAK1"), (sections[1].Items[0].Kind, sections[1].Items[0].Ref));
        Assert.Equal("MORE_HOME", next);
    }

    [Fact]
    public void Kaputte_Startseite_gibt_nichts()
    {
        var (sections, next) = YtHomeParser.Parse("kein json");
        Assert.Empty(sections);
        Assert.Null(next);
    }
}
