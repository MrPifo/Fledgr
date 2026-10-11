using System.Globalization;
using System.Text.Json;
using YTSPlayer.Contracts.Diagnostics;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Gemeinsamer Zugriff auf die Web-Ansicht für Wiedergabe, Anmeldung und Playlists.
/// Die Web-Ansicht wird erst beim ersten Bedarf gestartet.
/// </summary>
public sealed class YtEngine(Func<IYtWebHost> factory)
{
    IYtWebHost? _host;

    /// <summary>Die Web-Ansicht (wird beim ersten Zugriff als Objekt angelegt, aber noch nicht gestartet).</summary>
    public IYtWebHost Host => _host ??= Create();

    public bool IsRunning => _host?.IsRunning == true;

    /// <summary>true, solange die Web-Ansicht für die Anmeldung sichtbar ist. Dann gilt kein Leerlauf-Abbau.</summary>
    public bool LoginMode { get; private set; }

    public event Action<string>? Message;
    public event Action<Uri?>? Navigated;
    public event Action? Crashed;

    IYtWebHost Create()
    {
        var h = factory();
        h.Message += json => Message?.Invoke(json);
        h.Navigated += u => Navigated?.Invoke(u);
        h.Crashed += () => Crashed?.Invoke();
        return h;
    }

    Task? _starting;

    /// <summary>Startet die Web-Ansicht mit dieser Adresse oder navigiert dorthin, wenn sie schon läuft.</summary>
    public Task StartAsync(string url) => _starting = Host.StartAsync(url);

    /// <summary>
    /// Startet die Web-Ansicht nur, wenn sie weder läuft noch gerade startet. Startet sie gerade (z. B. Wiedergabe beim App-Start),
    /// wird nur gewartet und nicht wegnavigiert. True, wenn dieser Aufruf sie gestartet hat.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(string url)
    {
        if (IsRunning) return false;
        if (_starting is { IsCompleted: false } running)
        {
            try { await running; } catch { }
            return false;
        }
        await StartAsync(url);
        return true;
    }

    /// <summary>Beendet die Web-Ansicht und gibt das Host-Fenster endgültig frei (beim Ausschalten des Plugins).</summary>
    public async Task DisposeHostAsync()
    {
        if (_host == null) return;
        await StopAsync();
        if (_host is IDisposable d) d.Dispose();
        _host = null;
    }

    public async Task StopAsync()
    {
        if (_host == null) return;
        LoginMode = false;
        await _host.StopAsync();
    }

    public void SetLoginMode(bool on)
    {
        LoginMode = on;
        Host.SetLoginMode(on);
    }

    public void Navigate(string url) => _host?.Navigate(url);

    public Task<string> ExecAsync(string script) => _host == null ? Task.FromResult("") : _host.ExecAsync(script);

    /// <summary>Führt ein Skript aus, das einen String zurückgibt, und liefert diesen String.</summary>
    public async Task<string> ExecStringAsync(string script)
    {
        var raw = await ExecAsync(script);
        try { return JsonSerializer.Deserialize<string>(raw) ?? ""; }
        catch { return ""; }
    }

    /// <summary>Sendet einen Befehl an die JS-Brücke (play, pause, seek, volume, next, prev).</summary>
    public void Cmd(string command, double arg = 0) =>
        _ = ExecAsync($"window.__ytsp&&window.__ytsp.cmd('{command}',{arg.ToString(CultureInfo.InvariantCulture)})");

    public IReadOnlyList<ProcessRef> Processes() => _host?.Processes() ?? [];
}
