using System.Diagnostics;
using System.Runtime.InteropServices;
using YTSPlayer.Contracts.Playback;
using static YTSPlayer.Platform.Windows.CoreAudio;

namespace YTSPlayer.Platform.Windows;

/// <summary>
/// Findet im Lautstärkemixer die Einträge der Kindprozesse dieser App (WebView2) und liest oder setzt ihre Lautstärke.
/// Ein Eintrag entsteht erst, wenn die Seite zum ersten Mal Ton ausgibt.
/// </summary>
public sealed class WindowsChildAudio : IChildAudioVolume
{
    public double? Get()
    {
        double? result = null;
        ForEachSession((_, vol) =>
        {
            if (vol.GetMasterVolume(out var level) == 0) result ??= level;
        });
        return result;
    }

    public bool Set(double volume)
    {
        var found = false;
        var level = (float)Math.Clamp(volume, 0, 1);
        var none = Guid.Empty;
        ForEachSession((_, vol) =>
        {
            found = true;
            vol.SetMasterVolume(level, ref none);
        });
        return found;
    }

    /// <summary>Ruft <paramref name="action"/> für jeden Mixer-Eintrag eines Kindprozesses auf. Alle COM-Fehler werden verschluckt.</summary>
    static void ForEachSession(Action<IAudioSessionControl2, ISimpleAudioVolume> action)
    {
        object? enumerator = null, device = null, manager = null;
        try
        {
            var children = ChildProcessIds();
            if (children.Count == 0) return;
            enumerator = new MMDeviceEnumeratorCom();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out var dev) != 0) return;
            device = dev;
            var id = SessionManagerId;
            if (dev.Activate(ref id, 23, IntPtr.Zero, out manager) != 0) return;
            if (((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions) != 0) return;
            try
            {
                if (sessions.GetCount(out var count) != 0) return;
                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var session) != 0) continue;
                    try
                    {
                        if (session is IAudioSessionControl2 control && session is ISimpleAudioVolume vol
                            && control.GetProcessId(out var pid) == 0 && children.Contains((int)pid))
                            action(control, vol);
                    }
                    finally
                    {
                        Release(session);
                    }
                }
            }
            finally
            {
                Release(sessions);
            }
        }
        catch
        {
            // Core Audio nicht erreichbar: dann gibt es nichts zu steuern.
        }
        finally
        {
            Release(manager);
            Release(device);
            Release(enumerator);
        }
    }

    /// <summary>Prozess-Kennungen aller Nachkommen dieser App (Kinder, Enkel …).</summary>
    static HashSet<int> ChildProcessIds()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(0x2, 0);
        if (snapshot == new IntPtr(-1)) return [];
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var ok = Process32First(snapshot, ref entry); ok; ok = Process32Next(snapshot, ref entry))
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        }
        finally
        {
            CloseHandle(snapshot);
        }
        var me = Environment.ProcessId;
        var result = new HashSet<int>();
        foreach (var pid in parents.Keys)
        {
            var current = pid;
            for (var depth = 0; depth < 12 && parents.TryGetValue(current, out var parent); depth++)
            {
                if (parent == me) { result.Add(pid); break; }
                current = parent;
            }
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
