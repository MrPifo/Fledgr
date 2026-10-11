using System.Globalization;
using LibVLCSharp.Shared;

namespace Fledgr.Audio;

/// <summary>
/// LibVLC ohne Video, genutzt von lokaler Musik und Radio. Motor und Player entstehen beim ersten Abspielen
/// und werden bei <see cref="ReleaseAsync"/> wieder freigegeben, damit im Leerlauf kein Speicher belegt bleibt.
/// Die Ereignisse kommen aus LibVLC-Threads. Der Aufrufer macht daraus eigene Tasks.
/// </summary>
public sealed class LibVlcEngine(Func<string?>? libVlcDirectory = null) : IAsyncDisposable
{
    static readonly object CoreLock = new();
    static bool _coreReady;
    static readonly TimeSpan MetaPollInterval = TimeSpan.FromSeconds(2);

    readonly object _lock = new();
    LibVLC? _vlc;
    MediaPlayer? _mp;
    Media? _media;
    // Eigener Leser für Radio-Streams (ICY-Titel). Gehört zum aktuellen Medium.
    IcyStream? _icy;
    StreamMediaInput? _input;
    int _request;
    double _gain = 1;
    System.Threading.Timer? _metaTimer;
    string? _lastNowPlaying;

    public event Action? Playing;
    public event Action? Paused;
    public event Action? EndReached;
    public event Action? Error;
    public event Action<TimeSpan>? TimeChanged;
    public event Action<TimeSpan>? LengthChanged;

    /// <summary>Laufender Titel aus den Stream-Metadaten (ICY, z. B. „Künstler - Titel“). Kommt nur bei Änderungen.</summary>
    public event Action<string>? NowPlayingChanged;

    /// <summary>Die Lautstärke wurde außerhalb der App geändert (z. B. im Windows-Lautstärkemixer). Wert in Prozent. Kommt aus einem LibVLC-Thread.</summary>
    public event Action<double>? VolumeChangedExternally;

    long _ignoreVolumeUntil;

    /// <summary>
    /// LibVLC setzt unter Windows die Lautstärke des App-Eintrags im Mixer auf Wert³. Damit Regler und Windows-Mixer
    /// dieselbe Zahl zeigen, rechnet der Motor mit der Kubikwurzel hin und mit der dritten Potenz zurück.
    /// </summary>
    static int ToVlc(double gain) => (int)Math.Round(100 * Math.Cbrt(Math.Clamp(gain, 0, 1)));

    static double FromVlc(int vlcPercent) => Math.Pow(Math.Clamp(vlcPercent, 0, 100) / 100.0, 3);

    public bool IsLoaded => _mp != null;
    public bool IsPlaying => _mp?.IsPlaying == true;
    public bool IsSeekable => _mp?.IsSeekable == true;

    /// <summary>Wiedergabeposition in Millisekunden. Ohne Player ist der Wert 0 und Schreiben tut nichts.</summary>
    public long TimeMs
    {
        get => _mp?.Time ?? 0;
        set { if (_mp != null) _mp.Time = value; }
    }

    /// <summary>Lautstärke in Prozent (0–100). Wird gemerkt, auch wenn noch kein Player läuft.</summary>
    public void SetVolume(int percent) => SetGain(percent / 100.0);

    /// <summary>Lautstärke als Faktor (0–1), gemeint wie der Mixer-Eintrag der App in Windows. Wird gemerkt, auch wenn noch kein Player läuft.</summary>
    public void SetGain(double gain)
    {
        _gain = Math.Clamp(gain, 0, 1);
        _ignoreVolumeUntil = Environment.TickCount64 + 1500;
        if (_mp != null) _mp.Volume = ToVlc(_gain);
    }

    public void SetPaused(bool paused)
    {
        if (_mp != null) _mp.SetPause(paused);
    }

