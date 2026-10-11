namespace YTSPlayer.Modules.RadioPlayer.ViewModels;

/// <summary>Was im Radio zuletzt offen war (Abschnitt „radio-view“): Suchtext, Genre und Land.</summary>
public sealed class RadioViewState
{
    public string SearchText { get; set; } = "";
    public string Genre { get; set; } = "";
    public string CountryCode { get; set; } = "";
}
