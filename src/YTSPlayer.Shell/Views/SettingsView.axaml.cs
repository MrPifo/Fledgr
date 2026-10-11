using Avalonia.Controls;
using Avalonia.Interactivity;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
                vm.PropertyChanged += (_, e) =>
                {
                    // Tabwechsel: der neue Inhalt blendet kurz ein und schiebt sich leicht von links ein.
                    if (e.PropertyName == nameof(SettingsViewModel.SelectedCategory) && this.FindControl<StackPanel>("Panels") is { } panels)
                        _ = YTSPlayer.Ui.Controls.SlideInTransition.Play(panels, 0.18, 14);
                };
        };
    }

    /// <summary>Öffnet den Ordner mit den Logdateien im Datei-Explorer.</summary>
    async void OnOpenLogFolder(object? sender, RoutedEventArgs e)
    {
        if (AppLog.Directory is not { } dir || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher) return;
        try { await launcher.LaunchUriAsync(new Uri(dir)); }
        catch (Exception ex) { AppLog.Warn("Einstellungen", "Logordner konnte nicht geöffnet werden", ex); }
    }
}
