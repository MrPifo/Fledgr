using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Modules.MusicPlayer.Library;

namespace YTSPlayer.Modules.MusicPlayer.ViewModels;

/// <summary>Einstellungen des Musikplayers (Eintrag „Musik“ in den Einstellungen).</summary>
public sealed partial class MusicSettingsViewModel(LibraryService library, LibraryViewModel view) : ObservableObject
{
    [ObservableProperty] string _info = "";

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
            Info = value ? "Beim nächsten Scan werden die Tags gelesen. „Jetzt neu scannen“ startet ihn gleich." : "Beim nächsten Scan werden keine Tags mehr gelesen.";
        }
    }

    [RelayCommand]
    async Task RescanNow()
    {
        Info = "Scan läuft. Der Fortschritt steht auf der Seite „Musik“.";
        await view.RescanNowAsync();
    }
}
