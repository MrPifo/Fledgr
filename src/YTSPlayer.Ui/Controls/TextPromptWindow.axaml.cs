using Avalonia.Controls;

namespace YTSPlayer.Ui.Controls;

/// <summary>Kleiner Dialog mit einer Texteingabe. <see cref="AskAsync"/> liefert den Text oder null bei „Abbrechen“.</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow()
    {
        InitializeComponent();
        Cancel.Click += (_, _) => Close(null);
        Ok.Click += (_, _) => Close(Input.Text);
        Opened += (_, _) => Input.Focus();
    }

    /// <summary>Zeigt den Dialog über <paramref name="owner"/>. Leere Eingabe zählt wie Abbrechen.</summary>
    public static async Task<string?> AskAsync(Window owner, string title, string prompt, string okText)
    {
        var w = new TextPromptWindow { Title = title };
        w.Prompt.Text = prompt;
        w.Ok.Content = okText;
        var text = await w.ShowDialog<string?>(owner);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
