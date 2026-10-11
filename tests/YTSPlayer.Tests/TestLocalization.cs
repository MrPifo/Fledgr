using System.Runtime.CompilerServices;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Tests;

/// <summary>Meldet beim Start der Tests alle Übersetzungen an (wie die App), damit Texte aus <see cref="Loc"/> nicht als Schlüssel zurückkommen.</summary>
static class TestLocalization
{
    [ModuleInitializer]
    public static void Init()
    {
        foreach (var assembly in new[]
        {
            typeof(YTSPlayer.Ui.AppThemes).Assembly,
            typeof(YTSPlayer.Modules.MusicPlayer.MusicPlayerPlugin).Assembly,
            typeof(YTSPlayer.Modules.RadioPlayer.RadioPlayerPlugin).Assembly,
            typeof(YTSPlayer.Modules.YtConnect.YtConnectPlugin).Assembly,
        })
            Loc.Current.AddAssembly(assembly);
    }
}
