using System.Text.Json;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>Eine Stimmung (Chip) oben auf der Startseite. <paramref name="Params"/> ist der Wert, mit dem die Seite die gefilterte Startseite lädt.</summary>
public sealed record HomeChip(string Title, string Params, bool Selected);

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
                    // Besteht der Bereich nur aus Titel-Zeilen („Schnell wieder einsteigen“), wird er als Zeilen gezeigt, sonst als Karten.
                    var rows = shelf.TryGetProperty("contents", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0
                               && items.EnumerateArray().All(i => i.TryGetProperty("musicResponsiveListItemRenderer", out _));
                    if (cards.Count > 0) sections.Add(new ArtistSectionData(title, cards, rows ? SectionLayout.Rows : SectionLayout.Cards));
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

    /// <summary>
    /// Die Stimmungen oben auf der Startseite (<c>chipCloudRenderer</c>), leer wenn es keine gibt.
    /// Aufbau aus der echten Seite abgeleitet, nicht gegen jedes Konto geprüft.
    /// </summary>
    public static List<HomeChip> ParseChips(string json)
    {
        var chips = new List<HomeChip>();
        try
        {
            using var d = JsonDocument.Parse(json);
            if (YtLibrary.FindProperty(d.RootElement, "chipCloudRenderer") is not { } cloud
                || !cloud.TryGetProperty("chips", out var list) || list.ValueKind != JsonValueKind.Array) return chips;
            foreach (var c in list.EnumerateArray())
            {
                if (!c.TryGetProperty("chipCloudChipRenderer", out var chip)) continue;
                var title = chip.TryGetProperty("text", out var t) ? Join(t) : "";
                var param = YtLibrary.FindProperty(chip, "browseEndpoint") is { } be && be.TryGetProperty("params", out var p) ? p.GetString() ?? "" : "";
                var selected = chip.TryGetProperty("isSelected", out var sel) && sel.ValueKind == JsonValueKind.True;
                if (title.Length > 0 && param.Length > 0) chips.Add(new HomeChip(title, param, selected));
            }
        }
        catch (JsonException) { }
        return chips;
    }

    static string Join(JsonElement t)
    {
        if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("simpleText", out var s)) return s.GetString() ?? "";
        if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            return string.Concat(runs.EnumerateArray().Select(x => x.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "")).Trim();
        return "";
    }
}
