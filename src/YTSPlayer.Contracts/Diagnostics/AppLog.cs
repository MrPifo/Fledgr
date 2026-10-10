using System.Text;

namespace YTSPlayer.Contracts.Diagnostics;

/// <summary>
/// Einfache Logdatei (eine Datei pro Tag, höchstens 2 MB je Datei, Dateien älter als 14 Tage werden gelöscht).
/// Geloggt werden Start, unbehandelte Fehler, fehlgeschlagene Operationen und Antworten von YouTube mit Fehlerstatus.
/// Nie loggen: Passwörter, Cookies, Tokens, vollständige Adressen mit Parametern.
/// Vor <see cref="Init"/> (und in Selbsttests) passiert nichts.
/// </summary>
public static class AppLog
{
    const long MaxBytes = 2 * 1024 * 1024;
    const int KeepDays = 14;
    static readonly object Gate = new();
    static string? _dir;

    /// <summary>Ordner der Logdateien oder null, solange das Logbuch nicht eingerichtet ist.</summary>
    public static string? Directory => _dir;

    /// <summary>Richtet das Logbuch ein und räumt alte Dateien weg.</summary>
    public static void Init(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            _dir = dir;
            foreach (var f in System.IO.Directory.EnumerateFiles(dir, "fledgr-*.log*"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays)) File.Delete(f);
        }
        catch
        {
            _dir = null;
        }
    }

    public static void Info(string area, string message) => Write("INFO ", area, message, null);

    public static void Warn(string area, string message, Exception? ex = null) => Write("WARN ", area, message, ex);

    public static void Error(string area, string message, Exception? ex = null) => Write("FEHLER", area, message, ex);

    static void Write(string level, string area, string message, Exception? ex)
    {
        var dir = _dir;
        if (dir == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(level).Append("] ").Append(area).Append(": ").Append(message);
            if (ex != null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine().Append(ex.StackTrace);
            sb.AppendLine();
            lock (Gate)
            {
                var path = Path.Combine(dir, $"fledgr-{DateTime.Now:yyyyMMdd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Das Logbuch darf nie selbst zum Fehler werden.
        }
    }
}