    /// <summary>
    /// Startet eine Datei (<paramref name="isUrl"/> false) oder einen Stream (true). Beim ersten Mal lädt LibVLC und braucht kurz:
    /// nicht auf dem UI-Thread aufrufen.
    /// </summary>
    public void Play(string location, bool isUrl, TimeSpan startAt = default)
    {
        Interlocked.Increment(ref _request);
        EnsurePlayer();
        var media = new Media(_vlc!, location, isUrl ? FromType.FromLocation : FromType.FromPath);
        if (isUrl) AddStreamBuffer(media);
        if (startAt > TimeSpan.Zero) media.AddOption(":start-time=" + startAt.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
        Start(media, null, null);
    }

    /// <summary>
    /// Startet einen Internet-Radio-Stream. Die App liest ihn selbst (<see cref="IcyStream"/>), damit der Titel ankommt.
    /// Geht das nicht (HLS, Playlist, Fehler), öffnet LibVLC die Adresse wie bisher. Nicht auf dem UI-Thread aufrufen.
    /// </summary>
    public async Task PlayStreamAsync(string url, CancellationToken ct = default)
    {
        var request = Interlocked.Increment(ref _request);
        var icy = await IcyStream.OpenAsync(url, ct);
        // Inzwischen anderer Titel, Pause oder Stopp: diesen Stream verwerfen.
        if (request != Volatile.Read(ref _request))
        {
            icy?.Dispose();
            return;
        }
        if (icy == null)
        {
            Play(url, isUrl: true);
            return;
        }
        // Ein alter Leser kann beim Wechsel noch einen Titel melden. Nur der aktuelle zählt.
        icy.TitleChanged += title =>
        {
            bool current;
            lock (_lock) current = ReferenceEquals(_icy, icy);
            if (current) ReportNowPlaying(title);
        };
        EnsurePlayer();
        var input = new StreamMediaInput(icy);
        var media = new Media(_vlc!, input);
        AddStreamBuffer(media);
        Start(media, icy, input);
    }

    /// <summary>Puffer für Internet-Streams. Der Standard (0,3 bis 1 s) reicht bei schwankendem Netz oder trägen Servern nicht und lässt den Ton stocken.</summary>
    const int StreamBufferMs = 3000;

    static void AddStreamBuffer(Media media)
    {
        var ms = StreamBufferMs.ToString(CultureInfo.InvariantCulture);
        media.AddOption(":file-caching=" + ms);
        media.AddOption(":network-caching=" + ms);
        media.AddOption(":live-caching=" + ms);
    }

    void Start(Media media, IcyStream? icy, StreamMediaInput? input)
    {
        media.MetaChanged += OnMetaChanged;
        Media? oldMedia;
        IcyStream? oldIcy;
        StreamMediaInput? oldInput;
        lock (_lock)
        {
            oldMedia = _media; oldIcy = _icy; oldInput = _input;
            _media = media; _icy = icy; _input = input;
            _lastNowPlaying = null;
            _metaTimer ??= new System.Threading.Timer(_ => PollNowPlaying(), null, MetaPollInterval, MetaPollInterval);
        }
        _ignoreVolumeUntil = Environment.TickCount64 + 1500;
        _mp!.Volume = ToVlc(_gain);
        StopGuarded(() => _mp.Play(media), oldIcy);
        oldMedia?.Dispose();
        oldInput?.Dispose();
        oldIcy?.Dispose();
    }

    /// <summary>
    /// LibVLC wartet beim Stoppen, bis der Leser fertig gelesen hat. Hängt die Verbindung, bricht der Leser nach 1 s ab.
    /// Die Ereignisse sind dann schon abgemeldet, es kommt kein falsches „Ende“.
    /// </summary>
    static void StopGuarded(Action stop, IcyStream? icy)
    {
        if (icy == null) { stop(); return; }
        using var guard = new System.Threading.Timer(_ => icy.Abort(), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        stop();
    }

    /// <summary>Stoppt die Wiedergabe und gibt Motor, Player und Medium frei.</summary>
    public Task ReleaseAsync()
    {
        Interlocked.Increment(ref _request);
        MediaPlayer? mp;
        LibVLC? vlc;
        Media? media;
        IcyStream? icy;
        StreamMediaInput? input;
        lock (_lock)
        {
            mp = _mp; vlc = _vlc; media = _media; icy = _icy; input = _input;
            _mp = null; _vlc = null; _media = null; _icy = null; _input = null;
            _metaTimer?.Dispose();
            _metaTimer = null;
            _lastNowPlaying = null;
        }
        if (mp == null && vlc == null) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try { StopGuarded(() => mp?.Stop(), icy); } catch { }
            media?.Dispose();
            input?.Dispose();
            icy?.Dispose();
            mp?.Dispose();
            vlc?.Dispose();
        });
    }

