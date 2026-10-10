using YTSPlayer.Contracts.Diagnostics;

namespace YTSPlayer.Platform.Windows;

/// <summary>Messung von Speicher und CPU über Win32.</summary>
public sealed class WindowsProcessStats : IProcessStats
{
    public bool IsAvailable => OperatingSystem.IsWindows();
    public long PrivateBytes(int pid) => Native.PrivateWorkingSet(pid);
    public long CpuTicks(int pid) => Native.CpuTime(pid);
    public bool Trim(int pid) => Native.TrimWorkingSet(pid);
}
