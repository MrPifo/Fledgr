using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using YTSPlayer.Contracts;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Localization;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Desktop.SelfTests;
using YTSPlayer.Modules.MusicPlayer;
using YTSPlayer.Modules.RadioPlayer;
using YTSPlayer.Modules.YtConnect;
using YTSPlayer.Modules.YtConnect.Windows;
using YTSPlayer.Platform.Windows;
using YTSPlayer.Shell;

namespace YTSPlayer.Desktop;

public partial class App : Application
{
    public const string AppName = "Fledgr";
    const string OldAppName = "YT-SPlayer";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        RequestedThemeVariant = YTSPlayer.Ui.AppThemes.Graphit;
    }

    /// <summary>App-Ordner unter <paramref name="root"/>. Gibt es nur den alten Ordner (YT-SPlayer), wird er einmalig umbenannt.</summary>
    static string AppFolder(Environment.SpecialFolder root)
    {
        var baseDir = Environment.GetFolderPath(root);
        var dir = Path.Combine(baseDir, AppName);
        var old = Path.Combine(baseDir, OldAppName);
        try
        {
            if (!Directory.Exists(dir) && Directory.Exists(old)) Directory.Move(old, dir);
        }
        catch { } // z. B. gesperrt: dann startet die App mit leerem Ordner, der alte bleibt erhalten.
        return dir;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        YTSPlayer.Ui.Behaviors.GlobalDefocus.Install();
        YTSPlayer.Ui.Behaviors.TextBoxClear.Install();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var selfTest = args.Any(a => a.StartsWith("--selftest"));
            var uiTest = args.Contains("--selftest-ui");

            // Selbsttests laufen mit eigenen Ordnern, damit die echten Einstellungen und das Login unberührt bleiben.
            var root = selfTest ? Path.Combine(Path.GetTempPath(), "ytsp-selftest-" + Guid.NewGuid().ToString("N")[..8]) : null;
            var paths = root != null
                ? new AppPaths(Path.Combine(root, "data"), Path.Combine(root, "cache"), Path.Combine(root, "export"))
                : new AppPaths(
                    AppFolder(Environment.SpecialFolder.ApplicationData),
                    AppFolder(Environment.SpecialFolder.LocalApplicationData),
                    AppFolder(Environment.SpecialFolder.MyDocuments));
            var store = new JsonSettingsStore(paths, readOnly: selfTest);

            if (!selfTest)
            {
                AppLog.Init(Path.Combine(paths.Cache, "logs"));
                AppLog.Info("App", $"Start, Version {typeof(App).Assembly.GetName().Version}, {Environment.OSVersion}");
                AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Error("Absturz", "Unbehandelter Fehler", e.ExceptionObject as Exception);
                TaskScheduler.UnobservedTaskException += (_, e) => { AppLog.Error("Task", "Nicht beachteter Fehler in einer Aufgabe", e.Exception); e.SetObserved(); };
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => AppLog.Error("Oberfläche", "Unbehandelter Fehler im UI-Thread", e.Exception);
            }

            var settings = store.Load<AppSettings>("app");

            // Sprache und Theme aus den Einstellungen. Alle Projekte mit Strings\*.xml werden angemeldet.
            var loc = Loc.Current;
            foreach (var assembly in new[] { typeof(YTSPlayer.Ui.AppThemes), typeof(ShellApp), typeof(MusicPlayerPlugin), typeof(RadioPlayerPlugin), typeof(YtConnectPlugin), typeof(App) }.Select(t => t.Assembly))
                loc.AddAssembly(assembly);
            // Zum Testen: --lang=en-GB überschreibt die Sprache aus den Einstellungen (ohne sie zu speichern).
            var langArg = args.FirstOrDefault(a => a.StartsWith("--lang="));
            loc.SetLanguage(langArg != null ? langArg[7..] : settings.Language);
            RequestedThemeVariant = YTSPlayer.Ui.AppThemes.Get(settings.Theme);

            if (selfTest && !uiTest)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SelfTest.Start(args, paths, store, () => desktop.Shutdown());
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var services = new ServiceRegistry()
                .Add<IAppPaths>(paths)
                .Add<ISettingsStore>(store)
                .Add(settings)
                .Add<ILocalizer>(loc)
                .Add<IPerfTracker>(new PerfTracker())
                .Add<IProcessStats>(new WindowsProcessStats())
                .Add<IWindowEffects>(new WindowsWindowEffects())
                .Add<IMediaTransport>(new WindowsMediaTransport())
                .Add<ISystemAudio>(new WindowsSystemAudio())
                .Add<IChildAudioVolume>(new WindowsChildAudio());

            var plugins = new List<IPlugin>
            {
                new MusicPlayerPlugin(LibVlcLocator.Find),
                new YtConnectPlugin(s => new WebView2YtHost(s, paths)),
                new RadioPlayerPlugin(LibVlcLocator.Find),
            };
            if (args.Contains("--selftest-ui-fake")) plugins.Add(new FakeAccountPlugin());

            var shell = new ShellApp(services, plugins, () => AssetLoader.Open(new Uri($"avares://{typeof(App).Assembly.GetName().Name}/Assets/fledgr.ico")));
            shell.Attach(desktop, this);
            _ = shell.StartAsync();
            if (uiTest) _ = UiShots.RunAsync(shell, () => desktop.Shutdown());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
