using System.Runtime.InteropServices;

namespace YTSPlayer.Platform.Windows;

/// <summary>Win32-Aufrufe für Speicher, CPU und Fenster. Nur auf Windows nutzen.</summary>
public static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    struct Pmc2
    {
        public uint Cb, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage, PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinRect { public int L, T, R, B; }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcEntry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcEntry e);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcEntry
    {
        public uint Size, Usage, ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    /// <summary>Alle Prozesse unterhalb von <paramref name="rootPid"/> (Kinder, Enkel …) mit Namen der EXE. Ohne den Wurzelprozess selbst.</summary>
    public static IReadOnlyList<(int Pid, string Exe)> Descendants(int rootPid)
    {
        var all = new List<(int Pid, int Parent, string Exe)>();
        var snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return [];
        try
        {
            var e = new ProcEntry { Size = (uint)Marshal.SizeOf<ProcEntry>() };
            for (var ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                all.Add(((int)e.ProcessId, (int)e.ParentProcessId, e.ExeFile));
        }
        finally { CloseHandle(snap); }
        var result = new List<(int, string)>();
        var seen = new HashSet<int> { rootPid };
        var queue = new Queue<int>([rootPid]);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var p in all.Where(p => p.Parent == parent && seen.Add(p.Pid)))
            {
                result.Add((p.Pid, p.Exe));
                queue.Enqueue(p.Pid);
            }
        }
        return result;
    }
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] static extern bool GetProcessMemoryInfo(IntPtr h, ref Pmc2 c, uint cb);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet")] static extern bool EmptyWorkingSet(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out WinRect r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(IntPtr h, int index, nint value);

    const uint QueryLimited = 0x1000, VmRead = 0x0010, SetQuota = 0x0100, QueryInfo = 0x0400;
    const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    const int GwlExStyle = -20;
    const nint WsExToolWindow = 0x80, WsExAppWindow = 0x40000;

    /// <summary>Gibt den Arbeitssatz eines Prozesses an Windows zurück. Gibt false zurück, wenn Windows das ablehnt.</summary>
    public static bool TrimWorkingSet(int pid)
    {
        var h = OpenProcess(QueryInfo | SetQuota, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return EmptyWorkingSet(h); }
        finally { CloseHandle(h); }
    }

    /// <summary>Privater Arbeitssatz in Byte (wie die Task-Manager-Spalte „Arbeitsspeicher“). -1 bei Fehler.</summary>
    public static long PrivateWorkingSet(int pid)
    {
        var h = OpenProcess(QueryLimited | VmRead, false, pid);
        if (h == IntPtr.Zero) return -1;
        try
        {
            var c = new Pmc2 { Cb = (uint)Marshal.SizeOf<Pmc2>() };
            return GetProcessMemoryInfo(h, ref c, c.Cb) ? (long)c.PrivateWorkingSetSize : -1;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Verbrauchte CPU-Zeit (Kernel + User) in 100-ns-Einheiten. -1 bei Fehler.</summary>
    public static long CpuTime(int pid)
    {
        var h = OpenProcess(QueryLimited, false, pid);
        if (h == IntPtr.Zero) return -1;
        try { return GetProcessTimes(h, out _, out _, out var k, out var u) ? k + u : -1; }
        finally { CloseHandle(h); }
    }

    /// <summary>Setzt Position und Größe in Pixeln direkt über Windows. Avalonia würde Positionen außerhalb des Bildschirms auf den Rand schieben.</summary>
    public static void PlaceWindow(IntPtr h, int x, int y, int w, int hgt) => SetWindowPos(h, IntPtr.Zero, x, y, w, hgt, SwpNoZOrder | SwpNoActivate);

    /// <summary>Blendet das Fenster aus Taskleiste und Alt+Tab aus (an) oder wieder ein (aus).</summary>
    public static void SetToolWindow(IntPtr h, bool on)
    {
        var s = GetWindowLongPtr(h, GwlExStyle);
        SetWindowLongPtr(h, GwlExStyle, on ? (s | WsExToolWindow) & ~WsExAppWindow : (s & ~WsExToolWindow) | WsExAppWindow);
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);

    const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000, WsClipSiblings = 0x04000000;

    /// <summary>Erzeugt ein leeres Kind-Fenster. Darin kann eine fremde Ansicht (WebView2) eingebettet werden.</summary>
    public static IntPtr CreateChildWindow(IntPtr parent) =>
        CreateWindowEx(0, "Static", "", WsChild | WsVisible | WsClipChildren | WsClipSiblings, 0, 0, 1, 1, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    public static void DestroyChildWindow(IntPtr h) => DestroyWindow(h);

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BitmapInfoHeader bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    /// <summary>Lage und Größe eines Fensters in Bildschirm-Pixeln.</summary>
    public static (int X, int Y, int W, int H) WindowBounds(IntPtr h) =>
        GetWindowRect(h, out var r) ? (r.L, r.T, r.R - r.L, r.B - r.T) : (0, 0, 0, 0);

    /// <summary>
    /// Kopiert einen Bildschirm-Ausschnitt als BGRA-Bytes (von oben nach unten). Für Selbsttests, um eingebettete
    /// Fenster (WebView2) sichtbar zu prüfen.
    /// </summary>
    public static byte[]? CaptureScreen(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return null;
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020 | 0x40000000)) return null;
            var bi = new BitmapInfoHeader { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
            var bytes = new byte[w * h * 4];
            return GetDIBits(mem, bmp, 0, (uint)h, bytes, ref bi, 0) == 0 ? null : bytes;
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>Lage und Sichtbarkeit eines Fensters als Text (für Diagnose und Selbsttest).</summary>
    public static string DescribeWindow(IntPtr h) =>
        h != IntPtr.Zero && GetWindowRect(h, out var r) ? $"sichtbar={IsWindowVisible(h)} Rechteck=({r.L},{r.T})-({r.R},{r.B})" : "kein Fenster";
}
