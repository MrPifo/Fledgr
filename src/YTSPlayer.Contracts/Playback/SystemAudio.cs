namespace YTSPlayer.Contracts.Playback;

/// <summary>Zustand der System-Lautstärke (nur lesen). Optional: Köpfe ohne Umsetzung melden kein Stumm.</summary>
public interface ISystemAudio
{
    /// <summary>Wahr, wenn das Standard-Ausgabegerät des Systems stumm geschaltet ist.</summary>
    bool IsMuted();
}
