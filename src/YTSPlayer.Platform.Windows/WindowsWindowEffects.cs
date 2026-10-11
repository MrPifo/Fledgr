using System.Runtime.InteropServices;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Platform.Windows;

/// <summary>Fenster-Feinheiten über die Desktop Window Manager API (dwmapi) und die Fensterstile (user32).</summary>
public sealed class WindowsWindowEffects : IWindowEffects
{
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwcpDoNotRound = 1;
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x20;
    const long WsExToolWindow = 0x80;
    const long WsExLayered = 0x80000;
    const long WsExNoActivate = 0x08000000;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    public void SquareCorners(IntPtr nativeHandle)
    {
        if (nativeHandle == IntPtr.Zero) return;
        try
        {
            var pref = DwmwcpDoNotRound;
            // Ältere Windows-Versionen kennen das Attribut nicht und runden ohnehin nicht. Der Fehlercode ist egal.
            DwmSetWindowAttribute(nativeHandle, DwmwaWindowCornerPreference, ref pref, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
    }

    public void MakeClickThroughOverlay(IntPtr nativeHandle)
    {
        if (nativeHandle == IntPtr.Zero) return;
        try
        {
            var style = GetWindowLongPtr(nativeHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(nativeHandle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate));
        }
        catch (DllNotFoundException)
        {
        }
    }
}
