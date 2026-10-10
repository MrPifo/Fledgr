using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Merkt Position (und bei Bedarf Größe und „maximiert“) eines Fensters und stellt sie beim nächsten Start wieder her.
/// Liegt die gemerkte Stelle auf keinem Bildschirm mehr (z. B. Monitor abgesteckt), bleibt das Fenster in der Mitte.
/// </summary>
public static class WindowStateKeeper
{
    /// <summary>Setzt die gemerkte Lage und hängt das Merken an. Muss vor dem ersten Anzeigen aufgerufen werden.</summary>
    /// <param name="trackSize">Auch Größe und „maximiert“ merken (das Widget bestimmt seine Größe selbst).</param>
    public static void Bind(Window window, Func<WindowPlacement?> get, Action<WindowPlacement> set, Action save, bool trackSize)
    {
        Restore(window, get(), trackSize);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Capture(window, get(), trackSize) is { } p)
            {
                set(p);
                save();
            }
        };
        void Changed() { timer.Stop(); timer.Start(); }
        window.PositionChanged += (_, _) => Changed();
        if (trackSize)
        {
            window.Resized += (_, _) => Changed();
            window.PropertyChanged += (_, e) => { if (e.Property == Window.WindowStateProperty) Changed(); };
        }
    }

    static void Restore(Window window, WindowPlacement? p, bool trackSize)
    {
        if (p == null) return;
        if (trackSize && p.Width >= window.MinWidth && p.Height >= window.MinHeight && p.Width > 0 && p.Height > 0)
        {
            window.Width = p.Width;
            window.Height = p.Height;
        }
        if (OnAnyScreen(window, p))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(p.X, p.Y);
        }
        if (trackSize && p.Maximized) window.WindowState = WindowState.Maximized;
    }

    /// <summary>Liegt ein Stück der Titelleiste auf einem Bildschirm? Sonst käme man nicht mehr ans Fenster.</summary>
    static bool OnAnyScreen(Window window, WindowPlacement p)
    {
        try
        {
            var probe = new PixelPoint(p.X + 60, p.Y + 20);
            return window.Screens.All.Any(s => s.WorkingArea.Contains(probe));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Die aktuelle Lage oder null, wenn gerade nichts zu merken ist (minimiert: da hat das Fenster eine Fantasie-Position).</summary>
    static WindowPlacement? Capture(Window window, WindowPlacement? previous, bool trackSize)
    {
        if (window.WindowState == WindowState.Minimized) return null;
        var p = previous ?? new WindowPlacement();
        if (trackSize) p.Maximized = window.WindowState == WindowState.Maximized;
        // Maximiert ist die Position die des vollen Bildschirms. Dann bleibt die zuletzt gemerkte „normale“ Lage stehen.
        if (window.WindowState == WindowState.Normal)
        {
            p.X = window.Position.X;
            p.Y = window.Position.Y;
            if (trackSize)
            {
                p.Width = window.ClientSize.Width;
                p.Height = window.ClientSize.Height;
            }
        }
        return p;
    }
}
