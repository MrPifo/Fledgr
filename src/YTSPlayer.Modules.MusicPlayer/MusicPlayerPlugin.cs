using Avalonia.Controls.Templates;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.MusicPlayer.Library;
using YTSPlayer.Modules.MusicPlayer.Playback;
using YTSPlayer.Modules.MusicPlayer.ViewModels;
using YTSPlayer.Modules.MusicPlayer.Views;
using YTSPlayer.Ui;

namespace YTSPlayer.Modules.MusicPlayer;

/// <summary>Kern-Plugin: lokale Bibliothek mit Ordnern, Playlists, Favoriten und Suche. Immer aktiv.</summary>
public sealed class MusicPlayerPlugin(Func<string?>? libVlcDirectory = null) : IPlugin, IViewProvider
{
    LibVlcSource? _source;
    LibraryService? _library;

    public string Id => "music";
    public string DisplayName => "Musikplayer (lokal)";
    public bool IsCore => true;
    public bool EnabledByDefault => true;

    public IEnumerable<IDataTemplate> GetTemplates() =>
    [
        new FuncDataTemplate<LibraryViewModel>((_, _) => new LibraryView()),
        new FuncDataTemplate<MusicSettingsViewModel>((_, _) => new MusicSettingsView()),
    ];

    public async Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        var sp = context.Services;
        var store = sp.Require<ISettingsStore>();
        var perf = sp.Require<IPerfTracker>();
        var player = sp.Require<IPlayer>();

        var settings = store.Load<MusicSettings>("music");
        var library = _library = new LibraryService(settings, store, perf);
        var provider = new LibraryProvider(library);
        _source = new LibVlcSource(perf, libVlcDirectory);
        var vm = new LibraryViewModel(library, provider, player);

        context.AddSource(_source);
        context.AddProvider(provider);
        context.AddPage(new NavPage("music", "Musik", Icons.Music, vm, 10));
        context.AddSettingsPage(new SettingsPage("music-settings", "Musik", new MusicSettingsViewModel(library, vm)));
        await vm.InitializeAsync();
        library.StartWatching();
    }

    public async Task DeactivateAsync()
    {
        _library?.StopWatching();
        _library = null;
        if (_source != null) await _source.DisposeAsync();
        _source = null;
    }
}
