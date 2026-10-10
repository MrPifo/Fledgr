using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

/// <summary>
/// Fortschrittsleiste des Players (Hauptfenster und Widget). Sieht aus wie ein Slider, hängt aber am <see cref="ShellViewModel"/>:
/// Beim Ziehen werden Positionsmeldungen der Quelle ignoriert, beim Loslassen wird gespult.
/// Die Position wird von Hand in die Leiste geschrieben statt per Bindung. Eine Bindung kann beim Ziehen verloren gehen,
/// dann läuft die Leiste danach nicht mehr mit.
/// </summary>
public class PlayerSeekBar : Slider
{
    ShellViewModel? _vm;

    public PlayerSeekBar()
    {
        AddHandler(PointerPressedEvent, (_, _) => _vm?.BeginDrag(), RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, (_, _) => _vm?.EndDrag(Value), RoutingStrategies.Bubble, true);
        // Geht das Loslassen verloren (Zeiger woanders losgelassen, Fenster verliert den Fokus), darf die Leiste nicht im Zieh-Zustand hängen bleiben.
        AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (_vm is { Dragging: true }) _vm.EndDrag(Value);
        }, RoutingStrategies.Bubble, true);
        DataContextChanged += (_, _) => Attach();
    }

    /// <summary>Verwendet das Aussehen des normalen Sliders.</summary>
    protected override Type StyleKeyOverride => typeof(Slider);

    void Attach()
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as ShellViewModel;
        if (_vm == null) return;
        _vm.PropertyChanged += OnVmChanged;
        ShowPosition();
    }

    void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Position)) ShowPosition();
        // Das Maximum wird per Bindung nachgezogen. Danach den Wert noch einmal setzen, sonst bleibt er am alten Maximum hängen.
        else if (e.PropertyName == nameof(ShellViewModel.SliderMax)) Dispatcher.UIThread.Post(ShowPosition);
    }

    void ShowPosition()
    {
        if (_vm is not { Dragging: false }) return;
        SetCurrentValue(RangeBase.ValueProperty, Math.Min(_vm.Position, _vm.SliderMax));
    }
}
