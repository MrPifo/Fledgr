using Avalonia.Controls;
using YTSPlayer.Contracts.Diagnostics;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Die Web-Ansicht, in der YouTube Music läuft. Jede Plattform hat eine eigene Umsetzung
/// (Windows: WebView2, Android: Android-WebView). Alle Aufrufe kommen vom UI-Thread.
/// </summary>
public interface IYtWebHost
{
    bool IsRunning { get; }

    /// <summary>Werden Bilder, CSS und Fonts gerade blockiert?</summary>
    bool BlockActive { get; set; }

    long Requests { get; }
    long Blocked { get; }
    long Bytes { get; }

    /// <summary>Fläche in der App, in der die Web-Ansicht während der Anmeldung sichtbar ist.</summary>
    Control LoginSurface { get; }

    /// <summary>Nachricht der JS-Brücke (JSON).</summary>
    event Action<string>? Message;

    /// <summary>Eine Seite wurde fertig geladen (URL).</summary>
    event Action<Uri?>? Navigated;

    /// <summary>Der Web-Prozess ist abgestürzt.</summary>
    event Action? Crashed;

    /// <summary>Startet die Web-Ansicht versteckt und lädt die Adresse. Läuft sie schon, wird nur navigiert.</summary>
    Task StartAsync(string url, CancellationToken ct = default);

    Task StopAsync();
    void Navigate(string url);

    /// <summary>Navigiert und wartet, bis die Seite geladen ist (oder die Zeit abläuft).</summary>
    Task NavigateAndWaitAsync(string url, int timeoutMs = 20000);

    /// <summary>Führt ein Skript aus und liefert das Ergebnis als JSON.</summary>
    Task<string> ExecAsync(string script);

    void SetMemoryLow(bool low);
    IReadOnlyList<ProcessRef> Processes();

    /// <summary>
    /// true: Web-Ansicht in der <see cref="LoginSurface"/> zeigen, Blockade aus. false: wieder verstecken, Blockade an.
    /// </summary>
    void SetLoginMode(bool on);

    /// <summary>Löscht Cookies und lokale Daten (Abmelden).</summary>
    Task ClearDataAsync();
}
