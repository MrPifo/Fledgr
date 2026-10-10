using Material.Icons;
using YTSPlayer.Ui;

namespace YTSPlayer.Tests;

public class IconNamesTests
{
    [Fact]
    public void Alle_Icon_Namen_kennt_die_Bibliothek()
    {
        foreach (var name in Icons.All)
            Assert.True(Enum.TryParse<MaterialIconKind>(name, out _), $"Unbekanntes Icon: {name}");
    }

    [Fact]
    public void Namen_aus_den_Views_sind_gueltig()
    {
        // Feste Icons stehen direkt in den Views. Der Compiler prüft sie, hier nur die, die über Text gebunden werden.
        string[] bound = [Icons.Music, Icons.Cloud, Icons.Settings, Icons.Radio, Icons.Play, Icons.Pause, Icons.Shuffle, Icons.ShuffleOff,
            Icons.Folder, Icons.Playlist, Icons.Track, Icons.Station, Icons.Star, Icons.StarOutline];
        foreach (var name in bound) Assert.True(Enum.TryParse<MaterialIconKind>(name, out _), name);
    }
}
