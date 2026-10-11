using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>Einstellungen von YouTube Music: Wiedergabe, RAM-Hebel der Web-Ansicht und Titel-Anzeige.</summary>
public sealed partial class YtSettingsViewModel : ObservableObject
{
    readonly YtSettings settings;
    readonly YtPlaybackSource source;
    readonly AppSettings app;
    readonly ISettingsStore store;
    readonly YtAccountSession _account;

    public YtSettingsViewModel(YtSettings settings, YtPlaybackSource source, AppSettings app, ISettingsStore store, YtAccountSession account)
    {
        this.settings = settings;
        this.source = source;
        this.app = app;
        this.store = store;
        _account = account;
        account.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsLoggedIn));
            OnPropertyChanged(nameof(CanSignIn));
            OnPropertyChanged(nameof(AccountName));
            OnPropertyChanged(nameof(AccountEmail));
        });
    }

    [ObservableProperty] string _info = "";

    /// <summary>Ein Konto ist angemeldet: Name, Adresse und „Abmelden“ sind sichtbar.</summary>
    public bool IsLoggedIn => _account.State == YTSPlayer.Contracts.Account.AccountState.LoggedIn;

    /// <summary>Niemand ist angemeldet: „Anmelden“ ist sichtbar.</summary>
    public bool CanSignIn => _account.State == YTSPlayer.Contracts.Account.AccountState.LoggedOut;

    [RelayCommand]
    Task SignIn() => _account.SignInAsync();

    public string AccountName => _account.Profile?.Name ?? "";

    public string AccountEmail => _account.Profile?.Email ?? "";

    [RelayCommand]
    Task SignOut() => _account.SignOutAsync();

    public YtSettings S => settings;

    /// <summary>Das Plugin setzt hier das Löschen des Suchverlaufs ein (der Verlauf liegt im YT-ViewModel).</summary>
    public Action? ClearSearchHistoryHandler { get; set; }

    [RelayCommand]
    void ClearSearchHistory()
    {
        ClearSearchHistoryHandler?.Invoke();
        Info = Loc.T("yt.search.historyCleared");
    }

    /// <summary>Titel, Künstler und Jahr kurz oben einblenden, wenn ein neuer Titel startet.</summary>
    public bool ShowNowPlaying
    {
        get => app.NowPlayingShown(YtPlaybackSource.SourceKey);
        set
        {
            app.SetNowPlayingShown(YtPlaybackSource.SourceKey, value);
            store.Save("app", app);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    async Task Apply()
    {
        Info = Loc.T("yt.settings.applying");
        try
        {
            await source.RestartAsync();
            Info = Loc.T("yt.settings.applied");
        }
        catch (Exception ex)
        {
            Info = Loc.F("yt.settings.error", ex.Message);
        }
    }

    [RelayCommand]
    async Task TrimNow()
    {
        var n = await Task.Run(source.TrimAll);
        Info = Loc.F("yt.settings.trimmed", n);
    }
}
