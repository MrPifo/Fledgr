using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Localization;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Shell.Services;
using YTSPlayer.Ui;

namespace YTSPlayer.Shell.ViewModels;

/// <summary>Ein Knopf in der Kopfzeile eines Home-Abschnitts.</summary>
public sealed class HomeActionVm(HomeAction action)
{
    public string Text => Loc.T(action.Text);
    public string Icon => action.Icon;
    public IRelayCommand Command { get; } = new RelayCommand(action.Invoke);
}

/// <summary>Ein Abschnitt auf Home. Ohne Inhalt (siehe <see cref="IHomeContent"/>) ist er ausgeblendet.</summary>
public sealed class HomeSectionVm : ObservableObject, IDisposable
{
    readonly HomeSection _section;
    readonly INotifyPropertyChanged? _notifier;

    public HomeSectionVm(HomeSection section, Action<string> open)
    {
        _section = section;
        Actions = [.. (section.Actions ?? []).Select(a => new HomeActionVm(a))];
        OpenMoreCommand = new RelayCommand(() => { if (section.PageId != null) open(section.PageId); });
        _notifier = section.ViewModel as INotifyPropertyChanged;
        if (_notifier != null) _notifier.PropertyChanged += OnChanged;
    }

    public string Title => Loc.T(_section.Title);
    public string Tone => _section.Tone;
    public object ViewModel => _section.ViewModel;
    public bool HasMore => _section.PageId != null;
    public bool HasActions => Actions.Count > 0;
    public IReadOnlyList<HomeActionVm> Actions { get; }
    public IRelayCommand OpenMoreCommand { get; }

    /// <summary>Wahr, wenn der Abschnitt etwas zeigt.</summary>
    public bool IsVisible => _section.ViewModel is not IHomeContent c || c.HasContent;

    /// <summary>Texte neu abfragen (Sprachwechsel).</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IHomeContent.HasContent) or null or "") Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsVisible)));
    }

    public void Dispose()
    {
        if (_notifier != null) _notifier.PropertyChanged -= OnChanged;
    }
}

/// <summary>Eine Zeile im Verlauf (und die „Weitermachen“-Karte).</summary>
public sealed class HistoryRowVm(HistoryEntry entry, CoverLoader covers, Action<HistoryEntry> play) : PlayingRow
{
    const int CoverWidth = 96;
    Bitmap? _cover;
    bool _requested;

    public HistoryEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public string Artist => Entry.Artist;
    public bool HasArtist => Entry.Artist.Length > 0;

    /// <summary>Modulfarbe der Quelle: music, yt oder radio.</summary>
    public string Tone => Entry.SourceId switch { "yt" => "yt", "radio" => "radio", _ => "music" };

    /// <summary>Symbol, solange das Cover fehlt.</summary>
    public string SourceIcon => Entry.SourceId switch { "yt" => Icons.Youtube, "radio" => Icons.Radio, _ => Icons.Music };

    public string SourceText => Entry.SourceId switch { "yt" => Loc.T("home.source.yt"), "radio" => Loc.T("home.source.radio"), _ => Loc.T("home.source.local") };

    /// <summary>Wann der Titel lief, als kurzer Text („vor 5 Min“).</summary>
    public string When
    {
        get
        {
            var span = DateTime.UtcNow - Entry.PlayedUtc;
            if (span.TotalMinutes < 1) return Loc.T("home.ago.now");
            if (span.TotalHours < 1) return Loc.P("home.ago.min", (long)span.TotalMinutes);
            if (span.TotalDays < 1) return Loc.P("home.ago.hour", (long)span.TotalHours);
            return Loc.P("home.ago.day", (long)span.TotalDays);
        }
    }

    public IRelayCommand PlayCommand { get; } = new RelayCommand(() => play(entry));

