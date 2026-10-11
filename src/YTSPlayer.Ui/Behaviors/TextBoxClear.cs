using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace YTSPlayer.Ui.Behaviors;

/// <summary>
/// Globale Regel: Jedes Textfeld bekommt rechts einen kleinen X-Knopf, der den Inhalt leert. Er erscheint nur, wenn Text drinsteht.
/// Felder in Zahlenfeldern und Felder mit eigenem Inhalt rechts bleiben unberührt.
/// </summary>
public static class TextBoxClear
{
    static bool _installed;

    /// <summary>Hängt die Regel an alle Textfelder der App. Mehrfaches Aufrufen ist harmlos.</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Control.LoadedEvent.AddClassHandler<TextBox>(OnLoaded);
    }

    static void OnLoaded(TextBox box, RoutedEventArgs e)
    {
        if (box.InnerRightContent != null || box.IsReadOnly || box.PasswordChar != default || box.FindAncestorOfType<NumericUpDown>() != null) return;

        var icon = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 14, Height = 14 };
        var button = new Button { Classes = { "icon", "ghost", "compact" }, Content = icon, Focusable = false, IsVisible = !string.IsNullOrEmpty(box.Text) };
        button.Click += (_, _) =>
        {
            box.Text = "";
            box.Focus();
        };
        box.InnerRightContent = button;
        box.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(button));
    }

    sealed class TextObserver(Button button) : IObserver<string?>
    {
        public void OnNext(string? value) => button.IsVisible = !string.IsNullOrEmpty(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
