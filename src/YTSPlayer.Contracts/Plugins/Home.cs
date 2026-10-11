namespace YTSPlayer.Contracts.Plugins;

/// <summary>Ein Knopf in der Kopfzeile eines Home-Abschnitts. <paramref name="Text"/> ist ein Übersetzungs-Schlüssel, <paramref name="Icon"/> ein Name aus der Icon-Liste.</summary>
public sealed record HomeAction(string Text, string Icon, Action Invoke);

/// <summary>
/// Ein Abschnitt der Home-Seite. Das Modul liefert das ViewModel; die View dazu meldet es wie bei Seiten über <c>IViewProvider</c>.
/// <paramref name="Title"/> ist ein Übersetzungs-Schlüssel. <paramref name="Tone"/> wie bei <see cref="NavPage"/> (music, yt, radio, accent).
/// <paramref name="PageId"/> ist die Seite, die „Alle anzeigen“ öffnet (null = kein Link).
/// </summary>
public sealed record HomeSection(string Id, string Title, string Tone, int Order, object ViewModel, string? PageId = null, IReadOnlyList<HomeAction>? Actions = null);

/// <summary>
/// Optional für das ViewModel eines Home-Abschnitts: sagt, ob es etwas zu zeigen gibt. Ohne Inhalt blendet Home den Abschnitt aus.
/// Das ViewModel meldet Änderungen über <c>INotifyPropertyChanged</c> (Eigenschaft <see cref="HasContent"/>).
/// </summary>
public interface IHomeContent
{
    /// <summary>Wahr, wenn der Abschnitt etwas zeigt.</summary>
    bool HasContent { get; }
}
