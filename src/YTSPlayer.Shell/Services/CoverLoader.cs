using Avalonia.Media.Imaging;
using YTSPlayer.Contracts.Diagnostics;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Lädt Cover aus Bytes, einer Datei oder einer URL, verkleinert auf die Zielbreite und hält einen kleinen Cache.
/// Alles asynchron, mit Timeout und Abbruch.
/// </summary>
public sealed class CoverLoader(IPerfTracker perf)
{
    const int MaxCached = 16;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Dictionary<string, Bitmap> _cache = [];
    readonly LinkedList<string> _order = [];

    /// <summary>Lädt das Cover. Gibt null zurück, wenn nichts da ist oder das Laden scheitert. Bitmaps gehören dem Cache, nicht selbst freigeben.</summary>
    public async Task<Bitmap?> LoadAsync(string? url, byte[]? bytes, int width, CancellationToken ct = default)
    {
        if (bytes is { Length: > 0 }) return Decode(bytes, width);
        if (string.IsNullOrEmpty(url)) return null;
        if (_cache.TryGetValue(url, out var hit)) return hit;
        using var scope = perf.Begin("Cover.Load");
        try
        {
            var data = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? await Http.GetByteArrayAsync(url, ct)
                : await File.ReadAllBytesAsync(url, ct);
            var bmp = await Task.Run(() => Decode(data, width), ct);
            if (bmp != null) Add(url, bmp);
            return bmp;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            scope.Fail();
            return null;
        }
    }

    /// <summary>Dekodiert Bytes auf die Zielbreite. Gibt null bei ungültigen Bilddaten zurück.</summary>
    public static Bitmap? Decode(byte[] data, int width)
    {
        try
        {
            using var ms = new MemoryStream(data);
            return Bitmap.DecodeToWidth(ms, width);
        }
        catch
        {
            return null;
        }
    }

    void Add(string key, Bitmap bmp)
    {
        _cache[key] = bmp;
        _order.AddLast(key);
        while (_order.Count > MaxCached)
        {
            var oldest = _order.First!.Value;
            _order.RemoveFirst();
            _cache.Remove(oldest);
        }
    }
}
