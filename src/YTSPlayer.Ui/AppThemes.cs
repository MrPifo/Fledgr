using Avalonia.Styling;

namespace YTSPlayer.Ui;

/// <summary>
/// Alle Theme-Varianten der App. Jede Variante hat eine Datei in Theme/Themes und einen Eintrag in Palette.axaml.
/// Alle Varianten erben von Dunkel, damit eingebaute Fluent-Controls zu den Farben passen.
/// </summary>
public static class AppThemes
{
    /// <summary>Warmes Graphit mit Bernstein (Design v13).</summary>
    public static readonly ThemeVariant Graphit = new("Graphit", ThemeVariant.Dark);

    /// <summary>Alle bekannten Varianten, Name → Variante.</summary>
    public static readonly IReadOnlyDictionary<string, ThemeVariant> All = new Dictionary<string, ThemeVariant>(StringComparer.OrdinalIgnoreCase)
    {
        ["Graphit"] = Graphit,
    };

    /// <summary>Variante zum Namen, bei unbekanntem Namen das Standard-Theme.</summary>
    public static ThemeVariant Get(string? name) => name != null && All.TryGetValue(name, out var v) ? v : Graphit;
}
