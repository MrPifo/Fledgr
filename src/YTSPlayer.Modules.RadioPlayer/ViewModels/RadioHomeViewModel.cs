using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using YTSPlayer.Contracts.Plugins;

namespace YTSPlayer.Modules.RadioPlayer.ViewModels;

/// <summary>
/// Der Radio-Abschnitt auf der Home-Seite: Favoriten, danach die zuletzt gehörten Sender.
/// Die Sender-Zeilen sind dieselben Objekte wie in der Radio-Seite (Logo, Play-Befehl, „läuft gerade“).
/// </summary>
public sealed class RadioHomeViewModel : ObservableObject, IHomeContent
{
    const int MaxItems = 12;

    readonly RadioViewModel _radio;

    public RadioHomeViewModel(RadioViewModel radio)
    {
        _radio = radio;
        radio.Favorites.CollectionChanged += OnListChanged;
        radio.Recent.CollectionChanged += OnListChanged;
        Rebuild();
    }

    public ObservableCollection<StationItem> Stations { get; } = [];

    /// <summary>Wahr, wenn es Favoriten oder zuletzt gehörte Sender gibt.</summary>
    public bool HasContent => Stations.Count > 0;

    void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    void Rebuild()
    {
        Stations.Clear();
        foreach (var s in _radio.Favorites.Concat(_radio.Recent).DistinctBy(s => s.Station.Id).Take(MaxItems)) Stations.Add(s);
        OnPropertyChanged(nameof(HasContent));
    }
}
