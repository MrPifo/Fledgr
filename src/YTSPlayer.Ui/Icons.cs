using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace YTSPlayer.Ui;

/// <summary>
/// Alle Icon-Namen der App (Namen aus Material Design Icons). NIEMALS Unicode-Zeichen oder Emojis als Icons nutzen.
/// ViewModels und Plugins geben nur diese Namen weiter, die Views zeigen sie über <see cref="IconConverters.Kind"/> oder direkt als MaterialIcon.
/// Jeder Name hier wird im Test IconNamesTests gegen die Bibliothek geprüft.
/// </summary>
public static class Icons
{
    public const string Music = "MusicNote";
    public const string Cloud = "Cloud";
    public const string Settings = "Cog";
    public const string Radio = "Radio";
    public const string RadioMix = "RadioTower";
    public const string Shuffle = "Shuffle";
    public const string ShuffleOff = "ShuffleDisabled";
    public const string Like = "ThumbUp";
    public const string LikeOff = "ThumbUpOutline";
    public const string Dislike = "ThumbDown";
    public const string DislikeOff = "ThumbDownOutline";
    public const string Sleep = "Sleep";
    public const string SleepOff = "SleepOff";
    public const string Play = "Play";
    public const string Pause = "Pause";
    public const string Next = "SkipNext";
    public const string Previous = "SkipPrevious";
    public const string Volume = "VolumeHigh";
    public const string VolumeOff = "VolumeOff";
    public const string OpenExternal = "OpenInNew";
    public const string Close = "Close";
    public const string Expand = "ArrowExpand";
    public const string Widget = "PictureInPictureBottomRight";
    public const string Up = "ArrowUp";
    public const string Back = "ChevronLeft";
    public const string Star = "Star";
    public const string StarOutline = "StarOutline";
    public const string Folder = "Folder";
    public const string Playlist = "PlaylistMusic";
    public const string Track = "Music";
    public const string Station = "RadioTower";
    public const string Plus = "Plus";
    public const string Alert = "Alert";
    public const string Youtube = "Youtube";
    public const string Library = "Library";
    public const string Heart = "Heart";
    public const string HeartOutline = "HeartOutline";
    public const string Search = "Magnify";
    public const string Diagnostics = "Pulse";
    public const string Power = "Power";
    public const string More = "DotsVertical";
    public const string Lyrics = "TextLong";
    public const string Logout = "Logout";
    public const string Tune = "Tune";
    public const string Home = "Home";
    public const string PlayOutline = "PlayOutline";
    public const string History = "History";
    public const string Delete = "DeleteOutline";

    /// <summary>Alle Namen (für den Test, dass die Bibliothek sie kennt).</summary>
    public static readonly string[] All =
    [
        Music, Cloud, Settings, Radio, RadioMix, Shuffle, ShuffleOff, Like, LikeOff, Dislike, DislikeOff, Sleep, SleepOff, Play, Pause, Next, Previous, Volume, VolumeOff, OpenExternal,
        Close, Expand, Widget, Up, Back, Star, StarOutline, Folder, Playlist, Track, Station, Plus, Alert,
        Youtube, Library, Heart, HeartOutline, Search, Diagnostics, Power, More, Lyrics, Logout, Tune, Home, PlayOutline, History, Delete,
    ];
}

/// <summary>Umwandler für Bindungen: Icon-Name (Text) zu <see cref="MaterialIconKind"/>.</summary>
public static class IconConverters
{
    /// <summary>Text → Icon. Ein unbekannter Name zeigt kein Icon statt zu fehlen.</summary>
    public static readonly IValueConverter Kind = new FuncValueConverter<string?, MaterialIconKind>(
        name => name != null && Enum.TryParse<MaterialIconKind>(name, out var kind) ? kind : MaterialIconKind.Help);
}
