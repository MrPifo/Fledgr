using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTSPlayer.Contracts.Plugins;

namespace YTSPlayer.Modules.YtConnect.ViewModels;

/// <summary>
/// Der YouTube-Abschnitt auf der Home-Seite: die ersten Karten und Titel der (gespeicherten) YT-Startseite.
/// Home startet keine Web-Ansicht; es zeigt nur, was <see cref="YtViewModel"/> schon aus dem Cache geladen hat.
/// </summary>
public sealed partial class YtHomeViewModel : ObservableObject, IHomeContent
{
    const int MaxItems = 12;

    readonly YtViewModel _yt;

    public YtHomeViewModel(YtViewModel yt)
    {
        _yt = yt;
        yt.HomeSections.CollectionChanged += OnSectionsChanged;
        Rebuild();
    }

    public ObservableCollection<TrackItem> Items { get; } = [];

    /// <summary>Wahr, wenn die YT-Startseite schon etwas enthält.</summary>
    public bool HasContent => Items.Count > 0;

    void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    void Rebuild()
    {
        Items.Clear();
        // Zuerst Karten (Alben, Playlists, Mixe), dann Titel: Karten sind das, was man auf der Startseite sucht.
        var all = _yt.HomeSections.OrderBy(s => s.IsCards ? 0 : 1).SelectMany(s => s.Items);
        foreach (var t in all.DistinctBy(t => t.Info.Id).Take(MaxItems)) Items.Add(t);
        OnPropertyChanged(nameof(HasContent));
    }

    /// <summary>Klick auf eine Kachel: Titel spielt sofort, Album/Playlist/Künstler öffnet die YT-Seite.</summary>
    [RelayCommand]
    Task Open(TrackItem? item)
    {
        if (item == null) return Task.CompletedTask;
        if (item.Info.Kind != "") _yt.ShowPage?.Invoke();
        return _yt.OpenCardAsync(item);
    }

    /// <summary>Knopf „Mix starten“ im Kopf des Abschnitts.</summary>
    public void StartMix() => _yt.HomeMixCommand.Execute(null);

    /// <summary>Knopf „Zufällig“ im Kopf des Abschnitts.</summary>
    public void StartShuffle() => _yt.HomeShuffleCommand.Execute(null);
}
