using YTSPlayer.Modules.RadioPlayer;
using YTSPlayer.Modules.RadioPlayer.Data;
using YTSPlayer.Modules.RadioPlayer.Playback;

namespace YTSPlayer.Tests;

public class RadioParseTests
{
    // Erfundene Beispieldaten im Aufbau der Sender-Datenbank. Keine echten Sender.
    const string Stations = """
        [
          {"stationuuid":"aaa-1","name":"  Testradio Eins  ","url":"http://alt.example/a","url_resolved":"https://neu.example/a.mp3",
           "favicon":"https://example.org/logo.png","country":"Germany","tags":"pop,news","codec":"MP3","bitrate":128},
          {"stationuuid":"bbb-2","name":"Ohne Adresse","url":"","url_resolved":"","country":"Austria"},
          {"stationuuid":"ccc-3","name":"Nur Original","url":"https://x.example/s","url_resolved":"","favicon":"ftp://nein","codec":"AAC","bitrate":"64"},
          {"stationuuid":"","name":"Ohne Id","url":"https://y.example/s"}
        ]
        """;

    [Fact]
    public void Liest_Sender_und_nimmt_die_aufgeloeste_Adresse()
    {
        var first = RadioBrowserParser.ParseStations(Stations)[0];
        Assert.Equal("aaa-1", first.Id);
        Assert.Equal("Testradio Eins", first.Name);
        Assert.Equal("https://neu.example/a.mp3", first.StreamUrl);
        Assert.Equal("https://example.org/logo.png", first.LogoUrl);
        Assert.Equal("MP3 · 128 kBit/s", first.TechnicalInfo);
    }

    [Fact]
    public void Ueberspringt_Sender_ohne_Id_oder_Adresse()
    {
        var list = RadioBrowserParser.ParseStations(Stations);
        Assert.Equal(new[] { "aaa-1", "ccc-3" }, list.Select(s => s.Id));
    }

    [Fact]
    public void Faellt_auf_die_Originaladresse_zurueck_und_liest_Zahlen_aus_Text()
    {
        var second = RadioBrowserParser.ParseStations(Stations)[1];
        Assert.Equal("https://x.example/s", second.StreamUrl);
        Assert.Null(second.LogoUrl);
        Assert.Equal(64, second.Bitrate);
    }

    [Fact]
    public void Liest_Laender_ohne_leere_Namen()
    {
        const string json = """[{"name":"Germany","iso_3166_1":"DE","stationcount":6220},{"name":"","iso_3166_1":"XX","stationcount":1}]""";
        var country = Assert.Single(RadioBrowserParser.ParseCountries(json));
        Assert.Equal(new RadioCountry("Germany", "DE", 6220), country);
    }

    [Fact]
    public void Liest_Server_ohne_Dubletten()
    {
        const string json = """[{"ip":"1.2.3.4","name":"de1.api.radio-browser.info"},{"ip":"2001::1","name":"de1.api.radio-browser.info"},{"ip":"5.6.7.8","name":"nl1.api.radio-browser.info"}]""";
        Assert.Equal(new[] { "de1.api.radio-browser.info", "nl1.api.radio-browser.info" }, RadioBrowserParser.ParseServers(json));
    }

    [Theory]
    [InlineData("https://a.example/x", true)]
    [InlineData("ftp://a.example/x", false)]
    [InlineData("kein link", false)]
    [InlineData(null, false)]
    public void Prueft_http_Adressen(string? url, bool expected) => Assert.Equal(expected, RadioUrls.IsHttp(url));

    [Theory]
    [InlineData("https://radio.example/live.m3u", true)]
    [InlineData("https://radio.example/live.PLS?x=1", true)]
    [InlineData("https://radio.example/live.m3u8", false)]
    [InlineData("https://radio.example/mp3", false)]
    public void Erkennt_Playlisten(string url, bool expected) => Assert.Equal(expected, RadioUrls.IsPlaylist(url));

    [Fact]
    public void Liest_erste_Adresse_aus_M3U()
    {
        const string m3u = "#EXTM3U\r\n#EXTINF:-1,Test\r\nhttps://a.example/stream.mp3\r\nhttps://b.example/x\r\n";
        Assert.Equal("https://a.example/stream.mp3", PlaylistResolver.FirstStreamUrl(m3u));
    }

    [Fact]
    public void Liest_erste_Adresse_aus_PLS()
    {
        const string pls = "[playlist]\nNumberOfEntries=1\nFile1=http://b.example/live\nTitle1=Live\nLength1=-1\n";
        Assert.Equal("http://b.example/live", PlaylistResolver.FirstStreamUrl(pls));
    }

    [Fact]
    public void Gibt_null_wenn_keine_Adresse_in_der_Liste_steht() =>
        Assert.Null(PlaylistResolver.FirstStreamUrl("<html><body>Fehler</body></html>"));
}
