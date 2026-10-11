using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Ui;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Schaltet Plugins ein und aus und sammelt, was sie in die App einhängen (Seiten, Quellen, Tray-Einträge …).
/// Ausgeschaltet heißt: alles Eingehängte wird entfernt und das Plugin gibt seine Ressourcen frei.
/// </summary>
public sealed class PluginHost(IServiceProvider services, AppSettings settings, ISettingsStore store, PlayerService player, IReadOnlyList<IPlugin> plugins, IPerfTracker perf)
    : IPluginRegistry
{
    sealed class Contributions
    {
        public readonly List<IPlaybackSource> Sources = [];
        public readonly List<IMediaProvider> Providers = [];
        public readonly List<NavPage> Pages = [];
        public readonly List<HomeSection> HomeSections = [];
        public readonly List<SettingsPage> SettingsPages = [];
        public readonly List<TrayEntry> TrayEntries = [];
        public readonly List<IDiagnosticsContributor> Diagnostics = [];
        public readonly List<IDataTemplate> Templates = [];
        public IAccountSession? Account;
    }

    sealed class Context(PluginHost host, Contributions c) : IPluginContext
    {
        public IServiceProvider Services => host._services;
        public void AddSource(IPlaybackSource source) => Ui(() => { c.Sources.Add(source); host._player.Register(source); });
        public void AddProvider(IMediaProvider provider) => Ui(() => { c.Providers.Add(provider); host.Providers.Add(provider); });
        public void AddPage(NavPage page) => Ui(() => { c.Pages.Add(page); host.InsertSorted(page); });
        public void AddHomeSection(HomeSection section) => Ui(() => { c.HomeSections.Add(section); host.InsertSorted(section); });
        public void AddSettingsPage(SettingsPage page) => Ui(() => { c.SettingsPages.Add(page); host.SettingsPages.Add(page); });
        public void AddTrayEntry(TrayEntry entry) => Ui(() => { c.TrayEntries.Add(entry); host.TrayEntries.Add(entry); host.RaiseChanged(); });
        public void AddDiagnostics(IDiagnosticsContributor contributor) => Ui(() => { c.Diagnostics.Add(contributor); host.Diagnostics.Add(contributor); });

        public void SetAccount(IAccountSession session) => Ui(() =>
        {
            c.Account = session;
            host.Account = session;
            session.Changed += host.OnAccountChanged;
            host.RaiseAccountChanged();
        });

        static void Ui(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Invoke(a);
        }
    }

    readonly IServiceProvider _services = services;
    readonly PlayerService _player = player;
    readonly Dictionary<string, Contributions> _active = [];

    public IReadOnlyList<IPlugin> Plugins { get; } = plugins;
    public ObservableCollection<NavPage> Pages { get; } = [];
    public ObservableCollection<HomeSection> HomeSections { get; } = [];
    public ObservableCollection<SettingsPage> SettingsPages { get; } = [];
    public ObservableCollection<TrayEntry> TrayEntries { get; } = [];
    public List<IDiagnosticsContributor> Diagnostics { get; } = [];
    public List<IMediaProvider> Providers { get; } = [];
    public IAccountSession? Account { get; private set; }

    public event Action? Changed;
    public event Action? AccountChanged;

    public bool IsEnabled(string pluginId)
    {
        var p = Plugins.FirstOrDefault(x => x.Id == pluginId);
        if (p == null) return false;
        return p.IsCore || (settings.Plugins.TryGetValue(pluginId, out var on) ? on : p.EnabledByDefault);
    }

    /// <summary>Aktiviert beim Start alle eingeschalteten Plugins. Kern zuerst.</summary>
    public async Task InitializeAsync()
    {
        foreach (var p in Plugins.OrderByDescending(x => x.IsCore))
            if (IsEnabled(p.Id)) await ActivateAsync(p);
        Changed?.Invoke();
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled)
    {
        var p = Plugins.FirstOrDefault(x => x.Id == pluginId) ?? throw new ArgumentException("Unbekanntes Plugin: " + pluginId);
        if (p.IsCore || IsEnabled(pluginId) == enabled) return;
        settings.Plugins[pluginId] = enabled;
        store.Save("app", settings);
        if (enabled) await ActivateAsync(p);
        else await DeactivateAsync(p);
        Changed?.Invoke();
    }

    /// <summary>Beendet alle Plugins (beim Beenden der App).</summary>
    public async Task ShutdownAsync()
    {
        foreach (var p in Plugins.Reverse().ToList())
            if (_active.ContainsKey(p.Id)) await DeactivateAsync(p);
    }

    async Task ActivateAsync(IPlugin p)
    {
        using var scope = perf.Begin("Plugin.Activate");
        var c = new Contributions();
        _active[p.Id] = c;
        try
        {
            if (p is IViewProvider vp)
                foreach (var t in vp.GetTemplates())
                {
                    c.Templates.Add(t);
                    Application.Current?.DataTemplates.Add(t);
                }
            await p.ActivateAsync(new Context(this, c), CancellationToken.None);
        }
        catch (Exception ex)
        {
            scope.Fail();
            AppLog.Error("Plugin", $"Aktivieren von „{p.Id}“ fehlgeschlagen", ex);
            await DeactivateAsync(p);
            throw;
        }
    }

    async Task DeactivateAsync(IPlugin p)
    {
        if (!_active.Remove(p.Id, out var c)) return;
        foreach (var s in c.Sources) await _player.UnregisterAsync(s);
        foreach (var x in c.Pages) Pages.Remove(x);
        foreach (var x in c.HomeSections) HomeSections.Remove(x);
        foreach (var x in c.SettingsPages) SettingsPages.Remove(x);
        foreach (var x in c.TrayEntries) TrayEntries.Remove(x);
        foreach (var x in c.Providers) Providers.Remove(x);
        foreach (var x in c.Diagnostics) Diagnostics.Remove(x);
        foreach (var t in c.Templates) Application.Current?.DataTemplates.Remove(t);
        if (c.Account != null)
        {
            c.Account.Changed -= OnAccountChanged;
            if (Account == c.Account) Account = null;
            RaiseAccountChanged();
        }
        try { await p.DeactivateAsync(); }
        catch (Exception ex) { AppLog.Warn("Plugin", $"Freigeben von „{p.Id}“ fehlgeschlagen", ex); }
    }

    /// <summary>Hängt eine Seite der Shell selbst ein (z. B. Home). Sie gehört keinem Plugin und bleibt immer.</summary>
    public void AddShellPage(NavPage page) => InsertSorted(page);

    void InsertSorted(HomeSection section)
    {
        var i = 0;
        while (i < HomeSections.Count && HomeSections[i].Order <= section.Order) i++;
        HomeSections.Insert(i, section);
    }

    void InsertSorted(NavPage page)
    {
        var i = 0;
        while (i < Pages.Count && Pages[i].Order <= page.Order) i++;
        Pages.Insert(i, page);
    }

    void OnAccountChanged() => Dispatcher.UIThread.Post(RaiseAccountChanged);

    void RaiseAccountChanged() => AccountChanged?.Invoke();

    void RaiseChanged() => Changed?.Invoke();
}
