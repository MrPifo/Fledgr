using Avalonia;
using Avalonia.Controls;
using YTSPlayer.Platform.Windows;

namespace YTSPlayer.Modules.YtConnect.Windows;

/// <summary>
/// Unsichtbares Fenster, das der Web-Ansicht ein Fenster-Handle gibt, solange sie versteckt läuft.
/// Es liegt klein außerhalb des Bildschirms, damit Chromium es weiter als sichtbar ansieht (sonst lädt kein Audio).
/// </summary>
sealed class HiddenHostWindow : Window
{
    const int W = 320, H = 200;

    public new event Action? Resized;
    public bool AllowClose { get; set; }

    public HiddenHostWindow()
    {
        Title = "Fledgr – Web-Ansicht";
        SizeChanged += (_, _) => Resized?.Invoke();
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
        };
    }

    public IntPtr Hwnd => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public void ShowOffscreen()
    {
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = W;
        Height = H;
        if (!IsVisible) Show();
        var h = Hwnd;
        if (h == IntPtr.Zero) return;
        var sc = RenderScaling;
        Native.SetToolWindow(h, true);
        Native.PlaceWindow(h, -32000, -32000, (int)(W * sc), (int)(H * sc));
    }
}
