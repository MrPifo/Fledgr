using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.MusicPlayer;
using YTSPlayer.Modules.MusicPlayer.Library;

namespace YTSPlayer.Tests;

public sealed class LibraryServiceTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ytsp-lib-" + Guid.NewGuid().ToString("N")[..8]);
    readonly LibraryService _lib;

    public LibraryServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Sounds", "Impacts"));
        Directory.CreateDirectory(Path.Combine(_dir, "Musik"));
        foreach (var f in new[] { "Sounds/Impacts/hit_big.wav", "Sounds/Impacts/hit_small.mp3", "Musik/Lied Eins.flac", "Musik/notiz.txt" })
            File.WriteAllText(Path.Combine(_dir, f), "x");
        var paths = new AppPaths(_dir + "-data", _dir + "-data", _dir + "-data");
        _lib = new LibraryService(new MusicSettings { Folders = [_dir] }, new JsonSettingsStore(paths, readOnly: true), new PerfTracker());
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task Scan_findet_nur_Audio_Dateien()
    {
        await _lib.ScanAsync(null, CancellationToken.None);
        Assert.Equal(3, _lib.FileCount);
    }

    [Theory]
    [InlineData("impacts hit", 2)]
    [InlineData("HIT BIG", 1)]
    [InlineData("sounds\\impacts", 2)]
    [InlineData("lied eins", 1)]
    [InlineData("gibtesnicht", 0)]
    [InlineData("   ", 0)]
    public async Task Suche_braucht_alle_Woerter(string query, int expected)
    {
        await _lib.ScanAsync(null, CancellationToken.None);
        Assert.Equal(expected, (await _lib.SearchAsync(query, 100, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Suche_begrenzt_die_Trefferzahl()
    {
        await _lib.ScanAsync(null, CancellationToken.None);
        Assert.Single(await _lib.SearchAsync("hit", 1, CancellationToken.None));
    }

    [Fact]
    public async Task Ordneranzeige_listet_Unterordner_und_Dateien()
    {
        var (dirs, files) = await _lib.BrowseAsync(Path.Combine(_dir, "Musik"), CancellationToken.None);
        Assert.Empty(dirs);
        Assert.Single(files);
        var root = await _lib.BrowseAsync(_dir, CancellationToken.None);
        Assert.Equal(2, root.Folders.Count);
    }

    [Fact]
    public void Favoriten_und_Playlists()
    {
        var p = _lib.CreatePlaylist("Test");
        _lib.AddToPlaylist(p, "a.mp3");
        _lib.AddToPlaylist(p, "a.mp3");
        Assert.Single(p.Paths);
        _lib.RemoveFromPlaylist(p, "a.mp3");
        Assert.Empty(p.Paths);
        _lib.ToggleFavorite("x.mp3");
        Assert.True(_lib.IsFavorite("x.mp3"));
        _lib.ToggleFavorite("x.mp3");
        Assert.False(_lib.IsFavorite("x.mp3"));
    }

    [Fact]
    public async Task Parallele_Scans_laufen_nicht_doppelt()
    {
        var a = _lib.ScanAsync(null, CancellationToken.None);
        var b = _lib.ScanAsync(null, CancellationToken.None);
        await Task.WhenAll(a, b);
        Assert.Equal(3, _lib.FileCount);
    }
}
