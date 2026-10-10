using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.YtConnect;

namespace YTSPlayer.Tests;

public class YtLibraryTests
{
    static PlaylistInfo P(string id, string title = "T", string sub = "1 Titel") => new(id, title, sub);

    [Fact]
    public void Gleiche_Listen_sind_gleich() =>
        Assert.True(YtLibrary.SameList([P("a"), P("b")], [P("a"), P("b")]));

    [Fact]
    public void Andere_Anzahl_in_der_Info_ist_eine_Aenderung() =>
        Assert.False(YtLibrary.SameList([P("a", sub: "1 Titel")], [P("a", sub: "2 Titel")]));

    [Fact]
    public void Anderer_Titel_ist_eine_Aenderung() =>
        Assert.False(YtLibrary.SameList([P("a", "Alt")], [P("a", "Neu")]));

    [Fact]
    public void Andere_Reihenfolge_ist_eine_Aenderung() =>
        Assert.False(YtLibrary.SameList([P("a"), P("b")], [P("b"), P("a")]));

    [Fact]
    public void Neue_Playlist_ist_eine_Aenderung() =>
        Assert.False(YtLibrary.SameList([P("a")], [P("a"), P("b")]));

    [Fact]
    public void Titel_Vergleich_ignoriert_Bild_und_Dauer()
    {
        var a = new TrackInfo("v1", "Lied", "Band", "3:00", "http://x/1.jpg");
        var b = new TrackInfo("v1", "Lied", "Band", "3:01", "http://x/2.jpg");
        Assert.True(YtLibrary.SameTracks([a], [b]));
        Assert.False(YtLibrary.SameTracks([a], [a with { Title = "Anders" }]));
    }

    [Fact]
    public void Titel_URL_enthaelt_Video_und_Playlist() =>
        Assert.Equal("https://music.youtube.com/watch?v=vid&list=PL1", UrlNormalizer.WatchTrack("vid", "PL1"));

    [Fact]
    public async Task Vorschaubild_Cache_ignoriert_ungueltige_Adressen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ytsp-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ThumbnailCache(new AppPaths(dir, dir, dir), new PerfTracker());
            Assert.Null(await cache.GetAsync(null));
            Assert.Null(await cache.GetAsync("kein link"));
            Assert.Null(await cache.GetAsync("file:///C:/Windows/win.ini"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
