using System.Text.Json;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Liest die Startseite von YouTube Music (InnerTube <c>browse</c> mit „FEmusic_home“) und deren Fortsetzungen:
/// Bereiche mit Karten (Mixes, Alben, Künstler, Playlists) oder Titeln („Schnelle Auswahl“, „Zuletzt gehört“).
/// Aufbau aus der echten Seite abgeleitet, nicht gegen jedes Konto geprüft.
/// </summary>
public static class YtHomeParser
{
    /// <summary>Gibt die Bereiche und, wenn es weitere gibt, das Zeichen für die nächste Seite zurück.</summary>
    public static (List<ArtistSectionData> Sections, string? Next) Parse(string json)
    {
        var sections = new List<ArtistSectionData>();
        string? next = null;
        try
        {
            using var d = JsonDocument.Parse(json);
            var list = YtLibrary.FindProperty(d.RootElement, "sectionListRenderer") ?? YtLibrary.FindProperty(d.RootElement, "sectionListContinuation");
            if (list is not { } l) return (sections, null);

            if (l.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
                foreach (var block in contents.EnumerateArray())
                {
                    JsonElement shelf;
                    if (block.TryGetProperty("musicCarouselShelfRenderer", out var c)) shelf = c;
                    else if (block.TryGetProperty("musicImmersiveCarouselShelfRenderer", out var ic)) shelf = ic;
                    else if (block.TryGetProperty("musicShelfRenderer", out var s)) shelf = s;
                    else continue;

                    var title = shelf.TryGetProperty("header", out var h) && YtLibrary.FindProperty(h, "title") is { } t ? Join(t)
                        : shelf.TryGetProperty("title", out var t2) ? Join(t2) : "";
                    var cards = YtArtistParser.Cards(shelf);
                    if (cards.Count > 0) sections.Add(new ArtistSectionData(title, cards));
                }

            // Weitere Bereiche: das Zeichen steht in „continuations“ des Listen-Elements.
            if (l.TryGetProperty("continuations", out var conts) && conts.ValueKind == JsonValueKind.Array && conts.GetArrayLength() > 0)
            {
                var c0 = conts[0];
                if (c0.TryGetProperty("nextContinuationData", out var n) && n.TryGetProperty("continuation", out var tk)) next = tk.GetString();
                else if (c0.TryGetProperty("reloadContinuationData", out var r) && r.TryGetProperty("continuation", out var tk2)) next = tk2.GetString();
            }
        }
        catch (JsonException)
        {
            return ([], null);
        }
        return (sections, next);
    }

    static string Join(JsonElement t)
    {
        if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("simpleText", out var s)) return s.GetString() ?? "";
        if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            return string.Concat(runs.EnumerateArray().Select(x => x.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "")).Trim();
        return "";
    }
}