    public ValueTask DisposeAsync() => new(ReleaseAsync());

    void EnsurePlayer()
    {
        lock (_lock)
        {
            if (_mp != null) return;
            lock (CoreLock)
            {
                if (!_coreReady)
                {
                    Core.Initialize(libVlcDirectory?.Invoke());
                    _coreReady = true;
                }
            }
            _vlc = new LibVLC("--no-video", "--no-osd", "--no-snapshot-preview", "--no-stats");
            _mp = new MediaPlayer(_vlc);
            _mp.Playing += (_, _) => Playing?.Invoke();
            _mp.Paused += (_, _) => Paused?.Invoke();
            _mp.TimeChanged += (_, e) => TimeChanged?.Invoke(TimeSpan.FromMilliseconds(e.Time));
            _mp.LengthChanged += (_, e) => LengthChanged?.Invoke(TimeSpan.FromMilliseconds(e.Length));
            _mp.EndReached += (_, _) => EndReached?.Invoke();
            _mp.EncounteredError += (_, _) => Error?.Invoke();
            // Beim Start und nach eigenen Änderungen meldet LibVLC den Wert selbst zurück: das zählt nicht als Änderung von außen.
            _mp.VolumeChanged += (_, ev) =>
            {
                if (Environment.TickCount64 < _ignoreVolumeUntil) return;
                var vlc = (int)Math.Round(ev.Volume * 100);
                if (Math.Abs(vlc - ToVlc(_gain)) <= 1) return;
                _gain = FromVlc(vlc);
                VolumeChangedExternally?.Invoke(_gain);
            };
        }
    }

    void OnMetaChanged(object? sender, MediaMetaChangedEventArgs e)
    {
        if (e.MetadataType != MetadataType.NowPlaying) return;
        // Im LibVLC-Ereignis nicht abfragen (Deadlock-Gefahr), sondern in einem eigenen Task.
        _ = Task.Run(PollNowPlaying);
    }

    /// <summary>
    /// Liest den Stream-Titel und meldet ihn, wenn er sich geändert hat. Läuft zusätzlich alle 2 s mit,
    /// weil LibVLC die Änderung nicht immer als Ereignis meldet.
    /// </summary>
    void PollNowPlaying()
    {
        Media? media;
        lock (_lock) media = _media;
        if (media == null) return;
        try
        {
            var text = media.Meta(MetadataType.NowPlaying)?.Trim();
            if (!string.IsNullOrEmpty(text)) ReportNowPlaying(text);
        }
        catch (Exception)
        {
            // Medium gerade freigegeben: kein Titel mehr zu lesen.
        }
    }

    /// <summary>Meldet einen Titel nur, wenn er neu ist. Er kommt aus LibVLC (Abfrage) oder aus dem eigenen Leser.</summary>
    void ReportNowPlaying(string text)
    {
        lock (_lock)
        {
            if (text == _lastNowPlaying) return;
            _lastNowPlaying = text;
        }
        NowPlayingChanged?.Invoke(text);
    }
}
