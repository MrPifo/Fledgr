using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace YTSPlayer.Ui.Controls;

/// <summary>Rundes Profilbild mit Anfangsbuchstaben als Ersatz. Größe über Classes="small|medium".</summary>
public class Avatar : TemplatedControl
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<Avatar, IImage?>(nameof(Source));
    public static readonly StyledProperty<string?> InitialProperty = AvaloniaProperty.Register<Avatar, string?>(nameof(Initial));

    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public string? Initial { get => GetValue(InitialProperty); set => SetValue(InitialProperty, value); }
}
