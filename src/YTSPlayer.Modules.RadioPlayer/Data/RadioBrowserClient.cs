using System.Text;
using System.Text.Json;

namespace YTSPlayer.Modules.RadioPlayer.Data;

/// <summary>
/// Abfragen bei radio-browser.info (freie Sender-Datenbank). Nur Lesezugriffe, ohne Klick-Zählung, damit wenig Anfragen entstehen.
/// Der Server kommt aus der Serverliste. Fällt einer aus, wird der nächste probiert.
/// </summary>
public sealed class RadioBrowserClient(HttpClient http)
{
    const string ServerListUrl = "https://all.api.radio-browser.info/json/servers";
    const int MaxPlaylistBytes = 64 * 1024;
    const int MaxAttempts = 3;
    static readonly string[] FallbackServers = ["de1.api.radio-browser.info"];
    static readonly TimeSpan ServerListTtl = TimeSpan.FromHours(1);

    IReadOnlyList<string>? _servers;
    DateTime _serversAt;

    public async Task<IReadOnlyList<RadioStation>> SearchAsync(string name, string countryCode, string tag, CancellationToken ct)
    {
        var query = new List<string> { "hidebroken=true", "order=clickcount", "reverse=true", "limit=100" };
        if (name.Length > 0) query.Add("name=" + Uri.EscapeDataString(name));
        if (countryCode.Length > 0) query.Add("countrycode=" + Uri.EscapeDataString(countryCode));
        if (tag.Length > 0) query.Add("tag=" + Uri.EscapeDataString(tag));
        return RadioBrowserParser.ParseStations(await GetJsonAsync("/json/stations/search?" + string.Join("&", query), ct));
    }

    public async Task<IReadOnlyList<RadioCountry>> CountriesAsync(CancellationToken ct) =>
        RadioBrowserParser.ParseCountries(await GetJsonAsync("/json/countries?hidebroken=true&order=stationcount&reverse=true", ct));

    /// <summary>Der Sender mit dieser Id oder null, wenn die Datenbank ihn nicht (mehr) kennt.</summary>
    public async Task<RadioStation?> FindAsync(string stationId, CancellationToken ct) =>
        RadioBrowserParser.ParseStations(await GetJsonAsync("/json/stations/byuuid/" + Uri.EscapeDataString(stationId), ct)).FirstOrDefault();

    /// <summary>
    /// Lädt den Anfang einer Playlist (höchstens 64 KB). So bleibt eine falsche Adresse mit endlosem Stream nicht hängen.
    /// </summary>
    public async Task<string> DownloadPlaylistAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxPlaylistBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    async Task<string> GetJsonAsync(string path, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var host in (await ServersAsync(ct)).OrderBy(_ => Random.Shared.Next()).Take(MaxAttempts))
        {
            try
            {
                return await http.GetStringAsync("https://" + host + path, ct);
            }
            catch (Exception e) when ((e is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                last = e;
            }
        }
        throw last ?? new HttpRequestException("No server of the station database is reachable.");
    }

    async Task<IReadOnlyList<string>> ServersAsync(CancellationToken ct)
    {
        if (_servers != null && DateTime.UtcNow - _serversAt < ServerListTtl) return _servers;
        try
        {
            var names = RadioBrowserParser.ParseServers(await http.GetStringAsync(ServerListUrl, ct));
            if (names.Count > 0)
            {
                _servers = names;
                _serversAt = DateTime.UtcNow;
                return names;
            }
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException or JsonException) && !ct.IsCancellationRequested)
        {
            // Serverliste nicht lesbar: unten der feste Rückfall.
        }
        return FallbackServers;
    }
}
