using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Material.Icons;
using Material.Icons.Avalonia;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Ui.Controls;

/// <summary>
/// Einfaches Suchfeld für die Kopfzeile: rundes Textfeld mit Lupe. Der Platzhalter kommt über einen Übersetzungs-Schlüssel
/// (<see cref="PlaceholderKey"/>) und wechselt mit der Sprache.
/// </summary>
public sealed class SearchBox : UserControl
{
    /// <summary>Der Suchtext (zwei Richtungen bindbar).</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SearchBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Schlüssel des Platzhalter-Textes.</summary>
    public static readonly StyledProperty<string> PlaceholderKeyProperty = AvaloniaProperty.Register<SearchBox, string>(nameof(PlaceholderKey), "");

    readonly TextBox _box;

    /// <summary>Baut das Feld (TextBox mit Klasse „search“ und Lupe darüber).</summary>
    public SearchBox()
    {
        _box = new TextBox { Classes = { "search" } };
        _box.Bind(TextBox.TextProperty, new Binding(nameof(Text)) { Source = this, Mode = BindingMode.TwoWay });
        var icon = new MaterialIcon { Kind = MaterialIconKind.Magnify, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        icon.Bind(MarginProperty, this.GetResourceObservable("Pad.SearchIcon"));
        icon.Bind(WidthProperty, this.GetResourceObservable("Size.IconS"));
        icon.Bind(HeightProperty, this.GetResourceObservable("Size.IconS"));
        icon.Bind(MaterialIcon.ForegroundProperty, this.GetResourceObservable("Brush.TextMuted"));
        Content = new Grid { Children = { _box, icon } };
    }

    /// <summary>Der Suchtext.</summary>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>Schlüssel des Platzhalter-Textes in den Übersetzungsdateien.</summary>
    public string PlaceholderKey { get => GetValue(PlaceholderKeyProperty); set => SetValue(PlaceholderKeyProperty, value); }

    /// <summary>Das innere Textfeld (für Tastenkürzel, die den Fokus setzen).</summary>
    public TextBox Box => _box;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlaceholderKeyProperty && change.NewValue is string key && key.Length > 0)
            _box.Bind(TextBox.PlaceholderTextProperty, new Binding($"[{key}]") { Source = Loc.Current, Mode = BindingMode.OneWay });
    }
}
