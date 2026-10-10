using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Media;
using YTSPlayer.Contracts.Playback;

namespace YTSPlayer.Contracts.Plugins;

/// <summary>Eine Seite in der Navigation. Die Shell findet die passende View über das ViewModel.</summary>
public sealed record NavPage(string Id, string Title, string Icon, object ViewModel, int Order = 100);

/// <summary>Eine Einstellungs-Seite eines Plugins.</summary>
public sealed record SettingsPage(string Id, string Title, object ViewModel);

/// <summary>Ein Eintrag im Tray-Menü.</summary>
public sealed record TrayEntry(string Id, string Text, Action Invoke);

/// <summary>Das, was ein Plugin beim Aktivieren in die App einhängt. Beim Deaktivieren entfernt die Shell alles wieder.</summary>
public interface IPluginContext
{
    /// <summary>Dienste der App (Einstellungen, Pfade, Player, PerfTracker …).</summary>
    IServiceProvider Services { get; }

    void AddSource(IPlaybackSource source);
    void AddProvider(IMediaProvider provider);
    void AddPage(NavPage page);
    void AddSettingsPage(SettingsPage page);
    void AddTrayEntry(TrayEntry entry);
    void AddDiagnostics(IDiagnosticsContributor contributor);
    void SetAccount(IAccountSession session);
}

/// <summary>Ein Plugin. Der Kern (MusicPlayer) ist immer aktiv, die anderen lassen sich ein- und ausschalten.</summary>
public interface IPlugin
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>Kern-Plugins lassen sich nicht abschalten.</summary>
    bool IsCore { get; }

    /// <summary>Soll das Plugin ohne Einstellung aktiv sein?</summary>
    bool EnabledByDefault { get; }

    Task ActivateAsync(IPluginContext context, CancellationToken ct);

    /// <summary>Beendet Ressourcen (z. B. die WebView2).</summary>
    Task DeactivateAsync();
}

/// <summary>Verwaltung der Plugins. Die Shell setzt sie um.</summary>
public interface IPluginRegistry
{
    IReadOnlyList<IPlugin> Plugins { get; }
    bool IsEnabled(string pluginId);
    Task SetEnabledAsync(string pluginId, bool enabled);

    /// <summary>Ein Plugin wurde ein- oder ausgeschaltet.</summary>
    event Action? Changed;
}

/// <summary>Navigation in der Shell.</summary>
public interface INavigator
{
    void NavigateTo(string pageId);
}
