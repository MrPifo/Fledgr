using Avalonia.Controls;
using Avalonia.Input;
using YTSPlayer.Shell.ViewModels;

namespace YTSPlayer.Shell.Views;

/// <summary>Klick aufs Cover (Hauptfenster und Widget): öffnet die Adresse der Quelle im Standard-Browser.</summary>
static class CoverLink
{
    /// <summary>Öffnet die Adresse, die die Quelle anbietet (z. B. den Titel auf YouTube). Nur http und https, nur mit linker Maustaste.</summary>
    public static async void Open(TopLevel top, ShellViewModel vm, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(top).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (vm.ExternalUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try { await top.Launcher.LaunchUriAsync(uri); }
        catch (Exception) { vm.Status = "Der Browser konnte nicht geöffnet werden."; }
    }
}
