using System.Text;

namespace Fledgr.Audio;

/// <summary>
/// Liest einen Internet-Radio-Stream selbst und fragt dabei nach ICY-Metadaten. LibVLC 3 tut das bei HTTPS nicht,
/// deshalb käme sonst kein Titel an. Die Metadaten werden herausgeschnitten, LibVLC bekommt nur Audio.
/// Ein Lesefehler oder <see cref="Abort"/> beendet den Stream (Ende für LibVLC).
/// </summary>
public sealed class IcyStream : Stream
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    static readonly HttpClient Http = CreateHttp();

    readonly HttpResponseMessage _response;
    readonly Stream _body;
    readonly int _metaInt;
    readonly CancellationTokenSource _life;
    int _untilMeta;
    string? _lastTitle;

    IcyStream(HttpResponseMessage response, Stream body, int metaInt, CancellationTokenSource life)
    {
        _response = response;
        _body = body;
        _metaInt = metaInt;
        _untilMeta = metaInt;
        _life = life;
    }

    /// <summary>Neuer Titel aus den ICY-Daten (meist „Künstler - Titel“). Kommt aus dem Lese-Thread von LibVLC.</summary>
    public event Action<string>? TitleChanged;

    static HttpClient CreateHttp()
    {
        // Der Stream läuft unbegrenzt. Die Wartezeit gilt nur bis zu den Kopfzeilen (ConnectTimeout).
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Fledgr/1.0");
        return http;
    }

    /// <summary>
    /// Öffnet den Stream. Gibt null zurück, wenn LibVLC die Adresse besser selbst öffnet
    /// (HLS, Playlist, kein Audio, Fehler). Dann geht nichts verloren, LibVLC versucht es wie bisher.
    /// </summary>
    public static async Task<IcyStream?> OpenAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || IsPlaylistPath(uri.AbsolutePath))
            return null;

        var life = new CancellationTokenSource();
        HttpResponseMessage? response = null;
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token);
            connect.CancelAfter(ConnectTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Icy-MetaData", "1");
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connect.Token);
            if (!response.IsSuccessStatusCode || !IsAudio(response.Content.Headers.ContentType?.MediaType))
            {
                response.Dispose();
                life.Dispose();
                return null;
            }
            var metaInt = response.Headers.TryGetValues("icy-metaint", out var values) && int.TryParse(values.FirstOrDefault(), out var n) && n > 0 ? n : 0;
            var net = await response.Content.ReadAsStreamAsync(connect.Token);
            // Vorpuffer: etwa 4 Sekunden Audio (nach Bitrate), sonst 64 KB.
            var kbps = response.Headers.TryGetValues("icy-br", out var br) && int.TryParse(br.FirstOrDefault()?.Split(',')[0], out var k) && k > 0 ? k : 128;
            var prebuffer = Math.Clamp(kbps * 125 * 4, 32 * 1024, 160 * 1024);
            var body = new PumpedBody(net, prebuffer, life.Token);
            return new IcyStream(response, body, metaInt, life);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            response?.Dispose();
            life.Dispose();
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    static bool IsPlaylistPath(string path) =>
        path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".pls", StringComparison.OrdinalIgnoreCase);

    static bool IsAudio(string? type) =>
        type != null
        && (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || type.Equals("application/ogg", StringComparison.OrdinalIgnoreCase))
        && !type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
        && !type.Contains("scpls", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bricht das Lesen ab. LibVLC sieht dann das Ende des Streams.</summary>
    public void Abort()
    {
        try { _life.Cancel(); } catch (ObjectDisposedException) { }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            if (_metaInt > 0 && _untilMeta == 0)
            {
                ReadMeta();
                _untilMeta = _metaInt;
            }
            var max = _metaInt > 0 ? Math.Min(count, _untilMeta) : count;
            var read = ReadBody(buffer, offset, max);
            if (_metaInt > 0) _untilMeta -= read;
            return read;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or HttpRequestException)
        {
            return 0;
        }
    }

    int ReadBody(byte[] buffer, int offset, int count) =>
        _body.ReadAsync(buffer.AsMemory(offset, count), _life.Token).AsTask().GetAwaiter().GetResult();

    /// <summary>Ein Metadaten-Block: 1 Byte Länge (× 16), danach Text wie „StreamTitle='…';StreamUrl='…';“.</summary>
    void ReadMeta()
    {
        var lengthByte = new byte[1];
        ReadExactly(lengthByte, 1);
        var length = lengthByte[0] * 16;
        if (length == 0) return;
        var meta = new byte[length];
        ReadExactly(meta, length);

        var title = ParseTitle(Decode(meta));
        if (title == null || title == _lastTitle) return;
        _lastTitle = title;
        TitleChanged?.Invoke(title);
    }

    void ReadExactly(byte[] buffer, int count)
    {
        var done = 0;
        while (done < count)
        {
            var read = ReadBody(buffer, done, count - done);
            if (read == 0) throw new IOException("Stream beendet.");
            done += read;
        }
    }

    /// <summary>Meist UTF-8. Ältere Server senden Latin-1, das erkennt man an ungültigen UTF-8-Zeichen.</summary>
    static string Decode(byte[] meta)
    {
        var end = Array.IndexOf(meta, (byte)0);
        var span = meta.AsSpan(0, end < 0 ? meta.Length : end);
        var text = Encoding.UTF8.GetString(span);
        return text.Contains('�') ? Encoding.Latin1.GetString(span) : text;
    }

    /// <summary>Holt den Wert von StreamTitle. Ein Apostroph im Titel ist erlaubt, Schluss ist erst „';“.</summary>
    public static string? ParseTitle(string meta)
    {
        const string key = "StreamTitle='";
        var start = meta.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length;
        var end = meta.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0) end = meta.LastIndexOf('\'');
        if (end < start) return null;
        var title = meta[start..end].Trim();
        return title.Length > 0 ? title : null;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    /// <summary>
    /// Live-Stream ohne Länge. LibVLCSharp fragt die Länge trotz <see cref="CanSeek"/> = false ab.
    /// -1 wird dort zu ulong.MaxValue, das heißt für LibVLC „Größe unbekannt“. Nicht werfen, sonst hält der Debugger an.
    /// </summary>
    public override long Length => -1;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Abort();
            _body.Dispose();
            _response.Dispose();
            _life.Dispose();
        }
        base.Dispose(disposing);
    }
}
