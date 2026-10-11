using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Shell.Services;
using YTSPlayer.Shell.ViewModels;
using YTSPlayer.Shell.Views;
using YTSPlayer.Contracts.Localization;
using YTSPlayer.Ui;

namespace YTSPlayer.Shell;

/// <summary>
/// Baut die Oberfläche zusammen: Dienste, Plugin-Host, Fenster, Tray. Der Composition Root (Desktop) liefert nur Dienste und Plugins.
/// </summary>
public sealed class ShellApp
{
    readonly ServiceRegistry _services;
    readonly AppSettings _settings;
    readonly ISettingsStore _store;
    readonly PluginHost _host;
    readonly ShellViewModel _vm;
    readonly MainWindow _main;
    readonly Func<Stream> _openIcon;
    TrayService? _tray;
    readonly SessionRecorder _session;
    readonly HomeViewModel _home;
    readonly SleepTimer _sleep;

    /// <summary>Jede Eingabe in einem Fenster zählt für den Sleep-Timer als Bedienung (auch bereits behandelte Ereignisse).</summary>
    void WatchInput(Window w)
    {
        EventHandler<RoutedEventArgs> touch = (_, _) => _sleep.Touch();
        foreach (var ev in new RoutedEvent[] { InputElement.PointerMovedEvent, InputElement.PointerPressedEvent, InputElement.KeyDownEvent, InputElement.PointerWheelChangedEvent })
            w.AddHandler(ev, touch, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public ShellApp(ServiceRegistry services, IReadOnlyList<IPlugin> plugins, Func<Stream> openIcon)
    {
        _services = services;
        _settings = services.Require<AppSettings>();
        _store = services.Require<ISettingsStore>();
        var perf = services.Require<IPerfTracker>();
        var stats = services.Require<IProcessStats>();
        var paths = services.Require<IAppPaths>();

        var player = new PlayerService(_settings, perf);
        player.Persist = () => _store.Save("app", _settings);
        var covers = new CoverLoader(perf);
        _host = new PluginHost(services, _settings, _store, player, plugins, perf);
        var monitor = new ProcessMonitor(stats, _host);
        var sleep = new SleepTimer(_settings, _store, player);
        var settingsVm = new SettingsViewModel(_settings, _store, _host, sleep, paths) { VolumeCurveChanged = player.ReapplyVolume };
        _vm = new ShellViewModel(_settings, player, _host, covers, settingsVm);
        // Home: die Startseite der App. Der globale Verlauf lebt mit dem Player.
        var history = History = new PlayHistory(_store, player);
        _home = new HomeViewModel(player, _host, history, covers, id => _vm.NavigateTo(id));
        Application.Current?.DataTemplates.Add(new FuncDataTemplate<HomeViewModel>((_, _) => new HomeView()));
        _host.AddShellPage(new NavPage("home", "home.nav", Icons.Home, _home, 0, "accent"));
        // Titel-Anzeige über allen Programmen. Der Dienst hängt sich an den Player und lebt mit ihm.
        _ = new NowPlayingOverlayService(_settings, player, services.Get<IWindowEffects>());
        _vm.AttachSleep(sleep);
        if (services.Get<YTSPlayer.Contracts.Playback.ISystemAudio>() is { } systemAudio) _vm.AttachSystemAudio(systemAudio);
        _sleep = sleep;
        _session = new SessionRecorder(_store, player);
        _vm.PageChanged = _session.SetPage;

        services.Add<IThumbnailCache>(new ThumbnailCache(paths, perf));
        services.Add<IPlayHistory>(history);
        services.Add<IPlayer>(player).Add<IPluginRegistry>(_host).Add<INavigator>(_vm).Add(covers);

        _openIcon = openIcon;
        _main = new MainWindow { DataContext = _vm, Icon = new WindowIcon(openIcon()) };
        WatchInput(_main);
        WindowStateKeeper.Bind(_main, () => _settings.MainWindow, p => _settings.MainWindow = p, () => _store.Save("app", _settings), trackSize: true);
        WidgetWindow? widget = null;
        DiagnosticsWindow? diag = null;

        // Entweder die große App oder das Widget ist zu sehen, nie beides.
        _vm.ShowMain = () =>
        {
            // Erst das große Fenster nach vorn holen, dann das Widget verstecken. Versteckt man zuerst das aktive Widget,
            // gibt Windows den Fokus an ein fremdes Fenster weiter, und die große App landet dahinter.
            _main.Show();
            _main.WindowState = WindowState.Normal;
            _main.Topmost = true;
            _main.Activate();
            _main.Topmost = false;
            widget?.Hide();
            RememberWidgetMode(false);
        };
        _vm.OpenWidget = () =>
        {
            if (widget == null)
            {
                widget = new WidgetWindow(_services.Get<IWindowEffects>()) { DataContext = _vm };
                WatchInput(widget);
                WindowStateKeeper.Bind(widget, () => _settings.Widget, p => _settings.Widget = p, () => _store.Save("app", _settings), trackSize: false);
            }
            widget.Show();
            _main.Hide();
            RememberWidgetMode(true);
        };
        _vm.OpenDiagnostics = () =>
        {
            if (diag is { IsVisible: true }) { diag.Activate(); return; }
            diag = new DiagnosticsWindow { DataContext = new DiagnosticsViewModel(_settings, monitor, _host, perf, paths) };
            diag.Show();
        };
    }

    /// <summary>Merkt, ob die App im Miniplayer läuft, damit der nächste Start dort beginnt.</summary>
    void RememberWidgetMode(bool widget)
    {
        if (_settings.StartInWidget == widget) return;
        _settings.StartInWidget = widget;
        _store.Save("app", _settings);
    }

    public Window MainWindow => _main;
    public ShellViewModel ViewModel => _vm;
    public PluginHost Host => _host;

    /// <summary>Der globale Verlauf (für Selbsttests).</summary>
    public PlayHistory History { get; }

    /// <summary>Verbindet das Beenden mit der Desktop-Lebensdauer und richtet den Tray ein.</summary>
    public void Attach(IClassicDesktopStyleApplicationLifetime desktop, Application app, Action? beforeQuit = null)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.MainWindow = _main;

        // Zuletzt im Miniplayer beendet: gleich nach dem Öffnen des Hauptfensters zum Widget wechseln.
        if (_settings.StartInWidget)
        {
            void OpenAsWidget(object? s, EventArgs e)
            {
                _main.Opened -= OpenAsWidget;
                _vm.OpenWidget?.Invoke();
            }
            _main.Opened += OpenAsWidget;
        }

        // Medientasten und Medienanzeige des Betriebssystems, sobald das Hauptfenster eine native Kennung hat.
        if (_services.Get<IMediaTransport>() is { } transport && _services.Get<IPlayer>() is { } player)
        {
            void Connect()
            {
                if (_main.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero) transport.Attach(player, h);
            }
            _main.Opened += (_, _) => Connect();
            if (_main.IsVisible) Connect();
        }

        _vm.QuitApp = async () =>
        {
            _vm.Quitting = true;
            _services.Get<IMediaTransport>()?.Dispose();
            _session.Flush();
            _store.Save("app", _settings);
            beforeQuit?.Invoke();
            // Hängt ein Modul beim Freigeben (z. B. WebView2), beenden wir nach 5 s trotzdem.
            await Task.WhenAny(_host.ShutdownAsync(), Task.Delay(TimeSpan.FromSeconds(10)));
            desktop.Shutdown();
            _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => Environment.Exit(0));
        };
        _tray = new TrayService(app, _vm, _host, _openIcon());
    }

    /// <summary>Aktiviert alle eingeschalteten Plugins.</summary>
    public async Task StartAsync()
    {
        try
        {
            await _host.InitializeAsync();
        }
        catch (Exception ex)
        {
            _vm.Status = Loc.F("shell.error.module", ex.Message);
        }
        if (_vm.Nav.Count > 0) _vm.SelectedPage = _vm.Nav[0];
        await RestoreSessionAsync();
    }

    /// <summary>Öffnet die zuletzt offene Seite und stellt den zuletzt gespielten Titel an seiner Position wieder her.</summary>
    async Task RestoreSessionAsync()
    {
        try
        {
            // Erst den Titel: Er startet die Web-Ansicht mit seiner Adresse. Würde die Seite zuerst die Startseite laden,
            // bräche der Titel-Aufruf direkt danach diesen Seitenaufruf ab, und der Titel käme erst nach ~60 s.
            await _session.RestoreTrackAsync();
            // Die App startet immer auf Home. Wo man in einem Modul war, merkt sich das Modul selbst.
        }
        catch (Exception ex)
        {
            _vm.Status = Loc.F("shell.error.session", ex.Message);
        }
        finally
        {
            _session.Arm();
        }
    }
}
