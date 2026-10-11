namespace YTSPlayer.Modules.MusicPlayer.ViewModels;

/// <summary>Was in der Musik-Bibliothek zuletzt offen war (Abschnitt „music-view“): Ansicht und geöffneter Ordner oder Playlist.</summary>
public sealed class MusicViewState
{
    public int ModeIndex { get; set; }
    public string CurrentId { get; set; } = "";
    public string CurrentTitle { get; set; } = "";
    public int CurrentKind { get; set; }
}
