namespace YTSPlayer.Contracts.Media;

/// <summary>Ein abspielbarer Eintrag. <see cref="SourceId"/> sagt, welche Quelle ihn abspielt.</summary>
/// <param name="SourceId">Id der Wiedergabe-Quelle (z. B. "local", "yt", "radio").</param>
/// <param name="Id">Quellen-spezifische Id: Dateipfad, URL oder Sender-Id.</param>
public sealed record MediaItem(
    string SourceId,
    string Id,
    string Title,
    string Artist = "",
    string Album = "",
    string? CoverUrl = null,
    TimeSpan? Duration = null);

/// <summary>Art eines Eintrags im Medien-Baum.</summary>
public enum MediaNodeKind
{
    Folder,
    Playlist,
    Track,
    Station,
}

/// <summary>Ein Knoten im Medien-Baum eines Moduls (Ordner, Playlist, Titel, Sender).</summary>
public sealed record MediaNode(
    string Id,
    string Title,
    MediaNodeKind Kind,
    string Subtitle = "",
    MediaItem? Item = null)
{
    /// <summary>Hat der Knoten Kinder, die man nachladen kann?</summary>
    public bool HasChildren => Kind is MediaNodeKind.Folder or MediaNodeKind.Playlist;
}

/// <summary>Durchsuchbarer Inhalt eines Moduls.</summary>
public interface IMediaProvider
{
    string Id { get; }
    string Title { get; }

    /// <summary>Kinder eines Knotens. Bei <c>null</c> die oberste Ebene.</summary>
    Task<IReadOnlyList<MediaNode>> GetChildrenAsync(MediaNode? parent, CancellationToken ct = default);

    Task<IReadOnlyList<MediaNode>> SearchAsync(string query, CancellationToken ct = default);
}
