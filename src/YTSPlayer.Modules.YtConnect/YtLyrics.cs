using System.Text.Json;
using YTSPlayer.Contracts.Playback;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Songtexte von YouTube Music. Zwei Schritte: Die Antwort „next“ zum Titel nennt in ihren Reitern die Kennung des Songtext-Reiters (MPLY…),
/// danach liefert „browse“ mit dieser Kennung den Text. Hat der Titel keinen Songtext, gibt es keinen solchen Reiter.
/// </summary>
public static class YtLyrics
{
    /// <summary>Die Kennung des Songtext-Reiters aus der Antwort „next“ oder null.</summary>
    public static string? FindBrowseId(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return Walk(d.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "browseId" && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } id && id.StartsWith("MPLY", StringComparison.Ordinal)) return id;
                    if (Walk(p.Value) is { } r) return r;
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in e.EnumerateArray()) if (Walk(i) is { } r) return r;
            }
            return null;
        }
    }

    /// <summary>Den Text und die Quellenangabe aus der Antwort „browse“ des Songtext-Reiters lesen. Null, wenn kein Text da ist.</summary>
    public static LyricsResult? Parse(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (YtLibrary.FindProperty(d.RootElement, "musicDescriptionShelfRenderer") is not { } shelf) return null;
            var text = Join(shelf, "description");
            if (text.Trim().Length == 0) return null;
            var source = Join(shelf, "footer");
            return new LyricsResult(text.Trim(), source.Length > 0 ? source : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string Join(JsonElement e, string property)
    {
        if (!e.TryGetProperty(property, out var t)) return "";
        if (t.TryGetProperty("simpleText", out var s)) return s.GetString() ?? "";
        return t.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array
            ? string.Concat(runs.EnumerateArray().Select(x => x.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "")) : "";
    }
}
