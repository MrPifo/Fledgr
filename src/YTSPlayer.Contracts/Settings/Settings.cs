using System.Text.Json;

namespace YTSPlayer.Contracts.Settings;

/// <summary>Speicherorte der App. Jede Plattform gibt eigene Ordner vor.</summary>
public interface IAppPaths
{
    /// <summary>Einstellungen und Listen (Roaming).</summary>
    string Data { get; }

    /// <summary>Caches und Browser-Profile (Lokal).</summary>
    string Cache { get; }

    /// <summary>Ordner für Exporte (z. B. CSV).</summary>
    string Export { get; }
}

/// <summary>Einfache Umsetzung von <see cref="IAppPaths"/> mit festen Ordnern.</summary>
public sealed class AppPaths(string data, string cache, string export) : IAppPaths
{
    public string Data { get; } = data;
    public string Cache { get; } = cache;
    public string Export { get; } = export;
}

/// <summary>Gemerkte Lage eines Fensters. Position in Bildschirm-Pixeln, Größe in Fenster-Einheiten.</summary>
public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>Allgemeine Einstellungen der App (ohne Modul-Details).</summary>
public sealed class AppSettings
{
    /// <summary>Lage des Hauptfensters beim letzten Beenden (null = noch nie gemerkt, dann in der Mitte).</summary>
    public WindowPlacement? MainWindow { get; set; }

    /// <summary>Position des Widgets beim letzten Beenden.</summary>
    public WindowPlacement? Widget { get; set; }

    /// <summary>
    /// Sleep-Timer: Ist er an und gab es länger als <see cref="SleepTimerMinutes"/> Minuten keine Bedienung, wird die Wiedergabe pausiert.
    /// Gilt für alle Quellen (lokal und YouTube).
    /// </summary>
    public bool SleepTimerEnabled { get; set; } = true;

    public int SleepTimerMinutes { get; set; } = 30;

    /// <summary>Ein/Aus je Plugin-Id. Fehlt ein Eintrag, gilt der Standard des Plugins.</summary>
    public Dictionary<string, bool> Plugins { get; set; } = [];

    public double Volume { get; set; } = 1;

    /// <summary>Reglerstellung je Quelle (Quelle-Id → 0–1). Lokale Musik und YouTube sind unterschiedlich laut, jede Quelle behält ihre eigene Einstellung.</summary>
    public Dictionary<string, double> SourceVolumes { get; set; } = [];

    /// <summary>Zufallswiedergabe je Quelle (Quelle-Id → an/aus).</summary>
    public Dictionary<string, bool> SourceShuffle { get; set; } = [];
    public int TargetMb { get; set; } = 300;
    public bool MinimizeOnClose { get; set; }

    /// <summary>Quelle, die zuletzt gespielt hat. Der Play-Knopf nimmt sie nach einem Neustart wieder auf.</summary>
    public string? LastSourceId { get; set; }
}

/// <summary>Der Titel, der beim Beenden aktiv war (Teil von <see cref="SessionState"/>).</summary>
public sealed class SessionTrack
{
    public string SourceId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string? CoverUrl { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool WasPlaying { get; set; }
}

/// <summary>Stand beim letzten Beenden (Abschnitt „session“): offene Seite und zuletzt gespielter Titel mit Position.</summary>
public sealed class SessionState
{
    public string? PageId { get; set; }
    public SessionTrack? Track { get; set; }

    /// <summary>Warteschlange der Quelle des zuletzt gespielten Titels (nur Quellen mit eigener Warteschlange, begrenzt).</summary>
    public List<SessionQueueEntry> Queue { get; set; } = [];
}

/// <summary>Ein Eintrag der gemerkten Warteschlange.</summary>
public sealed class SessionQueueEntry
{
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
}

/// <summary>Speichert Einstellungen als JSON, ein Abschnitt pro Datei.</summary>
public interface ISettingsStore
{
    T Load<T>(string section) where T : class, new();
    void Save<T>(string section, T value) where T : class;
}

/// <summary>JSON-Datei je Abschnitt im Daten-Ordner. Im Nur-Lesen-Modus (Selbsttests) wird nichts geschrieben.</summary>
public sealed class JsonSettingsStore(IAppPaths paths, bool readOnly = false) : ISettingsStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    string FileFor(string section) => Path.Combine(paths.Data, section + ".json");

    public T Load<T>(string section) where T : class, new()
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(FileFor(section)), Options) ?? new T(); }
        catch { return new T(); }
    }

    public void Save<T>(string section, T value) where T : class
    {
        if (readOnly) return;
        try
        {
            Directory.CreateDirectory(paths.Data);
            File.WriteAllText(FileFor(section), JsonSerializer.Serialize(value, Options));
        }
        catch { }
    }
}
