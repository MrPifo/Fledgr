using Avalonia.Controls.Templates;

namespace YTSPlayer.Ui;

/// <summary>
/// Ein Plugin, das eigene Views mitbringt, meldet hier seine DataTemplates (ViewModel → View) an.
/// Die Shell trägt sie beim Aktivieren ein und beim Deaktivieren wieder aus.
/// </summary>
public interface IViewProvider
{
    IEnumerable<IDataTemplate> GetTemplates();
}
