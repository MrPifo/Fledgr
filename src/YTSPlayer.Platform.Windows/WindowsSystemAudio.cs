using YTSPlayer.Contracts.Playback;
using static YTSPlayer.Platform.Windows.CoreAudio;

namespace YTSPlayer.Platform.Windows;

/// <summary>Liest den Stumm-Zustand des Standard-Ausgabegeräts über die Windows Core Audio Schnittstelle.</summary>
public sealed class WindowsSystemAudio : ISystemAudio
{
    public bool IsMuted()
    {
        object? enumerator = null, device = null, volume = null;
        try
        {
            enumerator = new MMDeviceEnumeratorCom();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out var dev) != 0) return false;
            device = dev;
            var id = EndpointVolumeId;
            if (dev.Activate(ref id, 23, IntPtr.Zero, out volume) != 0) return false;
            return ((IAudioEndpointVolume)volume).GetMute(out var muted) == 0 && muted;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(volume);
            Release(device);
            Release(enumerator);
        }
    }
}
