using YTSPlayer.Contracts.Playback;

namespace YTSPlayer.Contracts.Media;

/// <summary>
/// Plattform-Dienst für Medientasten und die Medienanzeige des Betriebssystems (unter Windows die Lautstärke-/Medien-Übersicht).
/// Er zeigt Titel und Cover an und gibt Play/Pause/Weiter/Zurück/Spulen an den Player weiter.
/// </summary>
public interface IMediaTransport : IDisposable
{
    /// <summary>Verbindet den Dienst mit dem Player. <paramref name="windowHandle"/> ist die native Kennung des Hauptfensters.</summary>
    void Attach(IPlayer player, IntPtr windowHandle);
}
