using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>RAM-Hebel der Web-Ansicht. „Anwenden“ startet die Web-Ansicht mit den neuen Werten neu.</summary>
public sealed partial class YtSettingsViewModel(YtSettings settings, YtPlaybackSource source) : ObservableObject
{
    [ObservableProperty] string _info = "";

    public YtSettings S => settings;

    [RelayCommand]
    async Task Apply()
    {
        Info = "Wende an und starte die Web-Ansicht neu …";
        try
        {
            await source.RestartAsync();
            Info = "Angewendet.";
        }
        catch (Exception ex)
        {
            Info = "Fehler: " + ex.Message;
        }
    }

    [RelayCommand]
    async Task TrimNow()
    {
        var n = await Task.Run(source.TrimAll);
        Info = $"Arbeitssatz geleert bei {n} Prozessen.";
    }
}
