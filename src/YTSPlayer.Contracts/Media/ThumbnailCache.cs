using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Contracts.Media;

/// <summary>Holt kleine Vorschaubilder aus dem Netz und merkt sie sich als Dateien. Gibt die rohen Bild-Bytes zurück.</summary>
public interface IThumbnailCache
{
    /// <summary>Liefert die Bild-Bytes (aus dem Datei-Cache oder frisch geladen). Null, wenn nichts geladen werden konnte.</summary>
    Task<byte[]?> GetAsync(string? url, CancellationToken ct = default);
}

/// <summary>
/// Datei-Cache für Vorschaubilder unter <see cref="IAppPaths.Cache"/>\thumbs. Lädt höchstens 4 Bilder gleichzeitig,
/// teilt sich doppelte Anfragen und räumt alte Dateien auf, wenn der Ordner größer als <see cref="MaxBytes"/> wird.
/// </summary>
public sealed class ThumbnailCache : IThumbnailCache
{
    const long MaxBytes = 100L * 1024 * 1024;
    const int MaxImageBytes = 2 * 1024 * 1024;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly string _dir;
    readonly IPerfTracker _perf;
    readonly SemaphoreSlim _slots = new(4);
    readonly ConcurrentDictionary<string, Task<byte[]?>> _running = new();
    int _pruned;

    public ThumbnailCache(IAppPaths paths, IPerfTracker perf)
    {
        _dir = Path.Combine(paths.Cache, "thumbs");
        _perf = perf;
    }

    public async Task<byte[]?> GetAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var file = Path.Combine(_dir, Hash(url) + ".img");
        try
        {
            if (File.Exists(file)) return await File.ReadAllBytesAsync(file, ct);
        }
        catch (IOException)
        {
        }
        // Läuft schon dieselbe Anfrage, wird sie mitbenutzt. Ein Abbruch betrifft nur den Wartenden, nicht den Download.
        var task = _running.GetOrAdd(url, _ => DownloadAsync(uri, file));
        try
        {
            return await task.WaitAsync(ct);
        }
        finally
        {
            if (task.IsCompleted) _running.TryRemove(url, out _);
        }
    }

    async Task<byte[]?> DownloadAsync(Uri uri, string file)
    {
        await _slots.WaitAsync();
        try
        {
            using var scope = _perf.Begin("Thumb.Load");
            try
            {
                var data = await Http.GetByteArrayAsync(uri);
                if (data.Length is 0 or > MaxImageBytes) { scope.Fail(); return null; }
                Directory.CreateDirectory(_dir);
                var tmp = file + "." + Environment.CurrentManagedThreadId + ".tmp";
                await File.WriteAllBytesAsync(tmp, data);
                File.Move(tmp, file, true);
                if (Interlocked.Exchange(ref _pruned, 1) == 0) _ = Task.Run(Prune);
                return data;
            }
            catch (Exception)
            {
                scope.Fail();
                return null;
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Löscht die ältesten Dateien, bis der Ordner wieder unter der Grenze liegt (einmal pro Start).</summary>
    void Prune()
    {
        try
        {
            var files = new DirectoryInfo(_dir).GetFiles("*.img").OrderBy(f => f.LastAccessTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= MaxBytes) break;
                total -= f.Length;
                f.Delete();
            }
        }
        catch (Exception)
        {
        }
    }

    static string Hash(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
}
