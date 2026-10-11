using Avalonia.Controls.Templates;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.YtConnect.ViewModels;
using YTSPlayer.Modules.YtConnect.Views;
using YTSPlayer.Ui;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Plugin „YouTube Music“: Google-Konto verbinden und YT Music streamen.
/// Die Web-Ansicht kommt von der Plattform (<paramref name="hostFactory"/>) und startet erst bei Bedarf.
/// </summary>
public sealed class YtConnectPlugin(Func<YtSettings, IYtWebHost> hostFactory) : IPlugin, IViewProvider
{
    YtPlaybackSource? _source;
    YtAccountSession? _account;
    YtEngine? _engine;

    /// <summary>Id der Einstellungs-Seite des Moduls (das Profil-Menü öffnet sie).</summary>
    public const string SettingsId = "yt-settings";

    public string Id => "yt";
    public string DisplayName => "yt.plugin.name";
    public bool IsCore => false;
    public bool EnabledByDefault => true;

    public IEnumerable<IDataTemplate> GetTemplates() =>
    [
        new FuncDataTemplate<YtViewModel>((_, _) => new YtView()),
        new FuncDataTemplate<YtHomeViewModel>((_, _) => new YtHomeView()),
        new FuncDataTemplate<YtSearchHeader>((_, _) => new YtSearchBox()),
        new FuncDataTemplate<YtSettingsViewModel>((_, _) => new YtSettingsView()),
    ];

    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        var sp = context.Services;
        var store = sp.Require<ISettingsStore>();
        var perf = sp.Require<IPerfTracker>();
        var stats = sp.Require<IProcessStats>();
        var player = sp.Require<IPlayer>();
        var nav = sp.Require<INavigator>();

        var settings = store.Load<YtSettings>("yt");
        var engine = _engine = new YtEngine(() => hostFactory(settings));
        _source = new YtPlaybackSource(engine, settings, store, stats, perf, sp.Get<YTSPlayer.Contracts.Playback.IChildAudioVolume>());
        _account = new YtAccountSession(engine, store, perf, nav, () => player.ActiveSourceId == YtPlaybackSource.SourceKey ? player.StopAsync() : Task.CompletedTask);
        var library = new YtLibrary(engine, _source, store, perf);
        var vm = new YtViewModel(_account, library, player, engine, sp.Require<IThumbnailCache>(), store);

        var source = _source;
        source.TrackMenuHandler = (anchor, extras) => YtMenus.ShowCurrent(vm, source.State, source.CurrentArtistId, anchor, extras);
        source.AddToPlaylistHandler = anchor => YtMenus.ShowAddToPlaylist(vm, source.State, source.CurrentArtistId, anchor);
        vm.ShowPage = () => nav.NavigateTo("yt");
        source.LyricsHandler = library.GetLyricsAsync;
        source.ArtistHandler = () => vm.OpenArtistOfAsync(source.CurrentArtistId, source.State.Artist);
        context.AddSource(_source);
        context.AddPage(new NavPage("yt", "yt.nav", Icons.Youtube, vm, 20, "yt"));
        var home = new YtHomeViewModel(vm);
        context.AddHomeSection(new HomeSection("yt-home", "yt.nav", "yt", 10, home, "yt",
            [new HomeAction("yt.home.mix", Icons.PlayOutline, home.StartMix), new HomeAction("yt.home.shuffle", Icons.Shuffle, home.StartShuffle)]));
        context.AddSettingsPage(new SettingsPage(SettingsId, "yt.settings.title", new YtSettingsViewModel(settings, _source, sp.Require<AppSettings>(), store, _account) { ClearSearchHistoryHandler = vm.ClearSearchHistory }));
        context.AddDiagnostics(new YtDiagnostics(engine, _source));
        context.SetAccount(_account);
        _ = RefreshAvatarLaterAsync(_account, player);
        return Task.CompletedTask;
    }

    /// <summary>Kurz nach dem Start: fehlendes Profilbild nachladen (nur wenn angemeldet und kein Bild gespeichert ist).</summary>
    async Task RefreshAvatarLaterAsync(YtAccountSession account, IPlayer player)
    {
        await Task.Delay(4000);
        if (_account != account) return;
        await account.RefreshMissingAvatarAsync(() => player.ActiveSourceId == YtPlaybackSource.SourceKey);
    }

    public async Task DeactivateAsync()
    {
        _account?.Dispose();
        _account = null;
        if (_source != null) await _source.DisposeAsync();
        _source = null;
        if (_engine != null) await _engine.DisposeHostAsync();
        _engine = null;
    }
}
