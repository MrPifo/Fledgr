using System.Collections.ObjectModel;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>Ein Bereich der Künstler-Seite mit seinen Karten (Alben, Singles, Playlists, Künstler, Videos).</summary>
public sealed record ArtistSectionVm(string Title, ObservableCollection<TrackItem> Items);
