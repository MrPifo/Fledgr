using System.Text;

namespace YTSPlayer.Modules.RadioPlayer.Data;

/// <summary>Länderliste für die Auswahl: ein Eintrag je Land, nach Name sortiert. Die Suche ignoriert Groß-/Kleinschreibung und Akzente.</summary>
public static class RadioCountries
{
    /// <summary>Sortiert, legt Codes zusammen (z. B. „gr“ und „GR“) und lässt ungültige Einträge weg.</summary>
    public static IReadOnlyList<RadioCountry> Prepare(IEnumerable<RadioCountry> raw) =>
        raw.Where(c => c.Name.Length > 0 && c.Code.Length == 2)
            .Select(c => c with { Code = c.Code.ToUpperInvariant() })
            .GroupBy(c => c.Code)
            .Select(g => g.OrderByDescending(c => c.Count).First())
            .OrderBy(c => Fold(c.Name), StringComparer.Ordinal)
            .ToList();

    /// <summary>Passt das Land zur Suche? Ein Eintrag ohne Code (Alle Länder) passt immer.</summary>
    public static bool Matches(RadioCountry country, string filter) =>
        country.Code.Length == 0 || Fold(country.Name).Contains(Fold(filter), StringComparison.Ordinal);

    // Akzent-Buchstaben und ihre einfache Form. Ohne Zerlegung per Normalize, weil die App ohne ICU-Daten läuft.
    const string AccentedChars = "ÀÁÂÃÄÅàáâãäåÇçÈÉÊËèéêëÌÍÎÏìíîïÑñÒÓÔÕÖØòóôõöøÙÚÛÜùúûüÝýÿŞşĞğİıŠšŽžČčĆćĐđŁłŃńŚśŹźŻż";
    const string PlainChars = "AAAAAAaaaaaaCcEEEEeeeeIIIIiiiiNnOOOOOOooooooUUUUuuuuYyySsGgIiSsZzCcCcDdLlNnSsZzZz";
    static readonly Dictionary<char, char> Plain = AccentedChars.Zip(PlainChars).ToDictionary(p => p.First, p => p.Second);

    /// <summary>Großbuchstaben ohne Akzente („Türkiye“ wird „TURKIYE“).</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text) sb.Append(Plain.TryGetValue(ch, out var plain) ? plain : ch);
        return sb.ToString().Trim().ToUpperInvariant();
    }
}
