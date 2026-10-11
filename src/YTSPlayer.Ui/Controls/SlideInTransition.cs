using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;

namespace YTSPlayer.Ui.Controls;

/// <summary>
/// Seitenwechsel: die neue Seite blendet ein und schiebt sich ein kleines Stück (<see cref="Distance"/>) von links nach rechts ein.
/// Die alte Seite wird sofort ausgeblendet. Läuft von Hand pro Bild, damit die neue Seite auch bei einem Fehler sichtbar bleibt.
/// </summary>
public sealed class SlideInTransition : IPageTransition
{
    /// <summary>Dauer in Sekunden.</summary>
    public double Seconds { get; set; } = 0.22;

    /// <summary>Weg in Pixeln, den die neue Seite von links zurücklegt.</summary>
    public double Distance { get; set; } = 28;

    public Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (from != null) from.IsVisible = false;
        if (to == null) return Task.CompletedTask;
        to.IsVisible = true;
        return Play(to, Seconds, Distance, cancellationToken);
    }

    /// <summary>Blendet <paramref name="target"/> ein und schiebt es um <paramref name="distance"/> von links an seinen Platz. Am Ende ist es immer voll sichtbar.</summary>
    public static async Task Play(Visual target, double seconds, double distance, CancellationToken cancellationToken = default)
    {
        try
        {
            var shift = new TranslateTransform(-distance, 0);
            target.RenderTransform = shift;
            target.Opacity = 0;
            var clock = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                var t = Math.Min(1, clock.Elapsed.TotalSeconds / seconds);
                var eased = 1 - Math.Pow(1 - t, 3);
                shift.X = -distance * (1 - eased);
                target.Opacity = eased;
                if (t >= 1) break;
                await Task.Delay(8, CancellationToken.None);
            }
        }
        finally
        {
            target.Opacity = 1;
            target.RenderTransform = null;
        }
    }
}
