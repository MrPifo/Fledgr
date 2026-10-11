using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Ui.Localization;

/// <summary>
/// Übersetzter Text in XAML: <c>Text="{loc:T shell.nav.settings}"</c>. Der Text wechselt sofort, wenn die Sprache wechselt.
/// Parameter gehören ins ViewModel (<see cref="ILocalizer.Format"/>), nicht ins XAML.
/// </summary>
public sealed class TExtension : MarkupExtension
{
    /// <summary>Leere Erweiterung (Schlüssel folgt als Eigenschaft).</summary>
    public TExtension() { }

    /// <summary>Erweiterung mit Schlüssel.</summary>
    public TExtension(string key) => Key = key;

    /// <summary>Schlüssel in den Übersetzungsdateien.</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new ReflectionBindingExtension($"[{Key}]")
        {
            Mode = BindingMode.OneWay,
            Source = Loc.Current,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
