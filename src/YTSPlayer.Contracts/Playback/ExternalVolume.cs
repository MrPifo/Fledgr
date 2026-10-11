namespace YTSPlayer.Contracts.Playback;

/// <summary>
/// Optional für Quellen, deren Lautstärke auch außerhalb der App geändert werden kann (z. B. im Windows-Lautstärkemixer).
/// Der Wert (0–1) ist die Reglerstellung. Das Ereignis darf von jedem Thread kommen.
/// </summary>
public interface IExternalVolumeSource
{
    event Action<double>? VolumeChangedExternally;
}
