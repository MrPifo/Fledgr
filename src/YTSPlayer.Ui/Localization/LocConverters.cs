using Avalonia.Data.Converters;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Ui.Localization;

/// <summary>Umwandler für Bindungen auf Schlüssel, die erst zur Laufzeit feststehen (z. B. der Titel einer Navigations-Seite).</summary>
public static class LocConverters
{
    /// <summary>Übersetzungs-Schlüssel → Text. Ein leerer Schlüssel ergibt einen leeren Text.</summary>
    public static readonly IValueConverter Key = new FuncValueConverter<string?, string>(key => string.IsNullOrEmpty(key) ? "" : Loc.Current[key]);

    /// <summary>Wie <see cref="Key"/>, nimmt aber die Kurzform <c>schlüssel.short</c>, wenn es sie gibt (für die schmale Schiene).</summary>
    public static readonly IValueConverter ShortKey = new FuncValueConverter<string?, string>(key =>
    {
        if (string.IsNullOrEmpty(key)) return "";
        var shortKey = key + ".short";
        var text = Loc.Current[shortKey];
        return text != shortKey ? text : Loc.Current[key];
    });
}
