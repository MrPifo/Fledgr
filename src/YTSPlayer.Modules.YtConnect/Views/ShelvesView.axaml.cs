using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using YTSPlayer.Modules.YtConnect.ViewModels;

namespace YTSPlayer.Modules.YtConnect.Views;

/// <summary>Zeigt Bereiche mit Karten. Ein Klick auf eine Karte öffnet sie, Rechtsklick zeigt das Menü. Das ViewModel kommt aus dem geerbten DataContext.</summary>
public partial class ShelvesView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> SectionsProperty = AvaloniaProperty.Register<ShelvesView, IEnumerable?>(nameof(Sections));

    public ShelvesView()
    {
        InitializeComponent();
        Loaded += (_, _) => Shelves.ItemsSource = Sections;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SectionsProperty && Shelves != null) Shelves.ItemsSource = Sections;
    }

    /// <summary>Die Bereiche (<see cref="ArtistSectionVm"/>), die gezeigt werden.</summary>
    public IEnumerable? Sections { get => GetValue(SectionsProperty); set => SetValue(SectionsProperty, value); }

    YtViewModel? Vm => DataContext as YtViewModel;

    void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (Vm != null && (sender as Control)?.DataContext is TrackItem t) _ = Vm.OpenCardAsync(t);
    }

    void OnCardContext(object? sender, ContextRequestedEventArgs e)
    {
        if (Vm == null || sender is not Control c || c.DataContext is not TrackItem t) return;
        e.Handled = true;
        YtMenus.ShowTrack(Vm, t, c, atPointer: true);
    }
}
