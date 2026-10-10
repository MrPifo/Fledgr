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
    public string DisplayName => "YouTube Music (YT-Connect)";
    public bool IsCore => false;
    public bool EnabledByDefault => true;

    public IEnumerable<IDataTemplate> GetTemplates() =>
    [
        new FuncDataTemplate<YtViewModel>((_, _) => new YtView()),
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
        _source = new YtPlaybackSource(engine, settings, store, stats, perf);
        _account = new YtAccountSession(engine, store, perf, nav, () => player.ActiveSourceId == YtPlaybackSource.SourceKey ? player.StopAsync() : Task.CompletedTask);
        var library = new YtLibrary(engine, _source, store, perf);
        var vm = new YtViewModel(_account, library, player, engine, sp.Require<IThumbnailCache>(), store);

        var source = _source;
        source.TrackMenuHandler = anchor => YtMenus.ShowCurrent(vm, source.State, source.CurrentArtistId, anchor);
        vm.ShowPage = () => nav.NavigateTo("yt");
        source.LyricsHandler = library.GetLyricsAsync;
        source.ArtistHandler = () => vm.OpenArtistOfAsync(source.CurrentArtistId, source.State.Artist);
        context.AddSource(_source);
        context.AddPage(new NavPage("yt", "YouTube Music", Icons.Cloud, vm, 20));
        context.AddSettingsPage(new SettingsPage(SettingsId, "YouTube Music",new YtSettingsViewModel(settings, _source)));
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
