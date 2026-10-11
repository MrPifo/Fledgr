namespace YTSPlayer.Contracts.Playback;

/// <summary>
/// Lautstärke der Audio-Einträge, die zu den Kindprozessen der App gehören (die WebView der YouTube-Wiedergabe).
/// Auf Windows ist das der Eintrag „Microsoft Edge WebView2“ im Lautstärkemixer. Optional: Köpfe ohne Umsetzung lassen sie weg.
/// </summary>
public interface IChildAudioVolume
{
    /// <summary>Aktuelle Lautstärke (0–1) der Einträge, oder null, wenn es noch keinen gibt (die Seite hat noch keinen Ton gespielt).</summary>
    double? Get();

    /// <summary>Setzt die Lautstärke (0–1) aller Einträge der Kindprozesse. Wahr, wenn mindestens einer gefunden wurde.</summary>
    bool Set(double volume);
}
