using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.RadioPlayer;
using YTSPlayer.Modules.RadioPlayer.Data;

namespace YTSPlayer.Tests;

/// <summary>Speicher im Arbeitsspeicher statt in einer Datei.</summary>
sealed class MemorySettings : ISettingsStore
{
    readonly Dictionary<string, object> _values = [];

    public T Load<T>(string section) where T : class, new() => _values.TryGetValue(section, out var v) ? (T)v : new T();

    public void Save<T>(string section, T value) where T : class => _values[section] = value;
}

public class RadioStoreTests
{
    static RadioStation Sender(int n) => new($"id-{n}", $"Sender {n}", $"https://s{n}.example/stream");

    [Fact]
    public void Favorit_setzen_und_ohne_Verlauf_wieder_entfernen()
    {
        var store = new RadioStore(new MemorySettings());
        store.SetFavorite(Sender(1), true);
        Assert.Single(store.Favorites);

        store.SetFavorite(Sender(1), false);
        Assert.Empty(store.Favorites);
        Assert.Null(store.Find(Sender(1).StreamUrl));
    }

    [Fact]
    public void Verlauf_ist_auf_20_begrenzt_und_Favoriten_bleiben()
    {
        var store = new RadioStore(new MemorySettings());
        store.SetFavorite(Sender(0), true);
        for (var i = 1; i <= 25; i++) store.MarkPlayed(Sender(i));

        Assert.Equal(RadioStore.MaxRecent, store.Recent.Count);
        Assert.Single(store.Favorites);
    }

    [Fact]
    public void Favorit_der_gespielt_wurde_steht_nur_bei_den_Favoriten()
    {
        var store = new RadioStore(new MemorySettings());
        store.SetFavorite(Sender(1), true);
        store.MarkPlayed(Sender(1));

        Assert.Single(store.Favorites);
        Assert.Empty(store.Recent);
    }

    [Fact]
    public void Status_und_neue_Adresse_bleiben_bei_dem_Sender()
    {
        var store = new RadioStore(new MemorySettings());
        store.MarkPlayed(Sender(1));
        store.SetStatus("https://s1.example/stream", StationStatus.Dead);
        Assert.Equal(StationStatus.Dead, store.Find("https://s1.example/stream")!.Status);

        store.ReplaceUrl("https://s1.example/stream", "https://neu.example/s1");
        var moved = store.Find("https://neu.example/s1");
        Assert.NotNull(moved);
        Assert.Equal("id-1", moved.Id);
        Assert.Equal(StationStatus.Ok, moved.Status);
        Assert.Null(store.Find("https://s1.example/stream"));
    }

    [Fact]
    public void Gemerktes_bleibt_nach_einem_Neustart()
    {
        var settings = new MemorySettings();
        new RadioStore(settings).SetFavorite(Sender(7), true);
        Assert.Single(new RadioStore(settings).Favorites);
    }

    [Fact]
    public void Laenderliste_wird_mit_Zeitpunkt_gemerkt()
    {
        var store = new RadioStore(new MemorySettings());
        Assert.Null(store.CountriesUpdated);

        store.SetCountries([new RadioCountry("Germany", "DE", 6220)]);
        Assert.NotNull(store.CountriesUpdated);
        Assert.Single(store.Countries);
    }
}
