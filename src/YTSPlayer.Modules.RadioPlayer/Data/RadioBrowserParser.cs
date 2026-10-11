using System.Text.Json;

namespace YTSPlayer.Modules.RadioPlayer.Data;

/// <summary>Liest die Antworten von radio-browser.info. Nur die Felder, die die App braucht. Fehlerhafte Einträge fallen weg.</summary>
public static class RadioBrowserParser
{
    public static IReadOnlyList<RadioStation> ParseStations(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<RadioStation>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = Text(el, "stationuuid");
            var name = Text(el, "name");
            // Die aufgelöste Adresse ist meist die richtige. Ohne sie die Original-Adresse.
            var url = Text(el, "url_resolved");
            if (url.Length == 0) url = Text(el, "url");
            if (id.Length == 0 || name.Length == 0 || !RadioUrls.IsHttp(url)) continue;
            var logo = Text(el, "favicon");
            list.Add(new RadioStation(id, name, url, Text(el, "country"), Text(el, "tags"), Text(el, "codec"), Number(el, "bitrate"), RadioUrls.IsHttp(logo) ? logo : null));
        }
        return list;
    }

    public static IReadOnlyList<RadioCountry> ParseCountries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(el => new RadioCountry(Text(el, "name"), Text(el, "iso_3166_1"), Number(el, "stationcount")))
            .Where(c => c.Name.Length > 0 && c.Code.Length == 2)
            .ToList();
    }

    /// <summary>Die Servernamen (z. B. „de1.api.radio-browser.info“) ohne Dubletten.</summary>
    public static IReadOnlyList<string> ParseServers(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(el => Text(el, "name"))
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
    }

    static string Text(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : "";

    static int Number(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt32(out var n) ? n : 0;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s) ? s : 0;
    }
}
