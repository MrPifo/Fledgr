using Avalonia.Controls;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Views;

/// <summary>Titel-Anzeige über allen Programmen. Klicks gehen durch, der Fokus bleibt beim Programm des Nutzers.</summary>
public partial class NowPlayingOverlay : Window
{
    readonly IWindowEffects? _effects;

    public NowPlayingOverlay() : this(null)
    {
    }

    public NowPlayingOverlay(IWindowEffects? effects)
    {
        _effects = effects;
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (TryGetPlatformHandle() is { } h) _effects?.MakeClickThroughOverlay(h.Handle);
        };
    }

    /// <summary>Setzt den Text der Anzeige.</summary>
    public void SetText(string text) => Line.Text = text;
}
