using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Tests;

public class ContractsTests
{
    [Fact]
    public void PerfTracker_zaehlt_Dauer_und_Fehler()
    {
        var perf = new PerfTracker();
        using (perf.Begin("A")) { }
        using (var s = perf.Begin("A")) s.Fail();
        var e = Assert.Single(perf.Snapshot());
        Assert.Equal(2, e.Count);
        Assert.Equal(1, e.Failures);
        Assert.Equal(0, e.Running);
    }

    [Fact]
    public void PerfTracker_zeigt_laufende_Operationen()
    {
        var perf = new PerfTracker();
        using var s = perf.Begin("Lang");
        Assert.Equal(1, perf.Snapshot().Single().Running);
    }

    [Fact]
    public void ServiceRegistry_wirft_bei_fehlendem_Dienst()
    {
        var sr = new ServiceRegistry().Add<IAppPaths>(new AppPaths("a", "b", "c"));
        Assert.Equal("a", sr.Require<IAppPaths>().Data);
        Assert.Throws<InvalidOperationException>(() => sr.Require<ISettingsStore>());
        Assert.Null(((IServiceProvider)sr).Get<ISettingsStore>());
    }

    [Fact]
    public void SettingsStore_speichert_und_laedt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ytsp-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new JsonSettingsStore(new AppPaths(dir, dir, dir));
            store.Save("app", new AppSettings { Volume = 0.4, Plugins = { ["yt"] = false } });
            var back = store.Load<AppSettings>("app");
            Assert.Equal(0.4, back.Volume);
            Assert.False(back.Plugins["yt"]);
            Assert.Equal(300, store.Load<AppSettings>("fehlt").TargetMb);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void SettingsStore_im_Nur_Lesen_Modus_schreibt_nichts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ytsp-test-" + Guid.NewGuid().ToString("N")[..8]);
        new JsonSettingsStore(new AppPaths(dir, dir, dir), readOnly: true).Save("app", new AppSettings());
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Capabilities_sind_Flags()
    {
        var c = PlaybackCapabilities.Seek | PlaybackCapabilities.Volume;
        Assert.True(c.HasFlag(PlaybackCapabilities.Seek));
        Assert.False(c.HasFlag(PlaybackCapabilities.Next));
    }
}
