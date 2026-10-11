using Avalonia.Controls.Templates;
using Fledgr.Audio;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.RadioPlayer.Data;
using YTSPlayer.Modules.RadioPlayer.Playback;
using YTSPlayer.Modules.RadioPlayer.ViewModels;
using YTSPlayer.Modules.RadioPlayer.Views;
using YTSPlayer.Ui;

namespace YTSPlayer.Modules.RadioPlayer;

/// <summary>Plugin „Radio“: Sender suchen (radio-browser.info), Favoriten, Verlauf und Stream-Adressen per Hand.</summary>
public sealed class RadioPlayerPlugin(Func<string?>? libVlcDirectory = null) : IPlugin, IViewProvider
{
    RadioStreamSource? _source;
    RadioViewModel? _vm;
    HttpClient? _http;

    public string Id => "radio";
    public string DisplayName => "radio.plugin.name";
    public bool IsCore => false;
    public bool EnabledByDefault => true;

    public IEnumerable<IDataTemplate> GetTemplates() =>
    [
        new FuncDataTemplate<RadioViewModel>((_, _) => new RadioView()),
        new FuncDataTemplate<RadioHomeViewModel>((_, _) => new RadioHomeView()),
        new FuncDataTemplate<RadioSearchHeader>((_, _) => new YTSPlayer.Ui.Controls.SearchBox { PlaceholderKey = "radio.search.placeholder", [!YTSPlayer.Ui.Controls.SearchBox.TextProperty] = new Avalonia.Data.Binding("Vm.SearchText") { Mode = Avalonia.Data.BindingMode.TwoWay } }),
        new FuncDataTemplate<RadioSettingsViewModel>((_, _) => new RadioSettingsView()),
    ];

    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        var sp = context.Services;
        var store = new RadioStore(sp.Require<ISettingsStore>());
        var perf = sp.Require<IPerfTracker>();
        var player = sp.Require<IPlayer>();

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Fledgr/" + (typeof(RadioPlayerPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
        var client = new RadioBrowserClient(_http);
        _source = new RadioStreamSource(new LibVlcEngine(libVlcDirectory), store, client, perf);
        _vm = new RadioViewModel(store, client, player, _source, perf, sp.Require<IThumbnailCache>(), sp.Require<ISettingsStore>());

        context.AddSource(_source);
        context.AddPage(new NavPage("radio", "radio.nav", Icons.Radio, _vm, 30, "radio"));
        context.AddHomeSection(new HomeSection("radio-home", "radio.nav", "radio", 20, new RadioHomeViewModel(_vm), "radio"));
        context.AddSettingsPage(new SettingsPage("radio-settings", "radio.settings.title", new RadioSettingsViewModel(sp.Require<AppSettings>(), sp.Require<ISettingsStore>())));
        _vm.Initialize();
        return Task.CompletedTask;
    }

    public async Task DeactivateAsync()
    {
        _vm?.Dispose();
        _vm = null;
        if (_source != null) await _source.DisposeAsync();
        _source = null;
        _http?.Dispose();
        _http = null;
    }
}
