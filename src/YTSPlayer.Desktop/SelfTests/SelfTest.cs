using System.Text;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.MusicPlayer;
using YTSPlayer.Modules.MusicPlayer.Library;
using YTSPlayer.Modules.MusicPlayer.Playback;
using YTSPlayer.Modules.YtConnect;
using YTSPlayer.Modules.YtConnect.Windows;
using YTSPlayer.Platform.Windows;

namespace YTSPlayer.Desktop.SelfTests;

/// <summary>
/// Selbsttests für die Web-Ansicht (--selftest, --selftest-ytm, --selftest-login). Sie laufen mit eigenen Ordnern
/// und schreiben ihr Ergebnis neben die EXE.
/// </summary>
static class SelfTest
{
    const string Page = """
<!doctype html><html><head>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Roboto">
</head><body>
<img src="https://www.gstatic.com/images/branding/googlelogo/2x/googlelogo_color_92x30dp.png">
<audio id="a" loop></audio>
<script>
const sr = 22050, n = sr * 3, buf = new ArrayBuffer(44 + n * 2), dv = new DataView(buf);
const w = (o, s) => [...s].forEach((c, i) => dv.setUint8(o + i, c.charCodeAt(0)));
w(0, 'RIFF'); dv.setUint32(4, 36 + n * 2, true); w(8, 'WAVE'); w(12, 'fmt '); dv.setUint32(16, 16, true);
dv.setUint16(20, 1, true); dv.setUint16(22, 1, true); dv.setUint32(24, sr, true); dv.setUint32(28, sr * 2, true);
dv.setUint16(32, 2, true); dv.setUint16(34, 16, true); w(36, 'data'); dv.setUint32(40, n * 2, true);
for (let i = 0; i < n; i++) dv.setInt16(44 + i * 2, Math.sin(2 * Math.PI * 440 * i / sr) * 3000, true);
const a = document.getElementById('a');
a.src = URL.createObjectURL(new Blob([buf], { type: 'audio/wav' }));
a.volume = 0.05;
a.play();
</script></body></html>
""";

    static readonly WindowsProcessStats Stats = new();

