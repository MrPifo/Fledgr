using YTSPlayer.Modules.RadioPlayer;
using YTSPlayer.Modules.RadioPlayer.Data;

namespace YTSPlayer.Tests;

public class RadioCountriesTests
{
    [Fact]
    public void Sortiert_nach_Name_und_legt_Codes_zusammen()
    {
        var list = RadioCountries.Prepare(
        [
            new RadioCountry("Germany", "DE", 6220),
            new RadioCountry("Austria", "AT", 351),
            new RadioCountry("Greece", "gr", 1),
            new RadioCountry("Greece", "GR", 2150),
            new RadioCountry("Türkiye", "TR", 799),
            new RadioCountry("", "XX", 5),
        ]);

        Assert.Equal(new[] { "Austria", "Germany", "Greece", "Türkiye" }, list.Select(c => c.Name));
        Assert.Equal(2150, list.Single(c => c.Name == "Greece").Count);
    }

    [Theory]
    [InlineData("ger", "Germany", true)]
    [InlineData("TURK", "Türkiye", true)]
    [InlineData("tuerk", "Türkiye", false)]
    [InlineData("", "Austria", true)]
    [InlineData("ger", "Austria", false)]
    public void Suche_ignoriert_Gross_Klein_und_Akzente(string filter, string name, bool expected) =>
        Assert.Equal(expected, RadioCountries.Matches(new RadioCountry(name, "XX", 1), filter));
}
