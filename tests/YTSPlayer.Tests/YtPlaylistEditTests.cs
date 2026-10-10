using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class YtPlaylistEditTests
{
    [Fact]
    public void Eintrags_Id_wird_in_verschachtelter_Antwort_gefunden()
    {
        const string json = """
        { "contents": { "items": [
          { "x": { "playlistItemData": { "videoId": "AAA", "playlistSetVideoId": "S1" } } },
          { "x": [ { "playlistItemData": { "videoId": "BBB", "playlistSetVideoId": "S2" } } ] }
        ] } }
        """;
        Assert.Equal("S1", YtLibrary.FindSetId(json, "AAA"));
        Assert.Equal("S2", YtLibrary.FindSetId(json, "BBB"));
        Assert.Null(YtLibrary.FindSetId(json, "CCC"));
    }

    [Fact]
    public void Kaputtes_JSON_gibt_null()
    {
        Assert.Null(YtLibrary.FindSetId("nicht json", "AAA"));
    }

    [Fact]
    public void Gefaellt_mir_und_Episoden_Listen_lassen_sich_nicht_loeschen()
    {
        Assert.False(YtLibrary.CanDelete("LM"));
        Assert.False(YtLibrary.CanDelete("SE"));
        Assert.True(YtLibrary.CanDelete("PLabc"));
    }

    [Fact]
    public void Links_fuer_Teilen_und_Mix()
    {
        Assert.Equal("https://music.youtube.com/watch?list=RDAMPLPLabc", UrlNormalizer.PlaylistRadio("PLabc"));
        Assert.Equal("https://music.youtube.com/watch?v=ID1", UrlNormalizer.ShareTrack("ID1"));
        Assert.Equal("https://www.youtube.com/playlist?list=PLabc", UrlNormalizer.YoutubePlaylist("PLabc"));
        Assert.Null(UrlNormalizer.PlaylistId("https://music.youtube.com/watch?v=x&list=RDAMVMx"));
        Assert.Equal("PLabc", UrlNormalizer.PlaylistId("https://music.youtube.com/watch?v=x&list=PLabc"));
    }
}
