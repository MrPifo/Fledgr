using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

public partial class WidgetWindow : Window
{
    /// <summary>Abstand in Bildschirm-Pixeln, ab dem das Widget beim Ziehen an einen Rand einrastet.</summary>
    const int SnapDistance = 18;

    readonly IWindowEffects? _effects;
    bool _dragging;
    PixelPoint _pointerStart;
    PixelPoint _windowStart;

    public WidgetWindow() : this(null)
    {
    }

    public WidgetWindow(IWindowEffects? effects)
    {
        _effects = effects;
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle() is { } h) _effects?.SquareCorners(h.Handle);
        };
    }

    /// <summary>Ziehen beginnt: Mausposition und Fensterposition am Bildschirm merken.</summary>
    void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _pointerStart = this.PointToScreen(e.GetPosition(this));
        _windowStart = Position;
        e.Pointer.Capture(sender as IInputElement);
    }

    /// <summary>
    /// Die Position kommt immer aus der Mausbewegung seit dem Start (nicht aus der letzten Fensterposition).
    /// Das Einrasten verändert nur die angezeigte Stelle, darum klebt das Widget nicht fest: Wer weiter zieht, löst sich sofort.
    /// </summary>
    void OnDragMove(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var now = this.PointToScreen(e.GetPosition(this));
        var wanted = new PixelPoint(_windowStart.X + now.X - _pointerStart.X, _windowStart.Y + now.Y - _pointerStart.Y);
        Position = Snap(wanted);
    }

    void OnDragEnd(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
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

    /// <summary>
    /// Magnet: Kommt das Fenster an der gewünschten Stelle einem Rand des nutzbaren Bildschirms nahe, rastet es dort ein.
    /// Der nutzbare Bereich (WorkingArea) endet an der Taskleiste, das Widget hängt sich also auch an sie.
    /// Welcher Bildschirm gilt, bestimmt die Fenstermitte, damit man problemlos auf einen anderen Monitor ziehen kann.
    /// </summary>
    PixelPoint Snap(PixelPoint wanted)
    {
        var scale = RenderScaling;
        var w = (int)Math.Round(Bounds.Width * scale);
        var h = (int)Math.Round(Bounds.Height * scale);
        var center = new PixelPoint(wanted.X + w / 2, wanted.Y + h / 2);
        var screen = Screens.ScreenFromPoint(center) ?? Screens.ScreenFromWindow(this);
        if (screen == null) return wanted;
        var area = screen.WorkingArea;
        var x = wanted.X;
        var y = wanted.Y;

        if (Math.Abs(x - area.X) <= SnapDistance) x = area.X;
        else if (Math.Abs(x + w - area.Right) <= SnapDistance) x = area.Right - w;
        if (Math.Abs(y - area.Y) <= SnapDistance) y = area.Y;
        else if (Math.Abs(y + h - area.Bottom) <= SnapDistance) y = area.Bottom - h;
        return new PixelPoint(x, y);
    }
}
