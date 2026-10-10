using Avalonia.Media.Imaging;

namespace YTSPlayer.Modules.MusicPlayer.Library;

/// <summary>
/// Holt kleine Cover für Titel der Liste: erst das eingebettete Bild der Datei, sonst eine Bilddatei im Ordner (cover.jpg, folder.jpg …).
/// Die Bilder werden klein dekodiert und nur kurz gemerkt (die Liste ist virtualisiert, es sind immer nur die sichtbaren Zeilen im Speicher).
/// </summary>
public sealed class LocalCovers
{
    const int Width = 96;
    const int MaxCached = 150;
    static readonly string[] FolderImages = ["cover.jpg", "folder.jpg", "front.jpg", "albumart.jpg", "cover.png", "folder.png"];

    readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<string> _order = new();
    readonly SemaphoreSlim _gate = new(2);

    /// <summary>Das Cover der Datei oder null, wenn es keins gibt. Liest im Hintergrund.</summary>
    public async Task<Bitmap?> GetAsync(string file)
    {
        lock (_cache)
            if (_cache.TryGetValue(file, out var hit)) return hit;

        Bitmap? bmp = null;
        await _gate.WaitAsync();
        try
        {
            bmp = await Task.Run(() =>
            {
                var bytes = Load(file);
                if (bytes == null) return null;
                try
                {
                    using var ms = new MemoryStream(bytes);
                    return Bitmap.DecodeToWidth(ms, Width);
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }
        finally
        {
            _gate.Release();
        }

        lock (_cache)
        {
            _cache[file] = bmp;
            _order.Enqueue(file);
            while (_order.Count > MaxCached) _cache.Remove(_order.Dequeue());
        }
        return bmp;
    }

    static byte[]? Load(string file)
    {
        try
        {
            using var f = TagLib.File.Create(file, TagLib.ReadStyle.PictureLazy);
            var pic = f.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? f.Tag.Pictures.FirstOrDefault();
            if (pic?.Data.Data is { Length: > 0 and < 4_000_000 } data) return data;
        }
        catch (Exception)
        {
            // Datei ohne lesbare Tags: weiter mit dem Bild im Ordner.
        }
        try
        {
            if (Path.GetDirectoryName(file) is { } dir)
                foreach (var name in FolderImages)
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path) && new FileInfo(path).Length < 4_000_000) return File.ReadAllBytes(path);
                }
        }
        catch (Exception)
        {
            // Ohne Cover bleibt das Symbol stehen.
        }
        return null;
    }
}
