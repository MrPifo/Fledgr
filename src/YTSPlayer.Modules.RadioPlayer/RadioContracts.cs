namespace YTSPlayer.Modules.RadioPlayer;

/// <summary>Ein Radio-Sender.</summary>
public sealed record RadioStation(string Id, string Name, string StreamUrl, string Country = "", string Genre = "", string? LogoUrl = null);

/// <summary>Fortschritt beim Abgleich der Sender-Datenbank.</summary>
public sealed record RadioSyncProgress(int Done, int Total, string Step);

/// <summary>Durchsuchbarer Sender-Katalog (lokal gespeichert).</summary>
public interface IRadioCatalog
{
    Task<IReadOnlyList<RadioStation>> SearchAsync(string query, string? country, string? genre, CancellationToken ct = default);
    Task<IReadOnlyList<string>> CountriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GenresAsync(CancellationToken ct = default);
}

/// <summary>Gleicht die lokale Sender-Datenbank mit einer Online-Quelle ab.</summary>
public interface IRadioSync
{
    DateTime? LastSync { get; }

    /// <summary>Lädt die Sender neu. Meldet Fortschritt und lässt sich abbrechen.</summary>
    Task SyncAsync(IProgress<RadioSyncProgress>? progress, CancellationToken ct = default);
}
