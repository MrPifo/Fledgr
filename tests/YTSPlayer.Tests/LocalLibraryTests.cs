using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.MusicPlayer;
using YTSPlayer.Modules.MusicPlayer.Library;

namespace YTSPlayer.Tests;

public class LocalLibraryTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fledgr-test-" + Guid.NewGuid().ToString("N")[..8]);
    readonly string _music;
    readonly LibraryService _lib;
    readonly LibraryProvider _provider;

    public LocalLibraryTests()
    {
        _music = Path.Combine(_root, "musik");
        Directory.CreateDirectory(_music);
        var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "cache"), Path.Combine(_root, "export"));
        Directory.CreateDirectory(paths.Data);
        var settings = new MusicSettings();
        _lib = new LibraryService(settings, new JsonSettingsStore(paths), new PerfTracker());
        _provider = new LibraryProvider(_lib);
    }

    public void Dispose()
    {
        _lib.StopWatching();
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>Leere Dateien mit Audio-Endung: TagLib kann sie nicht lesen, der Index nimmt sie trotzdem auf (ohne Tags).</summary>
    void Touch(string name) => File.WriteAllBytes(Path.Combine(_music, name), []);

    [Fact]
    public async Task Dateien_ohne_Tags_landen_unter_Unbekannter_Interpret_und_Unbekanntes_Album()
    {
        Touch("a.mp3");
        Touch("b.mp3");
        _lib.AddFolder(_music);
        await _lib.ScanAsync(null, CancellationToken.None);

        var artists = await _provider.GetChildrenAsync(LibraryProvider.ArtistsNode);
        var artist = Assert.Single(artists);
        Assert.Equal("Unbekannter Interpret", artist.Title);
        var albums = await _provider.GetChildrenAsync(artist);
        var album = Assert.Single(albums);
        Assert.Equal("Unbekanntes Album", album.Title);
        Assert.NotNull(album.Item);
        var tracks = await _provider.GetChildrenAsync(album);
        Assert.Equal(2, tracks.Count);
        Assert.All(tracks, t => Assert.Equal(MediaNodeKind.Track, t.Kind));
    }

    [Fact]
    public async Task Neue_und_geloeschte_Dateien_kommen_ohne_Neu_Scan_in_den_Index()
    {
        Touch("a.mp3");
        _lib.AddFolder(_music);
        await _lib.ScanAsync(null, CancellationToken.None);
        Assert.Equal(1, _lib.FileCount);

        var changed = new TaskCompletionSource();
        _lib.FilesChanged += () => changed.TrySetResult();
        Touch("neu.mp3");
        Assert.True(await Task.WhenAny(changed.Task, Task.Delay(8000)) == changed.Task, "Keine Meldung über neue Datei");
        Assert.Equal(2, _lib.FileCount);

        var deleted = new TaskCompletionSource();
        _lib.FilesChanged += () => deleted.TrySetResult();
        File.Delete(Path.Combine(_music, "a.mp3"));
        Assert.True(await Task.WhenAny(deleted.Task, Task.Delay(8000)) == deleted.Task, "Keine Meldung über gelöschte Datei");
        Assert.Equal(1, _lib.FileCount);
    }
}
