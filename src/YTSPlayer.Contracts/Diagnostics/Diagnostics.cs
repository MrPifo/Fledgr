namespace YTSPlayer.Contracts.Diagnostics;

/// <summary>Ein Prozess, der zu einem Modul gehört (z. B. WebView2-Prozess).</summary>
public sealed record ProcessRef(int Pid, string Name);

/// <summary>Eine Zeile mit Zählern für das Dashboard (z. B. „Anfragen 120 · blockiert 40“).</summary>
public sealed record DiagnosticLine(string Label, string Value);

/// <summary>Ein Modul meldet hier seine Prozesse und Zähler an das Diagnose-Dashboard.</summary>
public interface IDiagnosticsContributor
{
    string Group { get; }
    IReadOnlyList<ProcessRef> GetProcesses();
    IReadOnlyList<DiagnosticLine> GetLines();
}

/// <summary>Speicher und CPU eines Prozesses. Jede Plattform hat eine eigene Umsetzung.</summary>
public interface IProcessStats
{
    bool IsAvailable { get; }

    /// <summary>Privater Arbeitssatz in Byte. -1 bei Fehler.</summary>
    long PrivateBytes(int pid);

    /// <summary>CPU-Zeit in 100-ns-Einheiten. -1 bei Fehler.</summary>
    long CpuTicks(int pid);

    /// <summary>Gibt den Arbeitssatz an das System zurück. Gibt false zurück, wenn das System das ablehnt.</summary>
    bool Trim(int pid);
}

/// <summary>Eine Zeile der Prozess-Tabelle.</summary>
public sealed record ProcessRow(string Group, string Name, int Pid, double Mb, double Cpu);

/// <summary>Ergebnis einer Messung der ganzen App.</summary>
public sealed record ProcessSample(IReadOnlyList<ProcessRow> Rows, double HostMb, IReadOnlyDictionary<string, double> GroupMb)
{
    public double TotalMb => HostMb + GroupMb.Values.Sum();
}

/// <summary>Auswertung einer benannten Operation im Dashboard.</summary>
public sealed record PerfEntry(string Name, int Count, int Failures, TimeSpan Last, TimeSpan Average, TimeSpan Max, int Running);

/// <summary>Misst Dauer und Fehler von benannten Operationen (Scan, Suche, Login …).</summary>
public interface IPerfTracker
{
    /// <summary>Startet eine Messung. Mit <c>using</c> verwenden. <see cref="PerfScope.Fail"/> markiert einen Fehler.</summary>
    PerfScope Begin(string operation);

    IReadOnlyList<PerfEntry> Snapshot();
}
