using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Shell.Services;

namespace YTSPlayer.Shell.ViewModels;

/// <summary>Ein Schalter für ein Plugin.</summary>
public sealed partial class PluginToggle : ObservableObject
{
    readonly PluginHost _host;
    readonly string _id;
    bool _busy;

    [ObservableProperty] bool _isEnabled;
    [ObservableProperty] string _error = "";

    public PluginToggle(PluginHost host, IPlugin plugin)
    {
        _host = host;
        _id = plugin.Id;
        Name = plugin.DisplayName;
        CanChange = !plugin.IsCore;
        _isEnabled = host.IsEnabled(plugin.Id);
    }

    public string Name { get; }
    public bool CanChange { get; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_busy) return;
        _ = ApplyAsync(value);
    }

    async Task ApplyAsync(bool value)
    {
        Error = "";
        try { await _host.SetEnabledAsync(_id, value); }
        catch (Exception ex)
        {
            Error = ex.Message;
            _busy = true;
            IsEnabled = _host.IsEnabled(_id);
            _busy = false;
        }
    }
}

/// <summary>Ein Eintrag in der Liste links in den Einstellungen.</summary>
public sealed record SettingsCategory(string Id, string Title);

/// <summary>Einstellungs-Seite: links die Kategorien (Allgemein, Module, je ein Eintrag pro Modul), rechts der Inhalt.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string GeneralId = "general";
    public const string ModulesId = "modules";

    readonly AppSettings _settings;
    readonly ISettingsStore _store;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowGeneral), nameof(ShowModules), nameof(SelectedPluginPage), nameof(ShowPlugin))]
    SettingsCategory? _selectedCategory;

    readonly SleepTimer _sleep;

    public SettingsViewModel(AppSettings settings, ISettingsStore store, PluginHost host, SleepTimer sleep)
    {
        _settings = settings;
        _store = store;
        _sleep = sleep;
        sleep.Changed += () =>
        {
            OnPropertyChanged(nameof(SleepTimerEnabled));
            OnPropertyChanged(nameof(SleepTimerMinutes));
        };
        Plugins = [.. host.Plugins.Select(p => new PluginToggle(host, p))];
        PluginPages = host.SettingsPages;
        PluginPages.CollectionChanged += (_, _) => RebuildCategories();
        RebuildCategories();
    }

    public ObservableCollection<PluginToggle> Plugins { get; }
    public ObservableCollection<SettingsPage> PluginPages { get; }
    public ObservableCollection<SettingsCategory> Categories { get; } = [];

    public bool ShowGeneral => SelectedCategory?.Id == GeneralId;
    public bool ShowModules => SelectedCategory?.Id == ModulesId;
    public SettingsPage? SelectedPluginPage => PluginPages.FirstOrDefault(p => p.Id == SelectedCategory?.Id);
    public bool ShowPlugin => SelectedPluginPage != null;

    /// <summary>Wählt eine Kategorie (Id von Allgemein, Module oder einer Modul-Seite). Eine unbekannte Id wählt „Allgemein“.</summary>
    public void Select(string? id) => SelectedCategory = Categories.FirstOrDefault(c => c.Id == id) ?? Categories[0];

    void RebuildCategories()
    {
        var keep = SelectedCategory?.Id;
        Categories.Clear();
        Categories.Add(new SettingsCategory(GeneralId, "Allgemein"));
        Categories.Add(new SettingsCategory(ModulesId, "Module"));
        foreach (var p in PluginPages) Categories.Add(new SettingsCategory(p.Id, p.Title));
        Select(keep);
    }

    public bool MinimizeOnClose
    {
        get => _settings.MinimizeOnClose;
        set { _settings.MinimizeOnClose = value; _store.Save("app", _settings); OnPropertyChanged(); }
    }

    /// <summary>Sleep-Timer an/aus (gilt für alle Player).</summary>
    public bool SleepTimerEnabled
    {
        get => _sleep.Enabled;
        set { _sleep.Enabled = value; OnPropertyChanged(); }
    }

    /// <summary>Minuten ohne Bedienung bis zur Pause.</summary>
    public int SleepTimerMinutes
    {
        get => _sleep.Minutes;
        set { _sleep.Minutes = value; OnPropertyChanged(); }
    }

    /// <summary>Ordner der Logdateien (leer, wenn nicht eingerichtet).</summary>
    public string LogDirectory => AppLog.Directory ?? "";

    public int TargetMb
    {
        get => _settings.TargetMb;
        set { _settings.TargetMb = Math.Max(0, value); _store.Save("app", _settings); OnPropertyChanged(); }
    }
}
