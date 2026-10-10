using System.Text.Json;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Suchvorschläge für das Suchfeld. Kommen von der öffentlichen Vorschlags-Schnittstelle von YouTube (ohne Anmeldung, ohne die Web-Ansicht zu starten).
/// Gesendet wird nur der gerade getippte Text.
/// </summary>
public static class YtSuggest
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>Bis zu <paramref name="max"/> Vorschläge zum Text. Bei einem Fehler (kein Netz, Zeitüberschreitung) eine leere Liste.</summary>
    public static async Task<List<string>> GetAsync(string text, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        try
        {
            var url = "https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&hl=de&q=" + Uri.EscapeDataString(text);
            var json = await Http.GetStringAsync(url, ct);
            return Parse(json, max);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Antwort der Form <c>["text",["vorschlag 1","vorschlag 2"]]</c>.</summary>
    public static List<string> Parse(string json, int max)
    {
        var list = new List<string>();
        try
        {
            using var d = JsonDocument.Parse(json);
            var root = d.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2 || root[1].ValueKind != JsonValueKind.Array) return list;
            foreach (var s in root[1].EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } t) list.Add(t);
                if (list.Count >= max) break;
            }
        }
        catch (JsonException)
        {
            // Unbekanntes Format: keine Vorschläge.
        }
        return list;
    }
}
