using Avalonia.Controls;
using Avalonia.Interactivity;
using YTSPlayer.Contracts.Diagnostics;

namespace YTSPlayer.Shell.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Öffnet den Ordner mit den Logdateien im Datei-Explorer.</summary>
    async void OnOpenLogFolder(object? sender, RoutedEventArgs e)
    {
        if (AppLog.Directory is not { } dir || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher) return;
        try { await launcher.LaunchUriAsync(new Uri(dir)); }
        catch (Exception ex) { AppLog.Warn("Einstellungen", "Logordner konnte nicht geöffnet werden", ex); }
    }
}
