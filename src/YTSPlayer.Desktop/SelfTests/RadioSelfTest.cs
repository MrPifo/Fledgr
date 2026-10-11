using System.Text;
using Fledgr.Audio;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.RadioPlayer;
using YTSPlayer.Modules.RadioPlayer.Data;
using YTSPlayer.Modules.RadioPlayer.Playback;
using YTSPlayer.Platform.Windows;
using YTSPlayer.Shell.Services;

namespace YTSPlayer.Desktop.SelfTests;

/// <summary>
/// Prüft das Radio-Modul ohne Oberfläche: Sender-Suche bei radio-browser.info, ein echter Stream, Pause und die beiden
/// Fehlerpfade („Stream antwortet nicht“, „Sender weg“). Eigener Temp-Ordner, die echte radio.json bleibt unberührt.
/// </summary>
static class RadioSelfTest
{
    public static async Task RunAsync(Action quit)
    {
        var o = new StringBuilder();
        void L(string t) => o.AppendLine(t);
        var tmp = Path.Combine(Path.GetTempPath(), "ytsp-radio-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        RadioStreamSource? source = null;
        try
        {
            L($"Radio-Test {DateTime.Now:s}");
            var perf = new PerfTracker();
            var store = new RadioStore(new JsonSettingsStore(new AppPaths(tmp, tmp, tmp)));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Fledgr-Selftest/1.0");
            var client = new RadioBrowserClient(http);

            var found = await client.SearchAsync("wdr 2", "", "", CancellationToken.None);
            L($"Suche 'wdr 2': {found.Count} Sender");
            var station = found.First(s => RadioUrls.IsHttp(s.StreamUrl));
            L($"Gewählt: {station.Name} · {station.TechnicalInfo}");

            source = new RadioStreamSource(new LibVlcEngine(LibVlcLocator.Find), store, client, perf);
            source.StatusChanged += () => L("  (Status eines Senders geändert)");

            // 1. Echter Stream: starten, warten, Pause.
            store.MarkPlayed(station);
            var item = RadioStreamSource.ToItem(station);
            await source.StartAsync(item, [item]);
            await Task.Delay(8000);
            L($"Nach 8 s: spielt={source.State.IsPlaying}, Titel='{source.State.Title}', Artist='{source.State.Artist}'");
            await source.PauseAsync();
            await Task.Delay(500);
            L($"Nach Pause: spielt={source.State.IsPlaying}");

            // 2. Tote Adresse, nicht in der Datenbank: „Stream antwortet nicht“.
            var dead = new MediaItem(RadioStreamSource.SourceKey, "http://127.0.0.1:9/nichts", "Tot-Test");
            var t0 = DateTime.UtcNow;
            await source.StartAsync(dead);
            var ok = await WaitForFailureAsync(source, 25000);
            L($"Tote Adresse ({(ok ? "gemeldet" : "KEINE Meldung")}) nach {(DateTime.UtcNow - t0).TotalSeconds:F1} s: spielt={source.State.IsPlaying}, Artist='{source.State.Artist}'");

            // 3. Sender aus der Datenbank, der nicht mehr existiert: „Sender weg“ und Status im Speicher.
            var gone = new RadioStation("00000000-0000-0000-0000-000000000000", "Weg-Test", "http://127.0.0.1:9/weg", "Testland");
            store.MarkPlayed(gone);
            t0 = DateTime.UtcNow;
            await source.StartAsync(RadioStreamSource.ToItem(gone));
            ok = await WaitForFailureAsync(source, 25000);
            L($"Weg-Test ({(ok ? "gemeldet" : "KEINE Meldung")}) nach {(DateTime.UtcNow - t0).TotalSeconds:F1} s: Artist='{source.State.Artist}', Status='{store.Find(gone.StreamUrl)?.Status}'");

            // 4. Adresse hat sich geändert: die alte ist tot, die Datenbank kennt die neue. Die App soll die neue versuchen.
            var moved = station with { StreamUrl = "http://127.0.0.1:9/alt" };
            store.MarkPlayed(moved);
            t0 = DateTime.UtcNow;
            await source.StartAsync(RadioStreamSource.ToItem(moved));
            await Task.Delay(20000);
            L($"Adresse geändert nach 20 s: spielt={source.State.IsPlaying}, Artist='{source.State.Artist}', alte Adresse im Speicher={store.Find(moved.StreamUrl) != null}, neue Adresse im Speicher={store.Find(station.StreamUrl)?.Status}");

            await source.StopAsync();

            // 5. Lautstärke: jede Quelle merkt sich ihre eigene, wie bei Musik und YouTube. Radio 30 %, Test-Quelle 80 %.
            var appSettings = new AppSettings();
            var player = new PlayerService(appSettings, perf);
            var other = new SilentSource();
            player.Register(source);
            player.Register(other);
            var otherItem = new MediaItem(other.Id, "test", "Test");
            await player.PlayAsync(item, [item]);
            await player.SetVolumeAsync(0.3);
            await player.PlayAsync(otherItem);
            await player.SetVolumeAsync(0.8);
            await player.PlayAsync(item, [item]);
            L($"Radio zurück: Lautstärke {player.Volume:F2} (erwartet 0,30), Quelle selbst {source.State.Volume:F2}");
            await player.PlayAsync(otherItem);
            L($"Test-Quelle zurück: Lautstärke {player.Volume:F2} (erwartet 0,80)");
            await player.StopAsync();
            L($"Gemerkt: radio {appSettings.SourceVolumes.GetValueOrDefault("radio"):F2}, {other.Id} {appSettings.SourceVolumes.GetValueOrDefault(other.Id):F2}");

            // 6. ICY-Titel: Ö3 sendet „Künstler - Titel“. 25 s mitlesen und jeden Zustand festhalten.
            var oe3 = (await client.SearchAsync("Ö3", "AT", "", CancellationToken.None)).FirstOrDefault(s => s.Name.Contains("Ö3"));
            if (oe3 != null)
            {
                var oeItem = RadioStreamSource.ToItem(oe3);
                var seen = new List<string>();
                source.StateChanged += st => { lock (seen) seen.Add($"Titel '{st.Title}' | Künstler '{st.Artist}' | Album '{st.Album}' | spielt={st.IsPlaying}"); };
                await source.StartAsync(oeItem, [oeItem]);
                await Task.Delay(25000);
                lock (seen)
                {
                    var distinct = seen.Distinct().ToList();
                    L($"Ö3 ({oe3.Name}): {distinct.Count} verschiedene Zustände in 25 s");
                    foreach (var s in distinct.Take(8)) L("  " + s);
                }
                await source.StopAsync();
            }
            else
            {
                L("Ö3 nicht in der Suche gefunden");
            }
        }
        catch (Exception ex) { L("FEHLER: " + ex); }
        try { if (source != null) await source.DisposeAsync(); } catch { }
        try { Directory.Delete(tmp, true); } catch { }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-radio-result.txt"), o.ToString());
        quit();
    }

    /// <summary>Zweite Quelle nur für den Lautstärke-Test. Spielt nichts ab.</summary>
    sealed class SilentSource : IPlaybackSource
    {
        PlaybackState _state = PlaybackState.Empty;

        public string Id => "silent-test";
        public string DisplayName => "Test";
        public PlaybackCapabilities Capabilities => PlaybackCapabilities.Volume;
        public PlaybackState State => _state;
        public event Action<PlaybackState>? StateChanged;

        public Task StartAsync(MediaItem item, IReadOnlyList<MediaItem>? queue = null, CancellationToken ct = default)
        {
            Set(new PlaybackState(item, item.Title, "", "", null, null, true, TimeSpan.Zero, TimeSpan.Zero, 1));
            return Task.CompletedTask;
        }

        public Task PlayAsync() => Task.CompletedTask;
        public Task PauseAsync() => Task.CompletedTask;
        public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
        public Task SetVolumeAsync(double volume) => Task.CompletedTask;
        public Task NextAsync() => Task.CompletedTask;
        public Task PreviousAsync() => Task.CompletedTask;

        public Task StopAsync()
        {
            Set(PlaybackState.Empty);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        void Set(PlaybackState st)
        {
            _state = st;
            StateChanged?.Invoke(st);
        }
    }

    /// <summary>Wartet, bis die Quelle einen Fehler meldet (spielt nicht mehr, Meldung gesetzt).</summary>
    static async Task<bool> WaitForFailureAsync(RadioStreamSource source, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (!source.State.IsPlaying && source.State.Artist.Length > 0) return true;
            await Task.Delay(200);
        }
        return false;
    }
}
