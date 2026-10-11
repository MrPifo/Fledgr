namespace YTSPlayer.Modules.RadioPlayer;

/// <summary>Ein Sender aus der Datenbank oder eine per Hand eingegebene Adresse.</summary>
public sealed record RadioStation(string Id, string Name, string StreamUrl, string Country = "", string Tags = "", string Codec = "", int Bitrate = 0, string? LogoUrl = null)
{
    /// <summary>Technik in einer Zeile, z. B. „MP3 · 128 kBit/s“.</summary>
    public string TechnicalInfo => string.Join(" · ", new[] { Codec, Bitrate > 0 ? $"{Bitrate} kBit/s" : "" }.Where(x => x.Length > 0));
}

/// <summary>Land aus der Datenbank mit der Anzahl der Sender.</summary>
public sealed record RadioCountry(string Name, string Code, int Count);

/// <summary>Zustand eines gemerkten Senders. Wird geprüft, wenn ein Stream nicht läuft.</summary>
public enum StationStatus
{
    Ok,

    /// <summary>Der Sender ist noch in der Datenbank, der Stream antwortet aber nicht.</summary>
    Dead,

    /// <summary>Der Sender ist nicht mehr in der Datenbank.</summary>
    Gone,
}

/// <summary>Kennzeichen der Sender-Ids. Datenbank-Sender haben die Id „stationuuid“, Eingaben von Hand ein Präfix.</summary>
public static class RadioIds
{
    public const string ManualPrefix = "manual-";

    public static bool IsDatabase(string id) => !id.StartsWith(ManualPrefix, StringComparison.Ordinal);

    public static string NewManual() => ManualPrefix + Guid.NewGuid().ToString("N");
}

/// <summary>Prüft Adressen, die der Nutzer eingibt oder die aus der Datenbank kommen.</summary>
public static class RadioUrls
{
    public static bool IsHttp(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Zeigt die Adresse auf eine Playlist (.m3u oder .pls) statt auf einen Stream?</summary>
    public static bool IsPlaylist(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".pls", StringComparison.OrdinalIgnoreCase));
}
