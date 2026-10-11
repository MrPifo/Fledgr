using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.MusicPlayer.Library;
using YTSPlayer.Modules.MusicPlayer.Playback;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.MusicPlayer.ViewModels;

/// <summary>Einstellungen des Musikplayers (Eintrag „Musik“ in den Einstellungen).</summary>
public sealed partial class MusicSettingsViewModel(LibraryService library, LibraryViewModel view, AppSettings app, ISettingsStore store) : ObservableObject
{
    [ObservableProperty] string _info = "";

    /// <summary>Titel, Künstler und Jahr kurz oben einblenden, wenn ein neuer Titel startet.</summary>
    public bool ShowNowPlaying
    {
        get => app.NowPlayingShown(LibVlcSource.SourceKey);
        set
        {
            app.SetNowPlayingShown(LibVlcSource.SourceKey, value);
            store.Save("app", app);
            OnPropertyChanged();
        }
    }

    /// <summary>Titel, Interpret und Album in der Suche berücksichtigen (liest die Tags beim Scan).</summary>
    public bool SearchTags
    {
        get => library.Settings.SearchTags;
        set
        {
            if (library.Settings.SearchTags == value) return;
            library.Settings.SearchTags = value;
            library.Save();
            OnPropertyChanged();
            Info = value ? Loc.T("music.settings.tags.on") : Loc.T("music.settings.tags.off");
        }
    }

    [RelayCommand]
    async Task RescanNow()
    {
        Info = Loc.T("music.settings.scanning");
        await view.RescanNowAsync();
    }
}
