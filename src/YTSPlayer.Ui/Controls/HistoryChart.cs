using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace YTSPlayer.Ui.Controls;

/// <summary>Einfaches Liniendiagramm für den RAM-Verlauf mit gestrichelter Zielmarke. Farben kommen aus der Palette.</summary>
public sealed class HistoryChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<HistoryChart, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<double> TargetProperty = AvaloniaProperty.Register<HistoryChart, double>(nameof(Target));

    static HistoryChart() => AffectsRender<HistoryChart>(ValuesProperty, TargetProperty);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.DrawRectangle(Brush("Brush.Surface1", Brushes.DimGray), null, new Rect(0, 0, w, h));
        var v = Values;
        if (v is not { Count: > 1 }) return;
        var max = Math.Max(Target * 1.2, v.Max() * 1.1);
        if (max <= 0) return;
        double Y(double x) => h - x / max * h;
        var ty = Y(Target);
        ctx.DrawLine(new Pen(Brush("Brush.Warning", Brushes.Orange), 1, new DashStyle([4, 4], 0)), new Point(0, ty), new Point(w, ty));
        var pen = new Pen(Brush("Brush.Accent", Brushes.DeepSkyBlue), 2);
        var step = w / (v.Count - 1);
        for (var i = 1; i < v.Count; i++)
            ctx.DrawLine(pen, new Point((i - 1) * step, Y(v[i - 1])), new Point(i * step, Y(v[i])));
    }
}
