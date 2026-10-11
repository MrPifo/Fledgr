using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        Closing += (_, e) =>
        {
            if (DataContext is not ShellViewModel { Quitting: false } vm) return;
            e.Cancel = true;
            if (vm.MinimizeOnClose) WindowState = WindowState.Minimized;
            else vm.QuitApp?.Invoke();
        };
    }

    /// <summary>Ein Klick auf den abgedunkelten Hintergrund schließt das Einstellungs-Popup.</summary>
    void OnSettingsBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm) vm.CloseSettingsCommand.Execute(null);
    }

    /// <summary>Strg+K springt in das Suchfeld der Kopfzeile (wenn die offene Seite eins anbietet).</summary>
    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.K || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var box = HeaderHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (box == null) return;
        box.Focus();
        e.Handled = true;
    }

    void OnArtistPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || DataContext is not ShellViewModel vm) return;
        vm.OpenArtistCommand.Execute(null);
        e.Handled = true;
    }

    void OnTrackMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control c && DataContext is ShellViewModel vm) vm.ShowTrackMenu(c);
    }

    void OnAddToPlaylist(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control c && DataContext is ShellViewModel vm) vm.ShowAddToPlaylist(c);
    }

    void OnCoverPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm) CoverLink.Open(this, vm, e);
    }
}
