using System.Runtime.InteropServices;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Platform.Windows;

/// <summary>Fenster-Feinheiten über die Desktop Window Manager API (dwmapi).</summary>
public sealed class WindowsWindowEffects : IWindowEffects
{
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwcpDoNotRound = 1;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
}
