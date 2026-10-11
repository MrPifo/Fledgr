using Avalonia;
using Avalonia.Controls;

namespace YTSPlayer.Ui.Controls;

/// <summary>
/// Drei kleine Balken für „dieser Titel ist der aktuelle“. Mit <see cref="Playing"/> bewegen sie sich, sonst stehen sie still.
/// Das Aussehen steckt in den Stilen von <c>StackPanel.bars</c> (Surfaces.axaml).
/// </summary>
public sealed class PlayingBars : UserControl
{
    public static readonly StyledProperty<bool> PlayingProperty = AvaloniaProperty.Register<PlayingBars, bool>(nameof(Playing));

    readonly StackPanel _bars = new() { Classes = { "bars", "accent" } };

    public PlayingBars()
    {
        _bars.Children.Add(new Border { Classes = { "bar", "b1" } });
        _bars.Children.Add(new Border { Classes = { "bar", "b2" } });
        _bars.Children.Add(new Border { Classes = { "bar", "b3" } });
        Content = _bars;
        IsHitTestVisible = false;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <summary>Wahr, solange der Titel wirklich spielt (nicht pausiert).</summary>
    public bool Playing { get => GetValue(PlayingProperty); set => SetValue(PlayingProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayingProperty) _bars.Classes.Set("on", Playing);
    }
}
