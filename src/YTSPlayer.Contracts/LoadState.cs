namespace YTSPlayer.Contracts;

/// <summary>Zustand einer Seite mit Netzwerk oder Scan. Die UI zeigt nur diesen Zustand.</summary>
public enum LoadState
{
    Idle,
    Loading,
    Ready,
    Empty,
    Error,
}
