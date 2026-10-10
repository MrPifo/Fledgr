using System.Text.Json;
using System.Text.RegularExpressions;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.YtConnect.Scripts;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Liest die Playlists und deren Titel aus der versteckten Seite und merkt sie sich. Die Anzeige kommt immer zuerst aus dem Speicher.
/// Prüfungen im Hintergrund laufen nur, wenn gerade nichts abgespielt wird.
/// </summary>
public sealed class YtLibrary(YtEngine engine, YtPlaybackSource source, ISettingsStore store, IPerfTracker perf)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    const int CacheVersion = 2;

    /// <summary>So lange gilt der Speicher als frisch. Davor wird im Hintergrund nicht erneut geprüft.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

    /// <summary>Zuletzt gespeicherte Playlists.</summary>
    public List<PlaylistInfo> LoadCached() => store.Load<YtPlaylistCache>("yt-playlists").Items;

    /// <summary>Wann die Playlists zuletzt erfolgreich geprüft wurden (UTC).</summary>
    public DateTime LastChecked => store.Load<YtPlaylistCache>("yt-playlists") is { Version: >= CacheVersion } c ? c.CheckedUtc : DateTime.MinValue;

    /// <summary>Wahr, solange die Web-Ansicht für die Wiedergabe gebraucht wird. Dann liest die App nichts im Hintergrund.</summary>
    public bool PlaybackBusy => source.InUse;

    /// <summary>Gespeicherte Titel einer Playlist.</summary>
    public List<TrackInfo> LoadCachedTracks(string playlistId) => store.Load<YtTracksCache>(TracksSection(playlistId)).Items;

    /// <summary>Wahr, wenn beide Listen dieselben Playlists in derselben Reihenfolge mit gleichem Titel und gleicher Info (Anzahl) haben.</summary>
    public static bool SameList(IReadOnlyList<PlaylistInfo> a, IReadOnlyList<PlaylistInfo> b)
    {
        // Die feste „Gefällt mir“-Zeile zählt nicht (die App legt sie selbst an).
        a = [.. a.Where(p => p.Id != LikedId)];
        b = [.. b.Where(p => p.Id != LikedId)];
        return SameLists(a, b);
    }

    /// <summary>Id der Playlist mit den gemochten Titeln.</summary>
    public const string LikedId = "LM";

    /// <summary>Text unter dem Namen der festen „Gefällt mir“-Zeile (daran erkennt man, dass die App sie selbst angelegt hat).</summary>
    public const string LikedSub = "Deine gemochten Titel";

    static bool SameLists(IReadOnlyList<PlaylistInfo> a, IReadOnlyList<PlaylistInfo> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id && p.First.Title == p.Second.Title && p.First.Sub == p.Second.Sub);

    /// <summary>Wahr, wenn beide Titel-Listen gleich sind (Reihenfolge, Id, Titel, Interpret, Eintrags-Id).</summary>
    public static bool SameTracks(IReadOnlyList<TrackInfo> a, IReadOnlyList<TrackInfo> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Id == p.Second.Id && p.First.Title == p.Second.Title && p.First.Artist == p.Second.Artist && p.First.SetId == p.Second.SetId && p.First.ArtistId == p.Second.ArtistId);

    readonly YtApi _api = new(engine);

    /// <summary>Playlists, die man nicht löschen kann (Gefällt-mir-Titel „LM“, Episoden „SE“).</summary>
    public static bool CanDelete(string playlistId) => playlistId is not ("LM" or "SE" or "");

    /// <summary>Speichert die Titelliste einer Playlist (z. B. nach dem Entfernen eines Titels).</summary>
    public void SaveCachedTracks(string playlistId, IEnumerable<TrackInfo> items)
    {
        store.Save(TracksSection(playlistId), new YtTracksCache { Items = [.. items], CheckedUtc = DateTime.UtcNow });
        source.QueueChanged(playlistId);
    }

    /// <summary>Speichert die Playlist-Liste (nach Löschen), die Prüfzeit bleibt.</summary>
    public void SaveCachedPlaylists(IEnumerable<PlaylistInfo> items) =>
        store.Save("yt-playlists", new YtPlaylistCache
        {
            Version = CacheVersion,
            Items = [.. items.Where(p => !(p.Id == LikedId && p.Sub == LikedSub))],
            CheckedUtc = LastChecked
        });

    /// <summary>Nach Änderungen auf YouTube: die gespeicherten Titel dieser Playlist verwerfen und die Playlist-Liste beim nächsten Start neu prüfen.</summary>
    void MarkStale(string playlistId)
    {
        store.Save(TracksSection(playlistId), new YtTracksCache());
        store.Save("yt-playlists", new YtPlaylistCache { Version = CacheVersion, Items = LoadCached(), CheckedUtc = DateTime.MinValue });
    }

    /// <summary>Fügt einen Titel einer Playlist hinzu. Gibt null bei Erfolg zurück, sonst einen Fehlertext. Die gespeicherte Liste wird gleich mit ergänzt.</summary>
    public Task<string?> AddTrackAsync(string playlistId, TrackInfo track) =>
        EditAsync(new { playlistId, actions = new[] { new { action = "ACTION_ADD_VIDEO", addedVideoId = track.Id } } },
            "Nicht hinzugefügt. Ist der Titel schon in der Playlist, oder gehört sie dir nicht?",
            () => AppendCachedAsync(playlistId, [WithThumb(track)]));

    /// <summary>Fügt alle Titel einer Playlist einer anderen hinzu.</summary>
    public Task<string?> AddPlaylistAsync(string targetId, string sourceId) =>
        EditAsync(new { playlistId = targetId, actions = new[] { new { action = "ACTION_ADD_PLAYLIST", addedFullListId = sourceId } } },
            "Nicht hinzugefügt. Gehört die Ziel-Playlist dir?",
            async () =>
            {
                var tracks = LoadCachedTracks(sourceId);
                if (tracks.Count > 0) await AppendCachedAsync(targetId, tracks);
                else
                {
                    // Die Titel der Quelle sind nicht gespeichert: die Ziel-Playlist ganz neu laden.
                    var fetched = await FetchTracksAsync(targetId, CancellationToken.None);
                    if (fetched.Count > 0) SaveCachedTracks(targetId, fetched);
                    else MarkStale(targetId);
                }
            });

    /// <summary>
    /// Hängt Titel an die gespeicherte Liste einer Playlist an (ohne Netzwerk). Ist noch nichts gespeichert, wird die ganze Liste
    /// jetzt über die Schnittstelle geholt (die Seite ist ja schon bereit), damit die Playlist beim Öffnen vollständig ist.
    /// </summary>
    async Task AppendCachedAsync(string playlistId, IReadOnlyList<TrackInfo> add)
    {
        var cache = store.Load<YtTracksCache>(TracksSection(playlistId));
        if (cache.Items.Count == 0)
        {
            var fetched = await FetchTracksAsync(playlistId, CancellationToken.None);
            if (fetched.Count == 0) return;
            foreach (var t in add) if (fetched.All(x => x.Id != t.Id)) fetched.Add(t);
            cache.Items = fetched;
        }
        else
        {
            foreach (var t in add) if (cache.Items.All(x => x.Id != t.Id)) cache.Items.Add(t);
        }
        cache.CheckedUtc = DateTime.UtcNow;
        store.Save(TracksSection(playlistId), cache);
        source.QueueChanged(playlistId);
    }

    /// <summary>Liest die Titel einer Playlist über die Schnittstelle (die Seite muss bereit sein). Kein Seitenwechsel, die Wiedergabe läuft weiter. Leer, wenn das nicht klappt.</summary>
    async Task<List<TrackInfo>> FetchTracksAsync(string playlistId, CancellationToken ct)
    {
        var all = new List<TrackInfo>();
        var seen = new HashSet<string>();
        string? token = null;
        try
        {
            for (var page = 0; page < 40; page++)
            {
                object body = token == null ? new { browseId = "VL" + playlistId } : new { continuation = token };
                var (status, text) = await _api.CallAsync("browse", body, ct);
                if (status != 200) break;
                var (items, next) = ParseTracks(text);
                foreach (var t in items) if (seen.Add(t.Id)) all.Add(WithThumb(t));
                if (next == null || items.Count == 0) break;
                token = next;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
        return all;
    }

    /// <summary>Wie <see cref="FetchTracksAsync"/>, bereitet aber die Seite vor und beendet die Web-Ansicht danach wieder, wenn sie nur dafür lief.</summary>
    async Task<List<TrackInfo>> TryFetchTracksAsync(string playlistId, CancellationToken ct)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return [];
            return await FetchTracksAsync(playlistId, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>
    /// Liest die Titel aus der Antwort einer Playlist-Seite (oder ihrer Fortsetzung). Gibt die Titel und, wenn es weitergeht,
    /// das Zeichen für die nächste Seite zurück. Nur der Bereich der Playlist selbst zählt, nicht die Vorschläge darunter.
    /// </summary>
    public static (List<TrackInfo> Items, string? Next) ParseTracks(string json)
    {
        var items = new List<TrackInfo>();
        string? next = null;
        try
        {
            using var d = JsonDocument.Parse(json);
            var scope = FindProperty(d.RootElement, "musicPlaylistShelfRenderer")
                        ?? FindProperty(d.RootElement, "musicPlaylistShelfContinuation")
                        ?? FindProperty(d.RootElement, "appendContinuationItemsAction")
                        ?? d.RootElement;
            Collect(scope);

            void Collect(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.Name == "musicResponsiveListItemRenderer") { if (ToTrack(p.Value) is { } t) items.Add(t); }
                        else if (p.Name == "continuationCommand" && p.Value.TryGetProperty("token", out var tk) && tk.GetString() is { Length: > 0 } s) next = s;
                        else Collect(p.Value);
                    }
                }
                else if (e.ValueKind == JsonValueKind.Array)
                {
                    foreach (var i in e.EnumerateArray()) Collect(i);
                }
            }
        }
        catch { return ([], null); }
        return (items, next);
    }

    internal static JsonElement? FindProperty(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == name) return p.Value;
                if (FindProperty(p.Value, name) is { } r) return r;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in e.EnumerateArray()) if (FindProperty(i, name) is { } r) return r;
        }
        return null;
    }

    internal static TrackInfo? ToTrack(JsonElement r)
    {
        if (!r.TryGetProperty("playlistItemData", out var pd) || !pd.TryGetProperty("videoId", out var v) || v.GetString() is not { Length: > 0 } id) return null;
        var setId = pd.TryGetProperty("playlistSetVideoId", out var s) ? s.GetString() ?? "" : "";
        string Column(string array, string item, int index) => string.Concat(Runs(r, array, item, index));
        var title = Column("flexColumns", "musicResponsiveListItemFlexColumnRenderer", 0);
        if (title.Length == 0) return null;
        var artist = Column("flexColumns", "musicResponsiveListItemFlexColumnRenderer", 1);
        var duration = Column("fixedColumns", "musicResponsiveListItemFixedColumnRenderer", 0);
        return new TrackInfo(id, title, artist, duration, ThumbOf(r), setId, "", "", ArtistIdOf(r));
    }

    /// <summary>Entfernt einen Titel aus einer Playlist. Fehlt die Eintrags-Id, wird sie erst von YouTube geholt.</summary>
    public async Task<string?> RemoveTrackAsync(string playlistId, TrackInfo track)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return "YouTube Music ist gerade nicht erreichbar.";
            var setId = track.SetId;
            if (setId.Length == 0)
            {
                var (st, text) = await _api.CallAsync("browse", new { browseId = "VL" + playlistId });
                setId = st == 200 ? FindSetId(text, track.Id) ?? "" : "";
                if (setId.Length == 0) return "Der Eintrag wurde nicht gefunden. „Aktualisieren“ in der Playlist klicken und nochmal versuchen.";
            }
            return await CallEditAsync("browse/edit_playlist",
                new { playlistId, actions = new[] { new { action = "ACTION_REMOVE_VIDEO", removedVideoId = track.Id, setVideoId = setId } } },
                "Nicht entfernt. Gehört die Playlist dir?", ok: t => t.Contains("STATUS_SUCCEEDED"));
        }
        catch (Exception ex) { return ex.Message; }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Legt eine neue, private Playlist an (mit <paramref name="videoId"/> als erstem Titel, wenn angegeben). Gibt Fehlertext oder die neue Playlist zurück.</summary>
    public async Task<(string? Error, PlaylistInfo? Playlist)> CreatePlaylistAsync(string title, string? videoId)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return ("YouTube Music ist gerade nicht erreichbar.", null);
            var body = new { title, description = "", privacyStatus = "PRIVATE", videoIds = videoId == null ? Array.Empty<string>() : [videoId] };
            var (status, text) = await _api.CallAsync("playlist/create", body);
            if (status == 0) return (text, null);
            if (status != 200) return ($"Nicht angelegt (Status {status}).", null);
            using var d = JsonDocument.Parse(text);
            if (!d.RootElement.TryGetProperty("playlistId", out var id) || id.GetString() is not { Length: > 0 } pid)
                return ("YouTube hat keine Playlist angelegt.", null);
            return (null, new PlaylistInfo(pid, title, videoId == null ? "0 Titel" : "1 Titel"));
        }
        catch (Exception ex) { return (ex.Message, null); }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Id der Pseudo-Playlist „Suchergebnisse“ (zeigt die Treffer in der Titelliste, ist keine echte Playlist).</summary>
    public const string SearchId = "__search__";

    /// <summary>Der Such-Filter, den die Seite für den jeweiligen Reiter schickt (Titel, Künstler, Alben, Playlists).</summary>
    static string SearchParams(string kind) => kind switch
    {
        "album" => "EgWKAQIYAWoMEA4QChADEAQQCRAF",
        "artist" => "EgWKAQIgAWoMEA4QChADEAQQCRAF",
        "playlist" => "Eg-KAQwIABAAGAAgACgBMABqChAEEAMQCRAFEAo%3D",
        _ => "EgWKAQIIAWoMEA4QChADEAQQCRAF",
    };

    /// <summary>
    /// Sucht bei YouTube Music. <paramref name="kind"/> ist leer (Titel), „artist“, „album“ oder „playlist“.
    /// Gibt Fehlertext oder die Treffer zurück (leer, wenn nichts gefunden wurde).
    /// </summary>
    public async Task<(string? Error, List<TrackInfo> Items, string? Next)> SearchAsync(string query, string kind, CancellationToken ct)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return ("YouTube Music ist gerade nicht erreichbar.", [], null);
            var (status, text) = await _api.CallAsync("search", new { query, @params = SearchParams(kind) }, ct);
            if (status == 0) return (text, [], null);
            if (status != 200) return ($"Suche fehlgeschlagen (Status {status}).", [], null);
            var (items, next) = ParseSearchPage(text, kind);
            if (items.Count == 0)
            {
                // Ersatz, falls der Filter-Wert nicht passt: ohne Filter suchen und die passenden Zeilen selbst auswählen.
                (status, text) = await _api.CallAsync("search", new { query }, ct);
                if (status == 200) (items, next) = ParseSearchPage(text, kind);
            }
            return (null, items.Select(WithThumb).ToList(), next);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (ex.Message, [], null); }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Lädt die nächste Seite einer Suche (das Zeichen kommt aus der vorigen Seite).</summary>
    public async Task<(string? Error, List<TrackInfo> Items, string? Next)> SearchMoreAsync(string token, string kind, CancellationToken ct)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return ("YouTube Music ist gerade nicht erreichbar.", [], null);
            var (status, text) = await _api.CallAsync("search", new { continuation = token }, ct);
            if (status == 0) return (text, [], null);
            if (status != 200) return ($"Weitere Treffer konnten nicht geladen werden (Status {status}).", [], null);
            var (items, next) = ParseSearchPage(text, kind);
            return (null, items.Select(WithThumb).ToList(), next);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (ex.Message, [], null); }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>
    /// Liest die Treffer der gewünschten Art aus der Antwort einer Suche. Titel: Zeilen mit Video-Id. Künstler, Alben und Playlists
    /// erkennt man an der Adresse der Zeile (UC…, MPRE…, VL…). Andere Zeilen werden ausgelassen.
    /// </summary>
    public static List<TrackInfo> ParseSearch(string json, string kind = "") => ParseSearchPage(json, kind).Items;

    /// <summary>Wie <see cref="ParseSearch"/>, gibt aber auch das Zeichen für die nächste Seite zurück (null, wenn es nichts mehr gibt).</summary>
    public static (List<TrackInfo> Items, string? Next) ParseSearchPage(string json, string kind = "")
    {
        var items = new List<TrackInfo>();
        var seen = new HashSet<string>();
        string? next = null;
        try
        {
            using var d = JsonDocument.Parse(json);
            Walk(d.RootElement);

            void Walk(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.Name == "musicResponsiveListItemRenderer") { if (SearchRow(p.Value, kind) is { } t && seen.Add(t.Id)) items.Add(t); }
                        // Die Fortsetzung steht je nach Antwort als „continuationCommand“ oder „nextContinuationData“.
                        else if (p.Name == "continuationCommand" && p.Value.TryGetProperty("token", out var tk) && tk.GetString() is { Length: > 0 } s) next = s;
                        else if (p.Name == "nextContinuationData" && p.Value.TryGetProperty("continuation", out var c) && c.GetString() is { Length: > 0 } s2) next = s2;
                        else Walk(p.Value);
                    }
                }
                else if (e.ValueKind == JsonValueKind.Array)
                {
                    foreach (var i in e.EnumerateArray()) Walk(i);
                }
            }
        }
        catch { return ([], null); }
        return (items, next);
    }

    /// <summary>Adresse der Zeile selbst (Künstler UC…, Album MPRE…, Playlist VL…). Titel-Zeilen haben keine.</summary>
    static string RowBrowseId(JsonElement r) =>
        r.TryGetProperty("navigationEndpoint", out var n) && n.TryGetProperty("browseEndpoint", out var b) && b.TryGetProperty("browseId", out var id)
            ? id.GetString() ?? "" : "";

    static TrackInfo? SearchRow(JsonElement r, string kind)
    {
        if (kind != "") return OtherRow(r, kind);
        // Die Video-Id steht je nach Zeile an anderer Stelle, aber immer als erstes „videoId“ der Zeile.
        if (FindString(r, "videoId") is not { Length: > 0 } id) return null;
        var title = string.Concat(Runs(r, "flexColumns", "musicResponsiveListItemFlexColumnRenderer", 0));
        if (title.Length == 0) return null;
        // Zweite Spalte: Interpret(en) · Album · Länge, getrennt durch Punkte.
        var parts = Runs(r, "flexColumns", "musicResponsiveListItemFlexColumnRenderer", 1);
        var artist = new List<string>();
        foreach (var run in parts)
        {
            if (run.Trim() is "•" or "·") break;
            artist.Add(run);
        }
        var duration = parts.LastOrDefault(x => System.Text.RegularExpressions.Regex.IsMatch(x.Trim(), @"^\d+:\d{2}(:\d{2})?$"))?.Trim() ?? "";
        return new TrackInfo(id, title, string.Concat(artist).Trim(), duration, ThumbOf(r), "", "", "", ArtistIdOf(r));
    }

    /// <summary>Eine Treffer-Zeile für Künstler, Alben oder Playlists. Die zweite Spalte (Art, Interpret, Jahr …) wird als Untertitel übernommen.</summary>
    static TrackInfo? OtherRow(JsonElement r, string kind)
    {
        var title = string.Concat(Runs(r, "flexColumns", "musicResponsiveListItemFlexColumnRenderer", 0));
        if (title.Length == 0) return null;
        var sub = string.Concat(Runs(r, "flexColumns", "musicResponsiveListItemFlexColumnRenderer", 1)).Trim();
        var browse = RowBrowseId(r);
        var thumb = ThumbOf(r);
        switch (kind)
        {
            case "artist" when browse.StartsWith("UC", StringComparison.Ordinal):
                return new TrackInfo(browse, title, sub, "", thumb, "", "artist", title);
            case "album" when browse.StartsWith("MPRE", StringComparison.Ordinal):
                // Das Album ist als Playlist (OLAK5uy_…) abspielbar und lesbar.
                return FindString(r, "playlistId") is { Length: > 0 } album ? new TrackInfo(album, title, sub, "", thumb, "", "album", album) : null;
            case "playlist" when browse.StartsWith("VL", StringComparison.Ordinal):
                return new TrackInfo(browse[2..], title, sub, "", thumb, "", "playlist", browse[2..]);
            default:
                return null;
        }
    }

    /// <summary>Die Kennung (UC…) des Künstlers einer Titel-Zeile: der Link in der zweiten Spalte. Leer, wenn die Zeile keinen hat.</summary>
    internal static string ArtistIdOf(JsonElement r)
    {
        if (!r.TryGetProperty("flexColumns", out var cols) || cols.ValueKind != JsonValueKind.Array || cols.GetArrayLength() < 2) return "";
        if (!cols[1].TryGetProperty("musicResponsiveListItemFlexColumnRenderer", out var c) || !c.TryGetProperty("text", out var t)
            || !t.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array) return "";
        foreach (var run in runs.EnumerateArray())
            if (run.TryGetProperty("navigationEndpoint", out var n) && n.TryGetProperty("browseEndpoint", out var b)
                && b.TryGetProperty("browseId", out var id) && id.GetString() is { } s && s.StartsWith("UC", StringComparison.Ordinal))
                return s;
        return "";
    }

    internal static string? FindString(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == name && p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
                if (FindString(p.Value, name) is { } s) return s;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in e.EnumerateArray()) if (FindString(i, name) is { } s) return s;
        }
        return null;
    }

    /// <summary>Die Textteile einer Spalte (Titel, Interpret …) einer Listenzeile, in Reihenfolge.</summary>
    internal static List<string> Runs(JsonElement r, string array, string item, int index)
    {
        var list = new List<string>();
        if (!r.TryGetProperty(array, out var cols) || cols.ValueKind != JsonValueKind.Array || cols.GetArrayLength() <= index) return list;
        if (!cols[index].TryGetProperty(item, out var c) || !c.TryGetProperty("text", out var t)) return list;
        if (t.TryGetProperty("simpleText", out var st)) { list.Add(st.GetString() ?? ""); return list; }
        if (t.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
            foreach (var x in runs.EnumerateArray()) list.Add(x.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "");
        return list;
    }

    /// <summary>Adresse des größten Vorschaubilds einer Listenzeile oder leer.</summary>
    internal static string ThumbOf(JsonElement r) =>
        r.TryGetProperty("thumbnail", out var th) && th.TryGetProperty("musicThumbnailRenderer", out var mt)
        && mt.TryGetProperty("thumbnail", out var t2) && t2.TryGetProperty("thumbnails", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
            ? arr[arr.GetArrayLength() - 1].TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""
            : "";

    /// <summary>Lädt die Künstler-Seite (Kopf, Top-Songs, Alben …). Gibt Fehlertext oder die Seite zurück.</summary>
    public async Task<(string? Error, ArtistPage? Page)> LoadArtistAsync(string browseId, CancellationToken ct)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return ("YouTube Music ist gerade nicht erreichbar.", null);
            var (status, text) = await _api.CallAsync("browse", new { browseId }, ct);
            if (status == 0) return (text, null);
            if (status != 200) return ($"Die Künstler-Seite konnte nicht geladen werden (Status {status}).", null);
            var page = YtArtistParser.Parse(text, browseId);
            if (page == null) return ("Die Künstler-Seite hat ein unbekanntes Format.", null);
            // Bilder der Karten und Top-Songs auf brauchbare Adressen bringen.
            return (null, page with
            {
                Thumb = BigThumb(page.Thumb),
                TopSongs = [.. page.TopSongs.Select(WithThumb)],
                Sections = [.. page.Sections.Select(s => s with { Items = [.. s.Items.Select(WithThumb)] })]
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (ex.Message, null); }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Holt den Songtext zu einem Titel von YouTube Music. Null, wenn es keinen gibt oder die Anfrage scheitert.</summary>
    public async Task<LyricsResult?> GetLyricsAsync(string videoId, CancellationToken ct)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return null;
            var (status, text) = await _api.CallAsync("next", new { videoId, isAudioOnly = true }, ct);
            if (status != 200 || YtLyrics.FindBrowseId(text) is not { } browseId) return null;
            (status, text) = await _api.CallAsync("browse", new { browseId }, ct);
            return status == 200 ? YtLyrics.Parse(text) : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Warn("YouTube", "Songtext konnte nicht geladen werden", ex);
            return null;
        }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Lädt die Startseite (Empfehlungen). Mit <paramref name="token"/> die nächsten Bereiche. Gibt Fehlertext oder Bereiche und das Zeichen für mehr zurück.</summary>
    public async Task<(string? Error, List<ArtistSectionData> Sections, string? Next)> LoadHomeAsync(string? token, CancellationToken ct)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return ("YouTube Music ist gerade nicht erreichbar.", [], null);
            object body = token == null ? new { browseId = "FEmusic_home" } : new { continuation = token };
            var (status, text) = await _api.CallAsync("browse", body, ct);
            if (status == 0) return (text, [], null);
            if (status != 200) return ($"Die Startseite konnte nicht geladen werden (Status {status}).", [], null);
            var (sections, next) = YtHomeParser.Parse(text);
            if (sections.Count == 0 && token == null) return ("Die Startseite hat ein unbekanntes Format oder ist leer.", [], null);
            var withThumbs = sections.Select(s => s with { Items = [.. s.Items.Select(WithThumb)] }).ToList();
            return (null, withThumbs, next);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (ex.Message, [], null); }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Größeres Bild für den Kopf der Künstler-Seite (die Adresse trägt die Größe am Ende).</summary>
    static string BigThumb(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Regex.Replace(url, @"=w\d+-h\d+[^?]*$", "=w512-h512-l90-rj") : url;

    /// <summary>Abonniert einen Künstler oder nimmt das Abo zurück.</summary>
    public async Task<string?> SetSubscribedAsync(string channelId, bool subscribe)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return "YouTube Music ist gerade nicht erreichbar.";
            return await CallEditAsync(subscribe ? "subscription/subscribe" : "subscription/unsubscribe", new { channelIds = new[] { channelId } },
                "YouTube hat die Änderung abgelehnt.", ok: t => !t.Contains("\"error\""));
        }
        catch (Exception ex) { return ex.Message; }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Setzt „Gefällt mir“ eines beliebigen Titels (nicht nur des laufenden) oder nimmt es zurück.</summary>
    public async Task<string?> SetLikeAsync(string videoId, bool like)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return "YouTube Music ist gerade nicht erreichbar.";
            return await CallEditAsync(like ? "like/like" : "like/removelike", new { target = new { videoId } },
                "YouTube hat die Bewertung abgelehnt.", ok: t => !t.Contains("\"error\""));
        }
        catch (Exception ex) { return ex.Message; }
        finally { await StopIfStartedAsync(started); }
    }

    /// <summary>Löscht eine Playlist des Kontos bei YouTube.</summary>
    public async Task<string?> DeletePlaylistAsync(string playlistId)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return "YouTube Music ist gerade nicht erreichbar.";
            return await CallEditAsync("playlist/delete", new { playlistId }, "Nicht gelöscht. Gehört die Playlist dir?", ok: t => !t.Contains("\"error\""));
        }
        catch (Exception ex) { return ex.Message; }
        finally { await StopIfStartedAsync(started); }
    }

    async Task<string?> EditAsync(object body, string failText, Func<Task> onSuccess)
    {
        var started = !engine.IsRunning;
        try
        {
            if (!await PreparePageAsync()) return "YouTube Music ist gerade nicht erreichbar.";
            var error = await CallEditAsync("browse/edit_playlist", body, failText, ok: t => t.Contains("STATUS_SUCCEEDED"));
            if (error == null) await onSuccess();
            return error;
        }
        catch (Exception ex) { return ex.Message; }
        finally { await StopIfStartedAsync(started); }
    }

    async Task<string?> CallEditAsync(string path, object body, string failText, Func<string, bool> ok)
    {
        var (status, text) = await _api.CallAsync(path, body);
        if (status == 0) return text;
        return status == 200 && ok(text) ? null : failText;
    }

    /// <summary>Sorgt dafür, dass die Web-Ansicht auf einer music.youtube.com-Seite läuft (nur dort stehen die Anmelde-Daten). Läuft sie schon dort, bleibt die Wiedergabe unberührt.</summary>
    async Task<bool> PreparePageAsync()
    {
        source.Touch();
        if (!engine.IsRunning) await engine.StartAsync(UrlNormalizer.Home);
        else if (!await OnPageAsync("music.youtube.com")) await engine.Host.NavigateAndWaitAsync(UrlNormalizer.Home);
        return engine.IsRunning;
    }

    /// <summary>Sucht in der Antwort einer Playlist-Seite die Eintrags-Id (playlistSetVideoId) zu einer Video-Id. Null, wenn nicht gefunden.</summary>
    public static string? FindSetId(string json, string videoId)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            return Walk(d.RootElement);
        }
        catch { return null; }

        string? Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("playlistItemData", out var pd) && pd.ValueKind == JsonValueKind.Object
                    && pd.TryGetProperty("videoId", out var v) && v.GetString() == videoId
                    && pd.TryGetProperty("playlistSetVideoId", out var s) && s.GetString() is { Length: > 0 } id)
                    return id;
                foreach (var p in e.EnumerateObject()) if (Walk(p.Value) is { } r) return r;
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in e.EnumerateArray()) if (Walk(i) is { } r) return r;
            }
            return null;
        }
    }

    /// <summary>
    /// Prüft im Hintergrund, ob sich Playlists geändert haben (Titel, Anzahl, Reihenfolge). Läuft nur, wenn nichts abgespielt wird.
    /// Gibt null zurück, wenn nicht geprüft werden konnte (spielt gerade, abgebrochen, nichts gefunden). Sonst die aktuelle Liste und ob sie sich geändert hat.
    /// </summary>
    public async Task<(List<PlaylistInfo> Items, bool Changed)?> CheckForUpdatesAsync(CancellationToken ct)
    {
        if (source.InUse) return null;
        using var scope = perf.Begin("Yt.PlaylistCheck");
        var cached = LoadCached();
        var started = !engine.IsRunning;
        try
        {
            if (started) await engine.StartAsync("about:blank");
            var items = await ScrapeAsync(ct, background: true);
            if (items.Count == 0) { scope.Fail(); return null; }
            var changed = !SameList(cached, items);
            store.Save("yt-playlists", new YtPlaylistCache { Version = CacheVersion, Items = changed ? items : cached, CheckedUtc = DateTime.UtcNow });
            return (changed ? items : cached, changed);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            scope.Fail();
            return null;
        }
        finally
        {
            await StopIfStartedAsync(started);
        }
    }

    /// <summary>
    /// Liest die Playlists neu (vom Nutzer ausgelöst). Läuft gerade Musik, setzt der Player danach an derselben Stelle fort.
    /// Gibt die Liste und einen Diagnose-Text zurück (leer, wenn alles gut ging).
    /// </summary>
    public async Task<(List<PlaylistInfo> Items, string Info)> RefreshAsync(IProgress<string>? progress, CancellationToken ct)
    {
        using var scope = perf.Begin("Yt.PlaylistScrape");
        var (wasPlaying, prevUrl, prevPos) = Snapshot();
        var started = !engine.IsRunning;
        source.Touch();
        try
        {
            if (started) await engine.StartAsync("about:blank");
            progress?.Report("Playlists werden gelesen …");
            var items = await ScrapeAsync(ct, background: false);
            var info = "";
            if (items.Count == 0)
            {
                progress?.Report("Keine Treffer. Zweiter Versuch ohne Blockade …");
                info = await engine.ExecStringAsync(LibraryScripts.Diagnose);
                engine.Host.BlockActive = false;
                try { items = await ScrapeAsync(ct, background: false); }
                finally { engine.Host.BlockActive = true; }
                if (items.Count > 0) info = "Nur ohne Blockade gefunden. " + info;
            }
            if (items.Count > 0) store.Save("yt-playlists", new YtPlaylistCache { Version = CacheVersion, Items = items, CheckedUtc = DateTime.UtcNow });
            else scope.Fail();
            if (wasPlaying && prevUrl.Length > 0) await source.PlayUrlAsync(prevUrl, prevPos, true);
            return (items, info);
        }
        catch
        {
            scope.Fail();
            throw;
        }
        finally
        {
            if (!wasPlaying) await StopIfStartedAsync(started);
        }
    }

    /// <summary>
    /// Liest die Titel einer Playlist. Mit <paramref name="background"/> nur, wenn nichts abgespielt wird (sonst null).
    /// Ohne <paramref name="background"/> unterbricht es eine laufende Wiedergabe kurz und setzt danach an derselben Stelle fort.
    /// </summary>
    public async Task<List<TrackInfo>?> RefreshTracksAsync(string playlistId, bool background, IProgress<string>? progress, CancellationToken ct)
    {
        // Zuerst über die Schnittstelle: kein Seitenwechsel, die Wiedergabe läuft ungestört weiter.
        progress?.Report("Songs werden gelesen …");
        var viaApi = await TryFetchTracksAsync(playlistId, ct);
        if (viaApi.Count > 0)
        {
            store.Save(TracksSection(playlistId), new YtTracksCache { Items = viaApi, CheckedUtc = DateTime.UtcNow });
            source.QueueChanged(playlistId);
            return viaApi;
        }
        if (background && source.InUse) return null;
        using var scope = perf.Begin("Yt.TracksScrape");
        var (wasPlaying, prevUrl, prevPos) = Snapshot();
        var started = !engine.IsRunning;
        source.Touch();
        try
        {
            if (started) await engine.StartAsync("about:blank");
            progress?.Report("Songs werden gelesen …");
            var items = await ScrapeTracksAsync(playlistId, background, ct);
            if (items.Count == 0) scope.Fail();
            else store.Save(TracksSection(playlistId), new YtTracksCache { Items = items, CheckedUtc = DateTime.UtcNow });
            if (wasPlaying && prevUrl.Length > 0 && !background) await source.PlayUrlAsync(prevUrl, prevPos, true);
            return items;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            scope.Fail();
            throw;
        }
        finally
        {
            if (!wasPlaying) await StopIfStartedAsync(started);
        }
    }

    (bool WasPlaying, string Url, double Pos) Snapshot() =>
        (source.State.IsPlaying, source.State.Item?.Id.Contains("watch") == true ? source.State.Item.Id : "", source.State.Position.TotalSeconds);

    /// <summary>Beendet die Web-Ansicht wieder, wenn wir sie gestartet haben und niemand sie für die Wiedergabe braucht (spart RAM).</summary>
    async Task StopIfStartedAsync(bool started)
    {
        if (started && !source.InUse && engine.IsRunning)
        {
            try { await engine.StopAsync(); }
            catch { }
        }
    }

    /// <summary>Prüft, ob die Seite wirklich dort ist, wo wir lesen wollen. Sonst würden wir z. B. die Warteschlange der Wiedergabe-Seite für Playlists halten.</summary>
    async Task<bool> OnPageAsync(string fragment)
    {
        try { return (await engine.ExecStringAsync("location.href")).Contains(fragment, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    internal static string TracksSection(string playlistId) => "yt-pl-" + Regex.Replace(playlistId, "[^A-Za-z0-9_-]", "_");

    async Task<List<PlaylistInfo>> ScrapeAsync(CancellationToken ct, bool background)
    {
        await engine.Host.NavigateAndWaitAsync(UrlNormalizer.LibraryPlaylists);
        var items = new List<PlaylistInfo>();
        var last = -1;
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(1500, ct);
            if (background && source.InUse) throw new OperationCanceledException();
            if (!await OnPageAsync("/library/")) continue;
            try
            {
                var json = await engine.ExecStringAsync(LibraryScripts.Scrape);
                items = JsonSerializer.Deserialize<List<PlaylistInfo>>(json, Json) ?? [];
            }
            catch { items = []; }
            if (items.Count > 0 && items.Count == last) break;
            last = items.Count;
            await engine.ExecAsync(LibraryScripts.Scroll);
        }
        return items;
    }

    async Task<List<TrackInfo>> ScrapeTracksAsync(string playlistId, bool background, CancellationToken ct)
    {
        await engine.Host.NavigateAndWaitAsync(UrlNormalizer.PlaylistPage(playlistId));
        var items = new List<TrackInfo>();
        var last = -1;
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(1200, ct);
            if (background && source.InUse) throw new OperationCanceledException();
            if (!await OnPageAsync("/playlist")) continue;
            try
            {
                var json = await engine.ExecStringAsync(LibraryScripts.Tracks);
                items = (JsonSerializer.Deserialize<List<TrackInfo>>(json, Json) ?? []).Select(WithThumb).ToList();
            }
            catch { items = []; }
            if (items.Count > 0 && items.Count == last) break;
            last = items.Count;
            await engine.ExecAsync(LibraryScripts.Scroll);
        }
        return items;
    }

    /// <summary>Setzt eine brauchbare Bildadresse: die der Seite (kleiner skaliert) oder sonst das Standard-Vorschaubild zur Video-Id.</summary>
    internal static TrackInfo WithThumb(TrackInfo t)
    {
        var thumb = t.Thumb ?? "";
        if (!thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            thumb = $"https://i.ytimg.com/vi/{Uri.EscapeDataString(t.Id)}/mqdefault.jpg";
        else
            thumb = Regex.Replace(thumb, @"=w\d+-h\d+[^?]*$", "=w120-h120-l90-rj");
        return t with { Thumb = thumb };
    }
}
