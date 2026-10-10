using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class LyricsTests
{
    [Fact]
    public void Kennung_des_Songtext_Reiters_wird_gefunden()
    {
        const string next = """
        { "tabs": [
          { "tabRenderer": { "title": "Weiter", "endpoint": { "browseEndpoint": { "browseId": "MPTRt_abc" } } } },
          { "tabRenderer": { "title": "Songtext", "endpoint": { "browseEndpoint": { "browseId": "MPLYt_xyz" } } } }
        ] }
        """;
        Assert.Equal("MPLYt_xyz", YtLyrics.FindBrowseId(next));
        Assert.Null(YtLyrics.FindBrowseId("{ \"tabs\": [] }"));
        Assert.Null(YtLyrics.FindBrowseId("kein json"));
    }

    [Fact]
    public void Text_und_Quelle_werden_gelesen()
    {
        const string browse = """
        { "contents": { "x": { "musicDescriptionShelfRenderer": {
            "description": { "runs": [ { "text": "Zeile eins\nZeile zwei" } ] },
            "footer": { "runs": [ { "text": "Quelle: Musixmatch" } ] } } } } }
        """;
        var r = YtLyrics.Parse(browse);
        Assert.NotNull(r);
        Assert.Equal("Zeile eins\nZeile zwei", r!.Text);
        Assert.Equal("Quelle: Musixmatch", r.Source);
        Assert.Null(YtLyrics.Parse("{}"));
    }
}
