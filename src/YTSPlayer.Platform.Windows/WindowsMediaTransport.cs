using Windows.Media;
using Windows.Storage.Streams;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;

namespace YTSPlayer.Platform.Windows;

/// <summary>
/// Medientasten und Windows-Medienanzeige (SystemMediaTransportControls). Windows zeigt dann Titel, Interpret, Cover und
/// die Zeitleiste an und schickt Play/Pause/Weiter/Zurück/Spulen an uns, auch von der Tastatur und aus Kopfhörern.
/// </summary>
public sealed class WindowsMediaTransport : IMediaTransport
{
    SystemMediaTransportControls? _smtc;
    IPlayer? _player;
    SynchronizationContext? _ui;
    string _shownKey = "";
    long _lastTimelineTick;

    public void Attach(IPlayer player, IntPtr windowHandle)
    {
        if (_smtc != null || windowHandle == IntPtr.Zero) return;
        try
        {
            _ui = SynchronizationContext.Current;
            _player = player;
            var smtc = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = smtc.IsPauseEnabled = smtc.IsStopEnabled = smtc.IsNextEnabled = smtc.IsPreviousEnabled = true;
            smtc.ButtonPressed += OnButton;
            smtc.PlaybackPositionChangeRequested += OnSeekRequested;
            _smtc = smtc;
            player.StateChanged += OnState;
            OnState(player.State);
        }
        catch
        {
            // Ohne Medienanzeige läuft die App normal weiter (z. B. sehr alte Windows-Versionen).
            _smtc = null;
        }
    }

    void OnButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs e)
    {
        var button = e.Button;
        // Das Ereignis kommt auf einem Hintergrund-Thread. Der Player gehört dem UI-Thread.
        _ui?.Post(_ =>
        {
            var p = _player;
            if (p == null) return;
            switch (button)
            {
                case SystemMediaTransportControlsButton.Play: if (!p.State.IsPlaying) _ = p.TogglePlayAsync(); break;
                case SystemMediaTransportControlsButton.Pause: if (p.State.IsPlaying) _ = p.TogglePlayAsync(); break;
                case SystemMediaTransportControlsButton.Stop: _ = p.StopAsync(); break;
                case SystemMediaTransportControlsButton.Next: _ = p.NextAsync(); break;
                case SystemMediaTransportControlsButton.Previous: _ = p.PreviousAsync(); break;
            }
        }, null);
    }

    void OnSeekRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs e)
    {
        var pos = e.RequestedPlaybackPosition;
        _ui?.Post(_ => _ = _player?.SeekAsync(pos), null);
    }

    void OnState(PlaybackState st)
    {
        var smtc = _smtc;
        if (smtc == null) return;
        try
        {
            smtc.PlaybackStatus = st.Item == null ? MediaPlaybackStatus.Closed : st.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            if (st.Item == null) { _shownKey = ""; smtc.DisplayUpdater.ClearAll(); smtc.DisplayUpdater.Update(); return; }

            // Titelangaben nur bei Änderung neu setzen (sonst flackert die Anzeige bei jedem Zeit-Tick).
            var key = $"{st.Title}|{st.Artist}|{st.Album}|{st.CoverBytes?.Length}|{st.CoverUrl}";
            if (key != _shownKey)
            {
                _shownKey = key;
                var u = smtc.DisplayUpdater;
                u.Type = MediaPlaybackType.Music;
                u.MusicProperties.Title = st.Title;
                u.MusicProperties.Artist = st.Artist;
                u.MusicProperties.AlbumTitle = st.Album;
                u.Thumbnail = CoverRef(st);
                u.Update();
            }

            // Die Zeitleiste höchstens einmal pro Sekunde aktualisieren.
            var now = Environment.TickCount64;
            if (st.Duration > TimeSpan.Zero && now - _lastTimelineTick >= 1000)
            {
                _lastTimelineTick = now;
                var t = new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    EndTime = st.Duration,
                    MaxSeekTime = st.Duration,
                    Position = st.Position > st.Duration ? st.Duration : st.Position,
                };
                smtc.UpdateTimelineProperties(t);
            }
        }
        catch
        {
            // Eine fehlgeschlagene Anzeige-Aktualisierung darf die Wiedergabe nie stören.
        }
    }

    static RandomAccessStreamReference? CoverRef(PlaybackState st)
    {
        try
        {
            if (st.CoverBytes is { Length: > 0 } bytes)
            {
                var stream = new InMemoryRandomAccessStream();
                using (var w = new DataWriter(stream))
                {
                    w.WriteBytes(bytes);
                    w.StoreAsync().GetAwaiter().GetResult();
                    w.DetachStream();
                }
                stream.Seek(0);
                return RandomAccessStreamReference.CreateFromStream(stream);
            }
            if (Uri.TryCreate(st.CoverUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return RandomAccessStreamReference.CreateFromUri(uri);
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        if (_player != null) _player.StateChanged -= OnState;
        if (_smtc != null)
        {
            _smtc.ButtonPressed -= OnButton;
            _smtc.PlaybackPositionChangeRequested -= OnSeekRequested;
            try { _smtc.IsEnabled = false; } catch { }
        }
        _smtc = null;
        _player = null;
    }
}
