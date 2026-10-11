using CommunityToolkit.Mvvm.ComponentModel;

namespace YTSPlayer.Ui;

/// <summary>
/// Basis für Zeilen in Listen (YouTube, Musik, Radio), die den laufenden Titel anzeigen können:
/// <see cref="IsCurrent"/> = diese Zeile ist der aktuelle Titel (Balken sichtbar), <see cref="IsPlayingNow"/> = er spielt gerade (Balken bewegen sich).
/// </summary>
public abstract class PlayingRow : ObservableObject
{
    bool _isCurrent;
    bool _isPlayingNow;

    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }

    public bool IsPlayingNow { get => _isPlayingNow; set => SetProperty(ref _isPlayingNow, value); }

    /// <summary>Setzt beide Zustände auf einmal.</summary>
    public void SetPlaying(bool current, bool playing)
    {
        IsCurrent = current;
        IsPlayingNow = current && playing;
    }
}
