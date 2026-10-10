namespace YTSPlayer.Contracts.Settings;

/// <summary>Plattform-Feinheiten für Fenster, die Avalonia nicht selbst kann. Der Android-Kopf braucht keine Umsetzung.</summary>
public interface IWindowEffects
{
    /// <summary>Schaltet die abgerundeten Fensterecken des Systems ab (Windows 11). Das Fenster muss schon ein Handle haben.</summary>
    void SquareCorners(IntPtr nativeHandle);
}
