using CommunityToolkit.Mvvm.ComponentModel;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.RadioPlayer.Playback;

namespace YTSPlayer.Modules.RadioPlayer.ViewModels;

/// <summary>Einstellungen des Radios (Eintrag „Radio“ in den Einstellungen).</summary>
public sealed partial class RadioSettingsViewModel(AppSettings app, ISettingsStore store) : ObservableObject
{
    /// <summary>Titel und Künstler kurz oben einblenden, wenn ein neuer Titel startet. Der Stream kennt kein Jahr.</summary>
    public bool ShowNowPlaying
    {
        get => app.NowPlayingShown(RadioStreamSource.SourceKey);
        set
        {
            app.SetNowPlayingShown(RadioStreamSource.SourceKey, value);
            store.Save("app", app);
            OnPropertyChanged();
        }
    }
}