    static string Format(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{(int)d.TotalMinutes}:{d.Seconds:00}";

    TimeSpan? _position;

    /// <summary>Länge des Titels als Text („4:12“) oder leer, wenn sie unbekannt ist (z. B. Radio).</summary>
    public string DurationText => Entry.DurationSeconds >= 1 ? Format(TimeSpan.FromSeconds(Entry.DurationSeconds)) : "";

    /// <summary>
    /// Zeit-Text der Karte: läuft der Titel gerade (oder ist er geladen), steht dort „1:48 / 4:12“ (die Position läuft mit),
    /// sonst nur die Länge. Leer, wenn nichts bekannt ist.
    /// </summary>
    public string TimeText => IsCurrent && _position is { } p && DurationText.Length > 0 ? Format(p) + " / " + DurationText : DurationText;

    public bool HasDuration => TimeText.Length > 0;

    /// <summary>Zieht die Position nach (der Player meldet sie etwa einmal pro Sekunde). Meldet nur bei geändertem Text.</summary>
    public void UpdatePosition(PlaybackState st)
    {
        if (!IsCurrent) return;
        var before = TimeText;
        _position = st.Position;
        if (TimeText != before) OnPropertyChanged(nameof(TimeText));
    }

    /// <summary>Symbol des Play-Knopfs: Pause, solange dieser Titel spielt.</summary>
    public string PlayIcon => IsPlayingNow ? Icons.Pause : Icons.Play;

    /// <summary>Übernimmt den Zustand des Players (ist es dieser Titel, spielt er gerade).</summary>
    public void Apply(PlaybackState st)
    {
        var current = st.Item is { } i && i.SourceId == Entry.SourceId && i.Id == Entry.ItemId;
        SetPlaying(current, st.IsPlaying);
        OnPropertyChanged(nameof(PlayIcon));
        // Die Länge ist oft erst nach dem Start bekannt: die Karte zeigt sie sofort.
        if (current && st.Duration.TotalSeconds > 0 && Math.Abs(Entry.DurationSeconds - st.Duration.TotalSeconds) > 1)
        {
            Entry.DurationSeconds = st.Duration.TotalSeconds;
            OnPropertyChanged(nameof(DurationText));
        }
        if (!current) _position = null;
        UpdatePosition(st);
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(HasDuration));
    }

    public bool HasCover => _cover != null;

    /// <summary>Cover. Die erste Abfrage startet das Laden im Hintergrund.</summary>
    public Bitmap? Cover
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = LoadAsync();
            }
            return _cover;
        }
    }

    async Task LoadAsync()
    {
        try
        {
            var bmp = await covers.LoadAsync(Entry.CoverUrl, null, CoverWidth);
            if (bmp == null) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _cover = bmp;
                OnPropertyChanged(nameof(Cover));
                OnPropertyChanged(nameof(HasCover));
            });
        }
        catch (Exception)
        {
            // Ohne Cover bleibt das Quellen-Symbol.
        }
    }
}