    public static void Start(string[] args, IAppPaths paths, ISettingsStore store, Action quit)
    {
        if (args.Contains("--selftest-local"))
        {
            _ = RunLocalAsync(store, quit);
            return;
        }
        if (args.Contains("--selftest-radio"))
        {
            _ = RadioSelfTest.RunAsync(quit);
            return;
        }
        var s = new YtSettings();
        var host = new WebView2YtHost(s, paths);
        async Task Finish(string file, StringBuilder o)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, file), o.ToString());
            try { await host.StopAsync(); } catch { }
            host.CloseWindow();
            quit();
        }
        if (args.Contains("--selftest")) _ = RunAsync(s, host, o => Finish("selftest-result.txt", o));
        else if (args.Contains("--selftest-ytm")) _ = RunYtmAsync(s, host, o => Finish("selftest-ytm-result.txt", o));
        else if (args.Contains("--selftest-login")) _ = RunLoginAsync(host, o => Finish("selftest-login-result.txt", o));
        else quit();
    }

    /// <summary>Prüft das Musik-Modul ohne Oberfläche: Index, Suche, Ordner-Anzeige und Wiedergabe mit LibVLC.</summary>
    static async Task RunLocalAsync(ISettingsStore store, Action quit)
    {
        var o = new StringBuilder();
        void L(string t) => o.AppendLine(t);
        var tmp = Path.Combine(Path.GetTempPath(), "ytsp-music-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            L($"Local-Test {DateTime.Now:s}");
            Directory.CreateDirectory(Path.Combine(tmp, "Sounds", "Impacts"));
            Directory.CreateDirectory(Path.Combine(tmp, "Musik"));
            WriteTone(Path.Combine(tmp, "Sounds", "Impacts", "hit_big.wav"), 440);
            WriteTone(Path.Combine(tmp, "Sounds", "Impacts", "hit_small.wav"), 660);
            WriteTone(Path.Combine(tmp, "Musik", "song.wav"), 330);
            File.WriteAllText(Path.Combine(tmp, "Musik", "notiz.txt"), "kein Audio");

            var perf = new PerfTracker();
            var settings = new MusicSettings { Folders = [tmp] };
            var lib = new LibraryService(settings, store, perf);
            var provider = new LibraryProvider(lib);
            await lib.ScanAsync(null, CancellationToken.None);
            L($"Index: {lib.FileCount} Dateien (erwartet 3)");
            var hits = await lib.SearchAsync("impacts hit", 50, CancellationToken.None);
            L($"Suche 'impacts hit': {hits.Count} Treffer (erwartet 2): {string.Join(", ", hits.Select(h => h.Relative))}");
            var none = await lib.SearchAsync("gibtesnicht", 50, CancellationToken.None);
            L($"Suche 'gibtesnicht': {none.Count} Treffer (erwartet 0)");
            var root = await provider.GetChildrenAsync(null);
            L("Wurzel: " + string.Join(", ", root.Select(n => $"{n.Title}[{n.Kind}]")));
            var sounds = await provider.GetChildrenAsync(root.First(n => n.Id == tmp));
            L("Inhalt Ordner: " + string.Join(", ", sounds.Select(n => $"{n.Title}[{n.Kind}]")));

            var stats = new WindowsProcessStats();
            double Mb() => stats.PrivateBytes(Environment.ProcessId) / 1048576.0;
            L($"RAM vor Wiedergabe: {Mb():F1} MB");
            await using var source = new LibVlcSource(perf, LibVlcLocator.Find);
            var states = new List<string>();
            source.StateChanged += st => { lock (states) states.Add($"{(st.IsPlaying ? "play" : "pause")}@{st.Position.TotalSeconds:F1}/{st.Duration.TotalSeconds:F1} '{st.Title}'"); };
            await source.SetVolumeAsync(0.03);
            var items = hits.Select(h => LibraryService.ToItem(h.Path)).ToList();
            await source.StartAsync(items[0], items);
            await Task.Delay(2500);
            L($"Nach 2,5 s: {source.State.Position.TotalSeconds:F1}/{source.State.Duration.TotalSeconds:F1} s, spielt={source.State.IsPlaying}, Titel='{source.State.Title}'");
            L($"RAM während Wiedergabe: {Mb():F1} MB");
            await source.PauseAsync();
            await Task.Delay(500);
            L($"Nach Pause: spielt={source.State.IsPlaying}");
            await source.SeekAsync(TimeSpan.FromSeconds(1.5));
            await source.PlayAsync();
            await Task.Delay(800);
            L($"Nach Seek 1,5 s: Position {source.State.Position.TotalSeconds:F1} s");
            await source.NextAsync();
            await Task.Delay(1200);
            L($"Nach Weiter: Titel='{source.State.Title}', spielt={source.State.IsPlaying}");
            await source.StopAsync();
            await Task.Delay(500);
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            L($"RAM nach Stopp: {Mb():F1} MB");
            lock (states) L($"Zustands-Meldungen: {states.Count}, erste 5: {string.Join(" | ", states.Take(5))}");
            foreach (var e in perf.Snapshot()) L($"   Operation {e.Name}: {e.Count}x, Fehler {e.Failures}, zuletzt {e.Last.TotalMilliseconds:F0} ms");
        }
        catch (Exception ex) { L("FEHLER: " + ex); }
        try { Directory.Delete(tmp, true); } catch { }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-local-result.txt"), o.ToString());
        quit();
    }

    static void WriteTone(string path, double hz)
    {
        const int rate = 22050, seconds = 3;
        var n = rate * seconds;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8); w.Write("fmt "u8);
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        for (var i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * hz * i / rate) * 3000));
    }
    static async Task RunAsync(YtSettings s, WebView2YtHost host, Func<StringBuilder, Task> done)
    {
        var o = new StringBuilder();
        void L(string t) => o.AppendLine(t);
        try
        {
            L($"Selftest {DateTime.Now:s}");
            L($"Einstellungen: Bilder={s.BlockImages} CSS={s.BlockStyles} Fonts={s.BlockFonts} GPUaus={s.DisableGpu} V8={s.LimitV8}");
            L("Browser-Argumente: " + host.Args);
            await host.StartAsync("about:blank");
            host.NavigateToString(Page);
            await Task.Delay(6000);
            var t1 = await host.ExecAsync("document.getElementById('a').currentTime");
            await Task.Delay(5000);
            var t2 = await host.ExecAsync("document.getElementById('a').currentTime");
            var paused = await host.ExecAsync("document.getElementById('a').paused");
            L($"Audio versteckt: currentTime {t1} -> {t2}, paused={paused}");
            Dump(host, L, "Normal");
            host.SetMemoryLow(true);
            await Task.Delay(6000);
            L($"Nach MemoryUsageTargetLevel=Low: currentTime {await host.ExecAsync("document.getElementById('a').currentTime")}");
            Dump(host, L, "Low");
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            L($"Trim: {TrimAll(host)} Prozesse geleert");
            await Task.Delay(1500);
            Dump(host, L, "Low+Trim");
            await Task.Delay(20000);
            Dump(host, L, "Low+Trim +20s");
        }
        catch (Exception ex) { L("FEHLER: " + ex); }
        await done(o);
    }

    static async Task RunYtmAsync(YtSettings s, WebView2YtHost host, Func<StringBuilder, Task> done)
    {
        var o = new StringBuilder();
        void L(string t) => o.AppendLine(t);
        try
        {
            L($"YTM-Test {DateTime.Now:s}  Bilder={s.BlockImages} CSS={s.BlockStyles} Fonts={s.BlockFonts}");
            await host.StartAsync(UrlNormalizer.Home);
            await Task.Delay(20000);
            L("URL: " + await host.ExecAsync("location.href"));
            L("Titel: " + await host.ExecAsync("document.title"));
            L("Elemente: " + await host.ExecAsync("JSON.stringify({video:!!document.querySelector('video'),playerBar:!!document.querySelector('ytmusic-player-bar'),next:!!document.querySelector('ytmusic-player-bar .next-button'),prev:!!document.querySelector('ytmusic-player-bar .previous-button'),avToggle:!!document.querySelector('ytmusic-av-toggle'),song:!!document.querySelector('ytmusic-av-toggle .song-button'),mediaSession:!!navigator.mediaSession,bridge:!!window.__ytsp})"));
            Dump(host, L, "YTM geladen");
            host.SetMemoryLow(true);
            await Task.Delay(8000);
            Dump(host, L, "YTM Low");
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            L($"Trim: {TrimAll(host)} Prozesse geleert");
            await Task.Delay(1500);
            Dump(host, L, "YTM Low+Trim");
        }
        catch (Exception ex) { L("FEHLER: " + ex); }
        await done(o);
    }

    static async Task RunLoginAsync(WebView2YtHost host, Func<StringBuilder, Task> done)
    {
        var o = new StringBuilder();
        try
        {
            await host.StartAsync(UrlNormalizer.Home);
            await Task.Delay(3000);
            o.AppendLine("Versteckt:           " + host.WindowInfo());
            host.SetLoginMode(true);
            await Task.Delay(1000);
            o.AppendLine("Login ohne Fläche:   " + host.WindowInfo() + "  (die Fläche gibt es nur im Hauptfenster; hier muss das Fenster versteckt bleiben)");
            host.SetLoginMode(false);
            await Task.Delay(1000);
            o.AppendLine("Wieder versteckt:    " + host.WindowInfo());
        }
        catch (Exception ex) { o.AppendLine("FEHLER: " + ex); }
        await done(o);
    }

    static int TrimAll(WebView2YtHost host)
    {
        var ok = Stats.Trim(Environment.ProcessId) ? 1 : 0;
        foreach (var p in host.Processes()) if (Stats.Trim(p.Pid)) ok++;
        return ok;
    }

    static void Dump(WebView2YtHost host, Action<string> L, string label)
    {
        double web = 0;
        var rows = new List<(string Name, int Pid, double Mb)>();
        foreach (var p in host.Processes())
        {
            var mb = Stats.PrivateBytes(p.Pid) / 1048576.0;
            web += mb;
            rows.Add((p.Name, p.Pid, mb));
        }
        var self = Stats.PrivateBytes(Environment.ProcessId) / 1048576.0;
        L($"[{label}] WebView {web:F1} MB, Host {self:F1} MB, gesamt {web + self:F1} MB, Anfragen {host.Requests}, blockiert {host.Blocked}");
        foreach (var r in rows.OrderByDescending(r => r.Mb)) L($"   {r.Name,-24} PID {r.Pid,6}  {r.Mb,7:F1} MB");
    }
}
