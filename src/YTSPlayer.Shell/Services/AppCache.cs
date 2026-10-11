using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Der Cache der App: Vorschaubilder (Lokal\thumbs) und gespeicherte Listen, die sich neu laden lassen
/// (Startseite, Titellisten der YT-Playlists, Tag-Index der Musik). Login, Einstellungen, Playlists und Protokolle gehören nicht dazu.
/// </summary>
public static class AppCache
{
    static readonly string[] DataPrefixes = ["yt-home", "yt-pl-", "music-tags"];

    static IEnumerable<string> Files(IAppPaths paths)
    {
        var thumbs = Path.Combine(paths.Cache, "thumbs");
        if (Directory.Exists(thumbs))
            foreach (var f in Directory.EnumerateFiles(thumbs, "*", SearchOption.AllDirectories)) yield return f;
        if (Directory.Exists(paths.Data))
            foreach (var f in Directory.EnumerateFiles(paths.Data, "*.json"))
                if (DataPrefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.OrdinalIgnoreCase))) yield return f;
    }

    /// <summary>Gesamtgröße des Caches in Bytes.</summary>
    public static long Measure(IAppPaths paths)
    {
        long total = 0;
        foreach (var f in Files(paths))
        {
            try { total += new FileInfo(f).Length; }
            catch (IOException) { }
        }
        return total;
    }

    /// <summary>Löscht den Cache. Gesperrte Dateien bleiben stehen. Gibt die Bytes zurück, die frei wurden.</summary>
    public static long Clear(IAppPaths paths)
    {
        long freed = 0;
        foreach (var f in Files(paths).ToList())
        {
            try
            {
                var size = new FileInfo(f).Length;
                File.Delete(f);
                freed += size;
            }
            catch (Exception) { }
        }
        return freed;
    }
}