/// <summary>
/// Die Startseite der App: Datum und Uhrzeit, „Weitermachen“, die Abschnitte der aktiven Module und der globale Verlauf.
/// Die Shell kennt die Module nicht; sie zeigt nur, was die Module als <see cref="HomeSection"/> anmelden.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IDisposable
{
    const int ShownHistory = 8;

    readonly PlayerService _player;
    readonly PluginHost _host;
    readonly IPlayHistory _history;
    readonly CoverLoader _covers;
    readonly Action<string> _navigate;
    readonly DispatcherTimer _clock;

    [ObservableProperty] string _dateText = "";
    [ObservableProperty] string _timeText = "";
    [ObservableProperty] string _greeting = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasContinue))] HistoryRowVm? _continue;

    public HomeViewModel(PlayerService player, PluginHost host, IPlayHistory history, CoverLoader covers, Action<string> navigate)
    {
        _player = player;
        _host = host;
        _history = history;
        _covers = covers;
        _navigate = navigate;
        Sections = [];
        History = [];
        RebuildSections();
        RebuildHistory();
        UpdateClock();
        host.HomeSections.CollectionChanged += OnSectionsChanged;
        history.Changed += RebuildHistory;
        player.StateChanged += OnPlayerState;
        Loc.Current.LanguageChanged += () => Dispatcher.UIThread.Post(RefreshTexts);
        // Die Uhr tickt alle 20 Sekunden und zeichnet nur bei geänderter Minute neu.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
    }

    public ObservableCollection<HomeSectionVm> Sections { get; }
    public ObservableCollection<HistoryRowVm> History { get; }

    public bool HasContinue => Continue != null;
    public bool HasHistory => History.Count > 0;

    /// <summary>Wahr, wenn kein Abschnitt vorhanden ist (alle Module aus).</summary>
    public bool NoSections => Sections.Count == 0;

    /// <summary>Version der App (z. B. „Fledgr 1.0.0“).</summary>
    public string VersionText => "Fledgr " + AppVersion;

    /// <summary>Die Versionsnummer ohne Zusatz (Commit-Hash).</summary>
    public static string AppVersion
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(HomeViewModel).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var v = info ?? asm.GetName().Version?.ToString(3) ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    void UpdateClock()
    {
        var now = DateTime.Now;
        var date = Loc.Current.LongDate(now).ToUpperInvariant();
        var time = Loc.Current.Format("home.time", Loc.Current.Time(now));
        var hello = now.Hour switch
        {
            >= 5 and < 11 => Loc.T("home.greeting.morning"),
            >= 11 and < 17 => Loc.T("home.greeting.day"),
            >= 17 and < 23 => Loc.T("home.greeting.evening"),
            _ => Loc.T("home.greeting.night"),
        };
        if (date != DateText) DateText = date;
        if (time != TimeText) TimeText = time;
        if (hello != Greeting) Greeting = hello;
    }

    void RefreshTexts()
    {
        UpdateClock();
        foreach (var s in Sections) s.RefreshTexts();
        RebuildHistory();
        OnPropertyChanged(nameof(VersionText));
    }

    void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildSections();

    void RebuildSections()
    {
        foreach (var s in Sections) s.Dispose();
        Sections.Clear();
        foreach (var s in _host.HomeSections) Sections.Add(new HomeSectionVm(s, _navigate));
        OnPropertyChanged(nameof(NoSections));
    }

    void RebuildHistory()
    {
        History.Clear();
        foreach (var e in _history.Entries.Take(ShownHistory)) History.Add(new HistoryRowVm(e, _covers, PlayEntry));
        Continue = _history.Entries.Count > 0 ? new HistoryRowVm(_history.Entries[0], _covers, PlayEntry) : null;
        OnPropertyChanged(nameof(HasHistory));
        ApplyPlayerState(_player.State);
    }

    string _lastState = "";

    /// <summary>Nur bei neuem Titel oder Play/Pause-Wechsel nachziehen (der Player meldet auch jede Positionsänderung).</summary>
    void OnPlayerState(PlaybackState st)
    {
        var key = (st.Item?.SourceId ?? "") + "|" + (st.Item?.Id ?? "") + "|" + st.IsPlaying;
        if (key == _lastState)
        {
            Continue?.UpdatePosition(st);
            return;
        }
        _lastState = key;
        ApplyPlayerState(st);
    }

    void ApplyPlayerState(PlaybackState st)
    {
        Continue?.Apply(st);
        foreach (var row in History) row.Apply(st);
    }

    /// <summary>Play/Pause der Karte: läuft dieser Titel gerade (oder ist er geladen), schaltet es um, sonst startet er.</summary>
    [RelayCommand]
    Task ToggleContinue()
    {
        if (Continue is not { } row) return Task.CompletedTask;
        if (row.IsCurrent) return RunAsync(_player.TogglePlayAsync);
        PlayEntry(row.Entry);
        return Task.CompletedTask;
    }

    void PlayEntry(HistoryEntry e) =>
        _ = RunAsync(() => _player.PlayAsync(new MediaItem(e.SourceId, e.ItemId, e.Title, e.Artist, e.Album, e.CoverUrl)));

    /// <summary>„Weitermachen“: ist ein Titel geladen (auch ein wiederhergestellter, pausierter), startet oder pausiert er. Sonst spielt der letzte Verlaufs-Eintrag.</summary>
    [RelayCommand]
    Task Resume()
    {
        if (_player.State.Item != null) return RunAsync(_player.TogglePlayAsync);
        if (_history.Entries.Count > 0) PlayEntry(_history.Entries[0]);
        return Task.CompletedTask;
    }

    [RelayCommand]
    void ClearHistory() => _history.Clear();

    static async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { AppLog.Error("Home", "Aktion fehlgeschlagen", ex); }
    }

    public void Dispose()
    {
        _clock.Stop();
        _host.HomeSections.CollectionChanged -= OnSectionsChanged;
        _history.Changed -= RebuildHistory;
        foreach (var s in Sections) s.Dispose();
    }
}
