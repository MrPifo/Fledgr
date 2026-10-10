using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using YTSPlayer.Platform.Windows;

namespace YTSPlayer.Modules.YtConnect.Windows;

/// <summary>
/// Fläche im Hauptfenster, in der die Web-Ansicht während der Anmeldung sichtbar ist.
/// Sie legt ein Windows-Kind-Fenster an. Die Web-Ansicht wird dort eingehängt und beim Verlassen wieder herausgenommen.
/// </summary>
sealed class LoginSurface : NativeControlHost
{
    public IntPtr Hwnd { get; private set; }

    /// <summary>Das Kind-Fenster existiert jetzt.</summary>
    public event Action? Attached;

    /// <summary>Das Kind-Fenster wird gleich zerstört. Die Web-Ansicht muss vorher raus.</summary>
    public event Action? Detaching;

    /// <summary>Größe oder Skalierung haben sich geändert.</summary>
    public event Action? Resized;

    public LoginSurface() => PropertyChanged += (_, e) =>
    {
        if (e.Property == BoundsProperty) Resized?.Invoke();
    };

    /// <summary>Größe der Fläche in Geräte-Pixeln.</summary>
    public (int W, int H) PixelSize
    {
        get
        {
            var sc = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            return ((int)Math.Max(1, Bounds.Width * sc), (int)Math.Max(1, Bounds.Height * sc));
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        Hwnd = Native.CreateChildWindow(parent.Handle);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Attached?.Invoke());
        return new PlatformHandle(Hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Native.DestroyChildWindow(control.Handle);
        Hwnd = IntPtr.Zero;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Detaching?.Invoke();
        base.OnDetachedFromVisualTree(e);
    }
}
