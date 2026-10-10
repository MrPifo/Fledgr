using Avalonia.Controls;
using Avalonia.Input;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DataContext is not ShellViewModel { Quitting: false } vm) return;
            e.Cancel = true;
            if (vm.MinimizeOnClose) WindowState = WindowState.Minimized;
            else vm.QuitApp?.Invoke();
        };
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

    void OnCoverPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel vm) CoverLink.Open(this, vm, e);
    }
}
