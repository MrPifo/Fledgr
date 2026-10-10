using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using YTSPlayer.Contracts;

namespace YTSPlayer.Ui.Controls;

/// <summary>
/// Zeigt je nach <see cref="State"/> den Inhalt, eine Lade-Anzeige, einen Leer-Text oder einen Fehler mit „Erneut versuchen“.
/// Alle Seiten mit Netzwerk oder Scan nutzen dieses Control.
/// </summary>
public class LoadingState : ContentControl
{
    public static readonly StyledProperty<LoadState> StateProperty = AvaloniaProperty.Register<LoadingState, LoadState>(nameof(State), LoadState.Ready);
    public static readonly StyledProperty<string?> MessageProperty = AvaloniaProperty.Register<LoadingState, string?>(nameof(Message));
    public static readonly StyledProperty<string?> ErrorTextProperty = AvaloniaProperty.Register<LoadingState, string?>(nameof(ErrorText));
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<LoadingState, double>(nameof(Progress));
    public static readonly StyledProperty<bool> IsIndeterminateProperty = AvaloniaProperty.Register<LoadingState, bool>(nameof(IsIndeterminate), true);
    public static readonly StyledProperty<ICommand?> RetryCommandProperty = AvaloniaProperty.Register<LoadingState, ICommand?>(nameof(RetryCommand));

    public LoadingState() => UpdatePseudoClasses(State);

    public LoadState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>Text unter dem Balken (Laden) oder Leer-Text (z. B. „Keine Treffer“).</summary>
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

    public string? ErrorText { get => GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

    /// <summary>Fortschritt 0 bis 100. Wird nur genutzt, wenn <see cref="IsIndeterminate"/> aus ist.</summary>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <summary>true = Dauer unbekannt (laufender Balken), false = Prozentbalken.</summary>
    public bool IsIndeterminate { get => GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }

    public ICommand? RetryCommand { get => GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty) UpdatePseudoClasses(change.GetNewValue<LoadState>());
    }

    void UpdatePseudoClasses(LoadState s)
    {
        PseudoClasses.Set(":loading", s == LoadState.Loading);
        PseudoClasses.Set(":empty", s == LoadState.Empty);
        PseudoClasses.Set(":error", s == LoadState.Error);
        PseudoClasses.Set(":ready", s is LoadState.Ready or LoadState.Idle);
    }
}
