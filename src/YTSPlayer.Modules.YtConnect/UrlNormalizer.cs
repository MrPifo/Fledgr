using System.Web;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>Bekannte YT-Music-Adressen und Umwandlung eingefügter Links.</summary>
public static class UrlNormalizer
{
    public const string Home = "https://music.youtube.com/";
    public const string LibraryPlaylists = "https://music.youtube.com/library/playlists";

    /// <summary>Google-Anmeldung, die danach direkt zu YouTube Music zurückkehrt.</summary>
    public const string Login = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fmusic.youtube.com%2F";

    /// <summary>URL, die eine Playlist direkt ab dem ersten Titel startet.</summary>
    public static string WatchPlaylist(string id) => $"{Home}watch?list={Uri.EscapeDataString(id)}";

    /// <summary>Die Seite einer Playlist (zum Auslesen der Titel).</summary>
    public static string PlaylistPage(string id) => $"{Home}playlist?list={Uri.EscapeDataString(id)}";

    /// <summary>Der einzelne Titel auf dem normalen YouTube (ohne Playlist): https://www.youtube.com/watch?v=ID. Null, wenn die Adresse keine Video-Id hat.</summary>
    public static string? ToYoutubeTrackUrl(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u) || u.Host is not ("music.youtube.com" or "www.youtube.com" or "youtube.com")) return null;
        var id = HttpUtility.ParseQueryString(u.Query)["v"];
        return string.IsNullOrEmpty(id) ? null : "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(id);
    }

    /// <summary>Die Video-Id aus einer YouTube-/YouTube-Music-Adresse (Parameter v) oder null.</summary>
    public static string? VideoId(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u)) return null;
        var id = HttpUtility.ParseQueryString(u.Query)["v"];
        return string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>Die Playlist-Id aus einer Adresse (Parameter list) oder null. Automatische Radio-Listen (RD…) zählen nicht.</summary>
    public static string? PlaylistId(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u)) return null;
        var id = HttpUtility.ParseQueryString(u.Query)["list"];
        return string.IsNullOrEmpty(id) || id.StartsWith("RD", StringComparison.Ordinal) ? null : id;
    }

    /// <summary>Wahr, wenn die Adresse eine automatische Radio-Playlist ist (list=RD…, z. B. RDAMVM…).</summary>
    public static bool IsRadioMix(string? href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var u) && (HttpUtility.ParseQueryString(u.Query)["list"]?.StartsWith("RD", StringComparison.Ordinal) ?? false);

    /// <summary>Radio-Mix zu einem Titel: die automatische Playlist „RDAMVM“ + Video-Id, die YouTube Music selbst für „Radio starten“ nutzt.</summary>
    public static string RadioMix(string videoId) =>
        $"{Home}watch?v={Uri.EscapeDataString(videoId)}&list=RDAMVM{Uri.EscapeDataString(videoId)}";

    /// <summary>Radio-Mix zu einer ganzen Playlist („Mix starten“ in YouTube Music): die automatische Playlist „RDAMPL“ + Playlist-Id.</summary>
    public static string PlaylistRadio(string playlistId) => $"{Home}watch?list=RDAMPL{Uri.EscapeDataString(playlistId)}";

    /// <summary>Link zum Teilen eines Titels (YouTube Music).</summary>
    public static string ShareTrack(string videoId) => $"{Home}watch?v={Uri.EscapeDataString(videoId)}";

    /// <summary>Link zum Teilen einer Playlist (YouTube Music).</summary>
    public static string SharePlaylist(string playlistId) => PlaylistPage(playlistId);

    /// <summary>Der Titel auf dem normalen YouTube.</summary>
    public static string YoutubeTrack(string videoId) => "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(videoId);

    /// <summary>Die Playlist auf dem normalen YouTube.</summary>
    public static string YoutubePlaylist(string playlistId) => "https://www.youtube.com/playlist?list=" + Uri.EscapeDataString(playlistId);

    /// <summary>Ein Titel innerhalb seiner Playlist (spielt danach die Playlist weiter).</summary>
    public static string WatchTrack(string videoId, string playlistId) =>
        $"{Home}watch?v={Uri.EscapeDataString(videoId)}&list={Uri.EscapeDataString(playlistId)}";

    /// <summary>Wandelt einen eingefügten YouTube-/YouTube-Music-Link in eine music.youtube.com-URL um. Gibt null zurück, wenn der Link nicht passt.</summary>
    public static string? ToMusicUrl(string input)
    {
        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return null;
        var host = u.Host.ToLowerInvariant();
        var q = HttpUtility.ParseQueryString(u.Query);
        if (host == "youtu.be") return Watch(u.AbsolutePath.Trim('/'), q["list"]);
        if (host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")) return null;
        if (u.AbsolutePath.StartsWith("/watch")) return Watch(q["v"], q["list"]);
        if (u.AbsolutePath.StartsWith("/playlist") && q["list"] is { Length: > 0 } l) return $"{Home}playlist?list={Uri.EscapeDataString(l)}";
        return null;
    }

    static string? Watch(string? id, string? list)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var url = $"{Home}watch?v={Uri.EscapeDataString(id)}";
        return string.IsNullOrWhiteSpace(list) ? url : $"{url}&list={Uri.EscapeDataString(list)}";
    }
}
