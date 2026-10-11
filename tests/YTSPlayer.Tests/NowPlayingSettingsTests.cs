using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Tests;

public class NowPlayingSettingsTests
{
    [Fact]
    public void Titel_Anzeige_ist_standardmaessig_an() =>
        Assert.True(new AppSettings().NowPlayingShown("radio"));

    [Fact]
    public void Titel_Anzeige_wird_je_Quelle_getrennt_gemerkt()
    {
        var settings = new AppSettings();
        settings.SetNowPlayingShown("radio", false);

        Assert.False(settings.NowPlayingShown("radio"));
        Assert.True(settings.NowPlayingShown("yt"));
    }
}
