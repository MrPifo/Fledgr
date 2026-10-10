using Avalonia.Controls;
using Avalonia.Controls.Templates;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Ui;

namespace YTSPlayer.Modules.RadioPlayer;

/// <summary>Platzhalter-ViewModel der Radio-Seite.</summary>
public sealed class RadioPlaceholderViewModel
{
    public string Text => "Das Radio-Modul kommt später. Die Schnittstellen (Sender-Katalog, Abgleich, Stream-Quelle) sind vorbereitet.";
}

/// <summary>Plugin „Radio“. Noch ohne Funktion: zeigt nur einen Hinweis. Standardmäßig aus.</summary>
public sealed class RadioPlayerPlugin : IPlugin, IViewProvider
{
    public string Id => "radio";
    public string DisplayName => "Radio (kommt später)";
    public bool IsCore => false;
    public bool EnabledByDefault => false;

    public IEnumerable<IDataTemplate> GetTemplates() =>
        [new FuncDataTemplate<RadioPlaceholderViewModel>((vm, _) =>
            new TextBlock { Text = vm?.Text, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap })];

    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        context.AddPage(new NavPage("radio", "Radio", Icons.Radio, new RadioPlaceholderViewModel(), 30));
        return Task.CompletedTask;
    }

    public Task DeactivateAsync() => Task.CompletedTask;
}
