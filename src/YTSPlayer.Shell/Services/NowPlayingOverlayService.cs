using Avalonia;
using Avalonia.Threading;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Shell.Views;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Blendet beim Songwechsel Titel, Künstler und Jahr oben über dem ganzen Bildschirm ein, auch über anderen Programmen.
/// Nach 5 s verschwindet die Anzeige. Die Einstellung gilt je Quelle. Tags und Dauer kommen oft nach, darum wartet sie kurz.
/// </summary>
public sealed class NowPlayingOverlayService
{
    const double StripHeight = 80;
    readonly AppSettings _settings;
    readonly PlayerService _player;
    readonly IWindowEffects? _effects;
    readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(800) };
    readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(5) };
    NowPlayingOverlay? _window;
    string? _key;

    public NowPlayingOverlayService(AppSettings settings, PlayerService player, IWindowEffects? effects)
    {
        _settings = settings;
        _player = player;
        _effects = effects;
        player.StateChanged += OnState;
        _settle.Tick += (_, _) => { _settle.Stop(); ShowNow(); };
        _hide.Tick += (_, _) => { _hide.Stop(); _window?.Hide(); };
    }

    void OnState(PlaybackState st)
    {
        var key = st.Item == null ? null : st.Item.SourceId + "|" + st.Item.Id + "|" + st.Title;
        if (key == null || key == _key) return;
        _key = key;
        _settle.Stop();
        if (st.IsPlaying) _settle.Start();
    }

    void ShowNow()
    {
        var st = _player.State;
        var sourceId = _player.ActiveSourceId;
        if (!st.IsPlaying || string.IsNullOrEmpty(st.Title) || sourceId == null || !_settings.NowPlayingShown(sourceId)) return;

        var line = st.Title;
        if (st.Artist.Length > 0) line += " – " + st.Artist;
        if (st.Year > 0) line += " (" + st.Year + ")";

        _window ??= new NowPlayingOverlay(_effects);
        _window.SetText(line);
        if (!_window.IsVisible) _window.Show();
        PlaceOverStrip(_window);
        _hide.Stop();
        _hide.Start();
    }

    /// <summary>Legt das Fenster als Streifen oben über die ganze Breite des Hauptbildschirms.</summary>
    static void PlaceOverStrip(NowPlayingOverlay window)
    {
        var screen = window.Screens.Primary;
        if (screen == null) return;
        var bounds = screen.Bounds;
        window.Position = new PixelPoint(bounds.X, bounds.Y);
        window.Width = bounds.Width / screen.Scaling;
        window.Height = StripHeight;
    }
}
