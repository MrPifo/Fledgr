using Fledgr.Audio;

namespace YTSPlayer.Tests;

public class IcyStreamTests
{
    [Theory]
    [InlineData("StreamTitle='Künstler - Titel';StreamUrl='';", "Künstler - Titel")]
    [InlineData("StreamTitle='Guns N' Roses - Don't Cry';", "Guns N' Roses - Don't Cry")]
    [InlineData("StreamTitle='Nur Titel'", "Nur Titel")]
    [InlineData("StreamTitle='';", null)]
    [InlineData("StreamUrl='http://x';", null)]
    public void ParseTitle(string meta, string? expected) => Assert.Equal(expected, IcyStream.ParseTitle(meta));
}
