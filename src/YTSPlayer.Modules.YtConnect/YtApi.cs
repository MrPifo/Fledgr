using System.Text.Json;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Modules.YtConnect.Scripts;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Fragt die InnerTube-Schnittstelle von YouTube Music über die Web-Ansicht ab (angemeldet, wie die Seite selbst).
/// Die Web-Ansicht muss auf einer music.youtube.com-Seite laufen.
/// </summary>
public sealed class YtApi
{
    readonly YtEngine _engine;
    readonly Dictionary<string, TaskCompletionSource<JsonElement>> _pending = [];

    public YtApi(YtEngine engine)
    {
        _engine = engine;
        engine.Message += OnMessage;
    }

    /// <summary>
    /// Schickt eine Anfrage und wartet auf die Antwort. Status 0 heißt: nicht gesendet, dann steht der Grund im Text.
    /// </summary>
    public async Task<(int Status, string Body)> CallAsync(string path, object body, CancellationToken ct = default)
    {
        var rid = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[rid] = tcs;
        try
        {
            await _engine.ExecAsync(ApiScript.Build(rid, path, JsonSerializer.Serialize(body)));
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), ct));
            ct.ThrowIfCancellationRequested();
            if (done != tcs.Task)
            {
                AppLog.Warn("YouTube", $"Keine Antwort auf {path}");
                return (0, "YouTube Music antwortet nicht.");
            }
            var r = tcs.Task.Result;
            var status = r.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
            var text = r.TryGetProperty(status == 0 ? "error" : "body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            // Nur Pfad und Status, nie Inhalt oder Kopfzeilen.
            if (status != 200) AppLog.Warn("YouTube", $"{path}: Status {status}" + (status == 0 ? " (" + text + ")" : ""));
            return (status, text);
        }
        finally
        {
            lock (_pending) _pending.Remove(rid);
        }
    }

    void OnMessage(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            var r = d.RootElement;
            if (r.GetProperty("t").GetString() != "api") return;
            TaskCompletionSource<JsonElement>? tcs;
            lock (_pending) _pending.TryGetValue(r.GetProperty("rid").GetString() ?? "", out tcs);
            tcs?.TrySetResult(r.Clone());
        }
        catch { }
    }
}
