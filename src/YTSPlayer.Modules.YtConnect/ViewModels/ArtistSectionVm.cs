using System.Collections.ObjectModel;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>Ein Bereich der Künstler-Seite oder Startseite mit seinen Karten (Alben, Singles, Playlists, Künstler, Videos) oder Zeilen (Titel).</summary>
public sealed record ArtistSectionVm(string Title, ObservableCollection<TrackItem> Items, SectionLayout Layout = SectionLayout.Cards)
{
    /// <summary>Wie viele Zeilen ein Zeilen-Bereich höchstens zeigt (vier Reihen mit je zwei Zeilen).</summary>
    public const int MaxRows = 8;

    /// <summary>Der Bereich wird als Zeilen gezeigt.</summary>
    public bool IsRows => Layout == SectionLayout.Rows;

    /// <summary>Der Bereich wird als Karten gezeigt.</summary>
    public bool IsCards => Layout != SectionLayout.Rows;

    /// <summary>Die ersten Zeilen eines Zeilen-Bereichs.</summary>
    public IEnumerable<TrackItem> RowItems => Items.Take(MaxRows);
}

/// <summary>Eine Stimmung (Chip) auf der Startseite. <see cref="Selected"/> ist wahr, wenn die Startseite gerade danach gefiltert ist.</summary>
public sealed record HomeChipVm(string Title, string Params, bool Selected);
