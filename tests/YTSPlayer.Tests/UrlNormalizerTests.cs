using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://youtu.be/abc123", "https://music.youtube.com/watch?v=abc123")]
    [InlineData("https://www.youtube.com/watch?v=abc123&list=PL1", "https://music.youtube.com/watch?v=abc123&list=PL1")]
    [InlineData("https://music.youtube.com/playlist?list=PL9", "https://music.youtube.com/playlist?list=PL9")]
    public void Wandelt_Links_um(string input, string expected) =>
        Assert.Equal(expected, UrlNormalizer.ToMusicUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("kein link")]
    [InlineData("https://example.com/watch?v=abc")]
    [InlineData("ftp://youtu.be/abc")]
    public void Lehnt_fremde_Links_ab(string input) => Assert.Null(UrlNormalizer.ToMusicUrl(input));

    [Fact]
    public void Playlist_URL_startet_ab_dem_ersten_Titel() =>
        Assert.Equal("https://music.youtube.com/watch?list=PL%2B1", UrlNormalizer.WatchPlaylist("PL+1"));
}
