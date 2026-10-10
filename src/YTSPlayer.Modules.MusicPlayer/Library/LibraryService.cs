using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Modules.MusicPlayer.Library;

/// <summary>Eine Audio-Datei im Index. <see cref="Lower"/> ist der Suchtext in Kleinbuchstaben (Pfad, bei gelesenen Tags auch Titel, Interpret, Album).</summary>
public sealed record LibraryEntry(string Path, string Name, string Relative, string Root, string Lower, string Title = "", string Artist = "", string Album = "", int TrackNo = 0);

/// <summary>Fortschritt eines Scans. Mit <paramref name="Tags"/> läuft gerade das Lesen der Tags (<paramref name="Total"/> = Gesamtzahl).</summary>
public sealed record ScanProgress(int Files, string Current, int Total = 0, bool Tags = false);

/// <summary>
/// Hält die Ordner-Liste, den Datei-Index und die Playlists. Scan, Suche und Ordner-Anzeige laufen im Hintergrund.
/// </summary>
public sealed class LibraryService(MusicSettings settings, ISettingsStore store, IPerfTracker perf)
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".wma", ".aif", ".aiff" };

    static readonly EnumerationOptions Recurse = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
    static readonly EnumerationOptions Flat = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System | FileAttributes.Hidden };

    const int TagCacheVersion = 2;

    IReadOnlyList<LibraryEntry> _entries = [];
    int _scanning;

    public MusicSettings Settings => settings;
    public int FileCount => _entries.Count;
    public bool IsScanning => _scanning != 0;

    /// <summary>Index oder Ordner-Liste haben sich geändert.</summary>
    public event Action? Changed;

    public static bool IsAudio(string path) => Extensions.Contains(Path.GetExtension(path));

    public static MediaItem ToItem(string path) => new("local", path, Path.GetFileNameWithoutExtension(path));

    public void Save() => store.Save("music", settings);

    /// <summary>Der aktuelle Index (nur lesen, wird bei Änderungen als Ganzes ersetzt).</summary>
    public IReadOnlyList<LibraryEntry> Entries => _entries;

    public void AddFolder(string path)
    {
        path = Path.GetFullPath(path);
        if (settings.Folders.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        settings.Folders.Add(path);
        Save();
        Changed?.Invoke();
        StartWatching();
    }

    public void RemoveFolder(string path)
    {
        settings.Folders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        _entries = _entries.Where(e => !string.Equals(e.Root, path, StringComparison.OrdinalIgnoreCase)).ToList();
        Save();
        Changed?.Invoke();
        StartWatching();
    }

    // ---- Ordner beobachten ----

    readonly List<FileSystemWatcher> _watchers = [];
    readonly HashSet<string> _pending = [];
    Timer? _debounce;

    /// <summary>Änderungen an den Dateien der beobachteten Ordner (neu, gelöscht, umbenannt) sind in den Index eingearbeitet. Kommt auf einem Hintergrund-Thread.</summary>
    public event Action? FilesChanged;

    /// <summary>Beobachtet alle Bibliotheks-Ordner. Neue, gelöschte und umbenannte Dateien erscheinen ohne erneuten Scan (nach etwa 2 Sekunden Ruhe).</summary>
    public void StartWatching()
    {
        StopWatching();
        foreach (var folder in settings.Folders.ToList())
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                w.Created += (_, e) => OnFileEvent(e.FullPath);
                w.Deleted += (_, e) => OnFileEvent(e.FullPath);
                w.Changed += (_, e) => OnFileEvent(e.FullPath);
                w.Renamed += (_, e) => { OnFileEvent(e.OldFullPath); OnFileEvent(e.FullPath); };
                w.Error += (_, e) => AppLog.Warn("Bibliothek", "Ordner-Beobachtung gestört", e.GetException());
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Bibliothek", "Ordner konnte nicht beobachtet werden", ex);
            }
        }
    }

    public void StopWatching()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _debounce?.Dispose();
    }

    void OnFileEvent(string path)
    {
        // Datei-Änderungen außerhalb von Audio und Ordnern ignorieren. Ordner (ohne Endung) und Audio zählen.
        if (Path.HasExtension(path) && !IsAudio(path)) return;
        lock (_pending) _pending.Add(path);
        _debounce?.Dispose();
        _debounce = new Timer(_ => ApplyPending(), null, 2000, Timeout.Infinite);
    }

    void ApplyPending()
    {
        try
        {
            string[] paths;
            lock (_pending) { paths = [.. _pending]; _pending.Clear(); }
            if (paths.Length == 0 || IsScanning) { if (paths.Length > 0) foreach (var p in paths) lock (_pending) _pending.Add(p); return; }
            var entries = _entries.ToList();
            var changed = false;
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.EnumerateFiles(p, "*", Recurse).Where(IsAudio)) changed |= Upsert(entries, f);
                }
                else if (File.Exists(p))
                {
                    if (IsAudio(p)) changed |= Upsert(entries, p);
                }
                else
                {
                    // Gelöscht oder weggezogen: die Datei selbst oder alles darunter (Ordner) entfernen.
                    var prefix = p.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    changed |= entries.RemoveAll(e => string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase)
                        || e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) > 0;
                }
            }
            if (!changed) return;
            _entries = entries;
            AppLog.Info("Bibliothek", $"Ordner-Änderung eingearbeitet, {entries.Count} Dateien im Index");
            FilesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Bibliothek", "Ordner-Änderung konnte nicht eingearbeitet werden", ex);
        }
    }

    /// <summary>Fügt eine Datei dem Index hinzu oder ersetzt ihren Eintrag (mit neu gelesenen Tags). Gibt zurück, ob sich etwas geändert hat.</summary>
    bool Upsert(List<LibraryEntry> entries, string file)
    {
        var root = settings.Folders.FirstOrDefault(r => file.StartsWith(r.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (root == null) return false;
        var rel = Path.GetRelativePath(root, file);
        var entry = new LibraryEntry(file, Path.GetFileNameWithoutExtension(file), rel, root, rel.ToLowerInvariant());
        if (settings.SearchTags)
        {
            long m;
            try { m = File.GetLastWriteTimeUtc(file).Ticks; } catch { m = 0; }
            var t = ReadTag(file, m);
            entry = entry with { Lower = (rel + " " + t.T + " " + t.A + " " + t.B).ToLowerInvariant(), Title = t.T, Artist = t.A, Album = t.B, TrackNo = t.N };
        }
        var i = entries.FindIndex(e => string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            if (entries[i] == entry) return false;
            entries[i] = entry;
        }
        else entries.Add(entry);
        return true;
    }

    /// <summary>Liest alle Ordner neu ein. Läuft im Hintergrund, meldet Fortschritt und lässt sich abbrechen.</summary>
    public async Task ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _scanning, 1) != 0) return;
        using var scope = perf.Begin("Library.Scan");
        try
        {
            var folders = settings.Folders.ToList();
            var result = await Task.Run(() => Scan(folders, progress, ct), ct);
            _entries = result;
            Changed?.Invoke();
            // Zweiter Schritt: Tags lesen, damit die Suche auch Titel, Interpret und Album kennt. Die Suche läuft währenddessen schon mit dem Pfad.
            if (settings.SearchTags)
            {
                var withTags = await Task.Run(() => ReadTags(result, progress, ct), ct);
                _entries = withTags;
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            scope.Fail();
        }
        catch
        {
            scope.Fail();
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    static List<LibraryEntry> Scan(List<string> folders, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var list = new List<LibraryEntry>();
        foreach (var root in folders)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", Recurse))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsAudio(file)) continue;
                var rel = Path.GetRelativePath(root, file);
                list.Add(new LibraryEntry(file, Path.GetFileNameWithoutExtension(file), rel, root, (rel).ToLowerInvariant()));
                if (list.Count % 250 == 0) progress?.Report(new ScanProgress(list.Count, rel));
            }
        }
        progress?.Report(new ScanProgress(list.Count, ""));
        return list;
    }

    /// <summary>
    /// Liest Titel, Interpret und Album jeder Datei (TagLib, ohne Cover). Bekannte, unveränderte Dateien kommen aus dem Speicher
    /// (Abschnitt „music-tags“). Läuft mit 2 Threads, damit die Festplatte und die Oberfläche nicht leiden.
    /// </summary>
    List<LibraryEntry> ReadTags(IReadOnlyList<LibraryEntry> entries, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        using var scope = perf.Begin("Library.Tags");
        var cache = store.Load<TagCache>("music-tags");
        if (cache.Version < TagCacheVersion) cache = new TagCache();
        var fresh = new Dictionary<string, TagEntry>(entries.Count);
        var result = new LibraryEntry[entries.Count];
        var done = 0;
        Parallel.For(0, entries.Count, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, i =>
        {
            var e = entries[i];
            long m;
            try { m = File.GetLastWriteTimeUtc(e.Path).Ticks; } catch { m = 0; }
            TagEntry? t;
            lock (cache) cache.Items.TryGetValue(e.Path, out t);
            if (t == null || t.M != m) t = ReadTag(e.Path, m);
            result[i] = e with { Lower = (e.Relative + " " + t.T + " " + t.A + " " + t.B).ToLowerInvariant(), Title = t.T, Artist = t.A, Album = t.B, TrackNo = t.N };
            lock (fresh) fresh[e.Path] = t;
            var n = Interlocked.Increment(ref done);
            if (n % 200 == 0) progress?.Report(new ScanProgress(n, "", entries.Count, true));
        });
        progress?.Report(new ScanProgress(entries.Count, "", entries.Count, true));
        store.Save("music-tags", new TagCache { Version = TagCacheVersion, Items = fresh });
        return [.. result];
    }

    static TagEntry ReadTag(string path, long modified)
    {
        try
        {
            using var f = TagLib.File.Create(path, TagLib.ReadStyle.None);
            var artist = f.Tag.FirstPerformer ?? f.Tag.FirstAlbumArtist ?? "";
            return new TagEntry { M = modified, T = f.Tag.Title ?? "", A = artist, B = f.Tag.Album ?? "", N = (int)Math.Min(f.Tag.Track, 9999) };
        }
        catch
        {
            // Defekte oder ungewöhnliche Datei: ohne Tags weiter, die Suche nutzt dann nur den Pfad.
            return new TagEntry { M = modified };
        }
    }

    /// <summary>Sucht in Dateiname und Ordnerpfad (und, wenn gelesen, in Titel, Interpret und Album). Alle Wörter müssen vorkommen. Gibt höchstens <paramref name="max"/> Treffer zurück.</summary>
    public Task<IReadOnlyList<LibraryEntry>> SearchAsync(string query, int max, CancellationToken ct)
    {
        var entries = _entries;
        return Task.Run<IReadOnlyList<LibraryEntry>>(() =>
        {
            using var scope = perf.Begin("Library.Search");
            var words = query.ToLowerInvariant().Split([' ', '\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return [];
            var hits = new List<LibraryEntry>();
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                var ok = true;
                foreach (var w in words)
                    if (!e.Lower.Contains(w, StringComparison.Ordinal)) { ok = false; break; }
                if (!ok) continue;
                hits.Add(e);
                if (hits.Count >= max) break;
            }
            return hits;
        }, ct);
    }

    /// <summary>Unterordner und Audio-Dateien eines Ordners (Ordner zuerst, jeweils sortiert).</summary>
    public Task<(List<string> Folders, List<string> Files)> BrowseAsync(string dir, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var scope = perf.Begin("Library.Browse");
            var dirs = Directory.EnumerateDirectories(dir, "*", Flat).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
            var files = Directory.EnumerateFiles(dir, "*", Flat).Where(IsAudio).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            ct.ThrowIfCancellationRequested();
            return (dirs, files);
        }, ct);

    public bool IsFavorite(string path) => settings.Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void ToggleFavorite(string path)
    {
        if (IsFavorite(path)) settings.Favorites.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        else settings.Favorites.Add(path);
        Save();
        Changed?.Invoke();
    }

    public PlaylistDef CreatePlaylist(string name)
    {
        var p = new PlaylistDef { Name = name.Trim() };
        settings.Playlists.Add(p);
        Save();
        Changed?.Invoke();
        return p;
    }

    public void DeletePlaylist(PlaylistDef p)
    {
        settings.Playlists.Remove(p);
        Save();
        Changed?.Invoke();
    }

    public void AddToPlaylist(PlaylistDef p, string path)
    {
        if (p.Paths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        p.Paths.Add(path);
        Save();
        Changed?.Invoke();
    }

    public void RemoveFromPlaylist(PlaylistDef p, string path)
    {
        p.Paths.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        Save();
        Changed?.Invoke();
    }
}
