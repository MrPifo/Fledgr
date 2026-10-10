using Avalonia.Controls;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => (DataContext as DiagnosticsViewModel)?.Start();
        Closed += (_, _) => (DataContext as DiagnosticsViewModel)?.Stop();
    }
}
