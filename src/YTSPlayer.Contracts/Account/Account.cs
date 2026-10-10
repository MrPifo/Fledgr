namespace YTSPlayer.Contracts.Account;

/// <summary>Zustand der Anmeldung.</summary>
public enum AccountState
{
    LoggedOut,
    LoginPanelOpen,
    Verifying,
    LoggedIn,
    LoggingOut,
}

/// <summary>Angezeigtes Profil. Das Bild ist klein (ca. 64 px). Nie Passwort oder Token.</summary>
public sealed record AccountProfile(string Name, string Email, byte[]? Avatar);

/// <summary>Anmeldung eines Moduls (YouTube Music). Die Shell zeigt Profil und Abmelden-Knopf.</summary>
public interface IAccountSession
{
    string DisplayName { get; }

    /// <summary>Id der Einstellungs-Seite des Moduls (siehe <c>SettingsPage.Id</c>), die das Profil-Menü öffnet. Null, wenn es keine gibt.</summary>
    string? SettingsPageId => null;

    AccountState State { get; }
    AccountProfile? Profile { get; }

    /// <summary>Zustand oder Profil haben sich geändert. Kann von jedem Thread kommen.</summary>
    event Action? Changed;

    /// <summary>Öffnet den Login (eingebettetes Panel).</summary>
    Task SignInAsync();

    Task SignOutAsync();
}
