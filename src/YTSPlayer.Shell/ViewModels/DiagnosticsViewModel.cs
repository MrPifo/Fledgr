using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Shell.Services;

namespace YTSPlayer.Shell.ViewModels;

/// <summary>Eine Zeile der Operationen-Tabelle.</summary>
public sealed record OperationRow(string Name, string Count, string Failures, string Last, string Average, string Max, string Running);

/// <summary>Werkzeug-Dashboard: RAM gegen Ziel, Verlauf, Prozesse, Zähler der Module, Dauer der Operationen.</summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    sealed record Point(DateTime Time, double HostMb, Dictionary<string, double> Groups);

    const int MaxHistory = 90;
    const int MaxCsv = 3700;

    readonly AppSettings _settings;
    readonly ProcessMonitor _monitor;
    readonly PluginHost _host;
    readonly IPerfTracker _perf;
    readonly IAppPaths _paths;
    readonly DispatcherTimer _timer;
    readonly List<double> _values = [];
    readonly List<Point> _points = [];
    bool _busy;

    [ObservableProperty] string _totalText = "–";
    [ObservableProperty] double _percent;
    [ObservableProperty] string _targetText = "";
    [ObservableProperty] double _targetMb;
    [ObservableProperty] IReadOnlyList<double> _history = [];
    [ObservableProperty] string _info = "";

    public DiagnosticsViewModel(AppSettings settings, ProcessMonitor monitor, PluginHost host, IPerfTracker perf, IAppPaths paths)
    {
        _settings = settings;
        _monitor = monitor;
        _host = host;
        _perf = perf;
        _paths = paths;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await UpdateAsync());
    }

    public ObservableCollection<ProcessRow> Rows { get; } = [];
    public ObservableCollection<DiagnosticLine> Lines { get; } = [];
    public ObservableCollection<OperationRow> Operations { get; } = [];

    public void Start()
    {
        _ = UpdateAsync();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    [RelayCommand]
    async Task TrimNow()
    {
        var n = await Task.Run(_monitor.TrimAll);
        Info = $"Arbeitssatz geleert bei {n} Prozessen.";
    }

    [RelayCommand]
    void ExportCsv()
    {
        try
        {
            var groups = _points.SelectMany(p => p.Groups.Keys).Distinct().OrderBy(x => x).ToList();
            var ci = CultureInfo.InvariantCulture;
            var lines = new List<string> { string.Join(',', ["time", "total_mb", "host_mb", .. groups.Select(g => g.ToLowerInvariant() + "_mb")]) };
            foreach (var p in _points)
            {
                var total = p.HostMb + p.Groups.Values.Sum();
                lines.Add(string.Join(',', [p.Time.ToString("s", ci), total.ToString("F1", ci), p.HostMb.ToString("F1", ci), .. groups.Select(g => p.Groups.GetValueOrDefault(g).ToString("F1", ci))]));
            }
            Directory.CreateDirectory(_paths.Export);
            var file = Path.Combine(_paths.Export, $"diagnose-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllLines(file, lines, Encoding.UTF8);
            Info = "Gespeichert: " + file;
        }
        catch (Exception ex)
        {
            Info = "Fehler: " + ex.Message;
        }
    }

    async Task UpdateAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var sample = await Task.Run(_monitor.Sample);
            Rows.Clear();
            foreach (var r in sample.Rows.OrderByDescending(r => r.Mb)) Rows.Add(r);
            var total = sample.TotalMb;
            var parts = string.Join(" · ", sample.GroupMb.Select(kv => $"{kv.Key} {kv.Value:F0} MB"));
            TotalText = $"{total:F0} MB gesamt  (Host {sample.HostMb:F0} MB{(parts.Length > 0 ? " · " + parts : "")})";
            TargetMb = _settings.TargetMb;
            TargetText = $"Ziel: {_settings.TargetMb} MB";
            Percent = _settings.TargetMb > 0 ? Math.Min(100, total / _settings.TargetMb * 100) : 0;
            _values.Add(total);
            if (_values.Count > MaxHistory) _values.RemoveAt(0);
            History = _values.ToArray();
            if (_points.Count < MaxCsv) _points.Add(new Point(DateTime.Now, sample.HostMb, new Dictionary<string, double>(sample.GroupMb)));

            Lines.Clear();
            foreach (var c in _host.Diagnostics.ToList())
                foreach (var l in c.GetLines()) Lines.Add(new DiagnosticLine($"{c.Group}: {l.Label}", l.Value));

            Operations.Clear();
            foreach (var e in _perf.Snapshot())
                Operations.Add(new OperationRow(e.Name, e.Count.ToString(), e.Failures.ToString(), Ms(e.Last), Ms(e.Average), Ms(e.Max), e.Running > 0 ? e.Running.ToString() : ""));
        }
        catch (Exception ex)
        {
            Info = "Messfehler: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    static string Ms(TimeSpan t) => t.TotalMilliseconds < 1000 ? $"{t.TotalMilliseconds:F0} ms" : $"{t.TotalSeconds:F1} s";
}
