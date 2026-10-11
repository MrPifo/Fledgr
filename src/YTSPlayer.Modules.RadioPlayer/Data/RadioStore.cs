using YTSPlayer.Contracts.Settings;

namespace YTSPlayer.Modules.RadioPlayer.Data;

/// <summary>Ein gemerkter Sender: Favorit, zuletzt gespielt oder beides. Wird in radio.json gespeichert.</summary>
public sealed class SavedStation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string Country { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Codec { get; set; } = "";
    public int Bitrate { get; set; }
    public string? LogoUrl { get; set; }
    public bool Favorite { get; set; }
    public DateTime? LastPlayed { get; set; }
    public StationStatus Status { get; set; }

    public SavedStation Clone() => (SavedStation)MemberwiseClone();
}

/// <summary>Inhalt von radio.json (Abschnitt „radio“).</summary>
public sealed class RadioSettings
{
    public List<SavedStation> Stations { get; set; } = [];
    public List<RadioCountry> Countries { get; set; } = [];
    public DateTime? CountriesUpdated { get; set; }
}

/// <summary>
/// Favoriten, zuletzt gespielte Sender und die Länderliste. Jede Änderung wird sofort gespeichert.
/// Die Lesemethoden liefern Kopien, damit die Oberfläche nicht mitten in einer Änderung liest.
/// </summary>
public sealed class RadioStore(ISettingsStore settings)
{
    public const int MaxRecent = 20;

    readonly object _lock = new();
    readonly RadioSettings _data = settings.Load<RadioSettings>("radio");

    public IReadOnlyList<SavedStation> Favorites
    {
        get { lock (_lock) return _data.Stations.Where(s => s.Favorite).Select(s => s.Clone()).ToList(); }
    }

    /// <summary>Zuletzt gespielt, neuester zuerst, ohne Favoriten (die stehen schon bei „Favoriten“). Höchstens <see cref="MaxRecent"/>.</summary>
    public IReadOnlyList<SavedStation> Recent
    {
        get
        {
            lock (_lock)
                return _data.Stations.Where(s => s.LastPlayed != null && !s.Favorite).OrderByDescending(s => s.LastPlayed).Take(MaxRecent).Select(s => s.Clone()).ToList();
        }
    }

    public IReadOnlyList<RadioCountry> Countries
    {
        get { lock (_lock) return _data.Countries.ToList(); }
    }

    public DateTime? CountriesUpdated
    {
        get { lock (_lock) return _data.CountriesUpdated; }
    }

    /// <summary>Der gemerkte Sender zu dieser Stream-Adresse oder null.</summary>
    public SavedStation? Find(string streamUrl)
    {
        lock (_lock) return _data.Stations.FirstOrDefault(s => s.StreamUrl == streamUrl)?.Clone();
    }

    public void SetCountries(IReadOnlyList<RadioCountry> countries)
    {
        lock (_lock)
        {
            _data.Countries = countries.ToList();
            _data.CountriesUpdated = DateTime.UtcNow;
            Save();
        }
    }

    /// <summary>Setzt oder entfernt den Favoriten. Ein Sender ohne Verlauf fällt ganz weg.</summary>
    public void SetFavorite(RadioStation station, bool favorite)
    {
        lock (_lock)
        {
            var entry = Upsert(station);
            entry.Favorite = favorite;
            if (!favorite && entry.LastPlayed == null) _data.Stations.Remove(entry);
            Save();
        }
    }

    /// <summary>Merkt den Sender als zuletzt gespielt. Über das Limit hinaus fallen die ältesten ohne Favorit weg.</summary>
    public void MarkPlayed(RadioStation station)
    {
        lock (_lock)
        {
            Upsert(station).LastPlayed = DateTime.UtcNow;
            var overflow = _data.Stations
                .Where(s => !s.Favorite && s.LastPlayed != null)
                .OrderByDescending(s => s.LastPlayed)
                .Skip(MaxRecent)
                .ToList();
            foreach (var s in overflow) _data.Stations.Remove(s);
            Save();
        }
    }

    public void SetStatus(string streamUrl, StationStatus status)
    {
        lock (_lock)
        {
            var entry = _data.Stations.FirstOrDefault(s => s.StreamUrl == streamUrl);
            if (entry == null || entry.Status == status) return;
            entry.Status = status;
            Save();
        }
    }

    /// <summary>Die Datenbank hat die Adresse geändert. Es ist derselbe Sender, nur mit neuer Adresse.</summary>
    public void ReplaceUrl(string oldUrl, string newUrl)
    {
        lock (_lock)
        {
            var entry = _data.Stations.FirstOrDefault(s => s.StreamUrl == oldUrl);
            if (entry == null) return;
            entry.StreamUrl = newUrl;
            entry.Status = StationStatus.Ok;
            Save();
        }
    }

    SavedStation Upsert(RadioStation station)
    {
        var entry = _data.Stations.FirstOrDefault(s => s.Id == station.Id) ?? _data.Stations.FirstOrDefault(s => s.StreamUrl == station.StreamUrl);
        if (entry == null)
        {
            entry = new SavedStation();
            _data.Stations.Add(entry);
        }
        if (entry.StreamUrl != station.StreamUrl) entry.Status = StationStatus.Ok;
        entry.Id = station.Id;
        entry.Name = station.Name;
        entry.StreamUrl = station.StreamUrl;
        entry.Country = station.Country;
        entry.Tags = station.Tags;
        entry.Codec = station.Codec;
        entry.Bitrate = station.Bitrate;
        entry.LogoUrl = station.LogoUrl;
        return entry;
    }

    void Save() => settings.Save("radio", _data);
}
