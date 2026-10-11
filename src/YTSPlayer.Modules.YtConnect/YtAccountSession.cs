using System.Text.Json;
using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Plugins;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.YtConnect.Scripts;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Modules.YtConnect;

/// <summary>
/// Anmeldung bei Google / YouTube Music. Die Google-Seite läuft in der Web-Ansicht, die dafür eingebettet im Hauptfenster erscheint.
/// Passwörter sehen wir nie. Gespeichert wird nur Name, E-Mail und das kleine Profilbild.
/// </summary>
public sealed class YtAccountSession : IAccountSession, IDisposable
{
    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // Manche Google-Bildserver antworten ohne Browser-Kennung mit 403.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("image/png,image/jpeg,image/*;q=0.8");
        return http;
    }

    readonly YtEngine _engine;
    readonly ISettingsStore _store;
    readonly IPerfTracker _perf;
    readonly INavigator _nav;
    readonly Func<Task> _stopPlayback;
    TaskCompletionSource<JsonElement>? _profileTcs;
    bool _sawAccounts;
    bool _avatarRefreshTried;
    int _loginGeneration;

    public YtAccountSession(YtEngine engine, ISettingsStore store, IPerfTracker perf, INavigator nav, Func<Task> stopPlayback)
    {
        _engine = engine;
        _store = store;
        _perf = perf;
        _nav = nav;
        _stopPlayback = stopPlayback;
        var cache = store.Load<YtProfileCache>("yt-profile");
        if (cache.LoggedIn || cache.Name.Length > 0 || cache.Email.Length > 0)
        {
            Profile = new AccountProfile(cache.Name, cache.Email, cache.Avatar);
            State = AccountState.LoggedIn;
        }
        engine.Message += OnMessage;
        engine.Navigated += OnNavigated;
    }

    public string DisplayName => "YouTube Music";

    public string? SettingsPageId => YtConnectPlugin.SettingsId;
    public AccountState State { get; private set; } = AccountState.LoggedOut;
    public AccountProfile? Profile { get; private set; }

    /// <summary>Letzte Meldung für die Anzeige (Fehler oder Hinweis).</summary>
    public string Notice { get; private set; } = "";

    public event Action? Changed;

    public void Dispose()
    {
        _engine.Message -= OnMessage;
        _engine.Navigated -= OnNavigated;
    }

    public async Task SignInAsync()
    {
        if (State != AccountState.LoggedOut) return;
        using var scope = _perf.Begin("Yt.Login");
        Notice = "";
        _sawAccounts = false;
        SetState(AccountState.LoginPanelOpen);
        _nav.NavigateTo("yt");
        try
        {
            await _engine.StartAsync(UrlNormalizer.Login);
            _engine.SetLoginMode(true);
        }
        catch (Exception ex)
        {
            scope.Fail();
            Notice = Loc.F("yt.account.loginFailed", ex.Message);
            await CancelLoginAsync();
        }
    }

    /// <summary>„Fertig“: prüft, ob die Anmeldung geklappt hat, und liest das Profil.</summary>
    public async Task FinishLoginAsync()
    {
        if (State != AccountState.LoginPanelOpen) return;
        var gen = ++_loginGeneration;
        using var scope = _perf.Begin("Yt.Verify");
        SetState(AccountState.Verifying);
        try
        {
            _engine.SetLoginMode(false);
            await _engine.Host.NavigateAndWaitAsync(UrlNormalizer.Home);
            JsonElement? result = null;
            for (var attempt = 0; attempt < 3 && result == null; attempt++)
            {
                await Task.Delay(1500);
                if (gen != _loginGeneration) return;
                result = await RequestProfileAsync(allowMenu: true);
            }
            if (result is not { } r || !r.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                scope.Fail();
                Notice = "Profil konnte nicht gelesen werden" + (result is { } e && e.TryGetProperty("reason", out var why) ? ": " + why.GetString() : ".");
                await FailLoginAsync();
                return;
            }
            if (!r.TryGetProperty("loggedIn", out var li) || !li.GetBoolean())
            {
                Notice = Loc.T("yt.account.none");
                await FailLoginAsync();
                return;
            }
            var name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var email = r.TryGetProperty("email", out var m) ? m.GetString() ?? "" : "";
            var photo = r.TryGetProperty("photo", out var p) ? p.GetString() ?? "" : "";
            var avatar = await LoadAvatarAsync(photo);
            if (avatar == null) Notice = AvatarFailure(r, photo);
            Profile = new AccountProfile(name, email, avatar);
            _store.Save("yt-profile", new YtProfileCache { Name = name, Email = email, Avatar = avatar, LoggedIn = true });
            await _engine.StopAsync();
            SetState(AccountState.LoggedIn);
        }
        catch (Exception ex)
        {
            scope.Fail();
            Notice = Loc.F("yt.account.loginError", ex.Message);
            await FailLoginAsync();
        }
    }

    /// <summary>„Abbrechen“: schließt das Login-Panel, ohne anzumelden.</summary>
    public async Task CancelLoginAsync()
    {
        if (State is not (AccountState.LoginPanelOpen or AccountState.Verifying)) return;
        _loginGeneration++;
        await FailLoginAsync();
    }

    public async Task SignOutAsync()
    {
        if (State != AccountState.LoggedIn) return;
        using var scope = _perf.Begin("Yt.Logout");
        SetState(AccountState.LoggingOut);
        try
        {
            await _stopPlayback();
            await _engine.EnsureRunningAsync("about:blank");
            await _engine.Host.ClearDataAsync();
            await _engine.StopAsync();
        }
        catch (Exception ex)
        {
            scope.Fail();
            Notice = "Abmelden: " + ex.Message;
        }
        _store.Save("yt-profile", new YtProfileCache());
        Profile = null;
        SetState(AccountState.LoggedOut);
    }

    async Task FailLoginAsync()
    {
        try
        {
            _engine.SetLoginMode(false);
            await _engine.StopAsync();
        }
        catch { }
        SetState(AccountState.LoggedOut);
    }

    async Task<JsonElement?> RequestProfileAsync(bool allowMenu)
    {
        var tcs = _profileTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _engine.ExecAsync(LibraryScripts.Profile(allowMenu));
        var done = await Task.WhenAny(tcs.Task, Task.Delay(6000));
        return done == tcs.Task ? tcs.Task.Result : null;
    }

    /// <summary>Lädt das Profilbild. Bei Fehlern steht der Grund in <see cref="Notice"/> (nur Host, keine Adresse mit Parametern).</summary>
    async Task<byte[]?> LoadAvatarAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) { Notice = Loc.T("yt.avatar.noUrl"); return null; }
        try
        {
            if (url.StartsWith("//")) url = "https:" + url;
            // Google-Bilder: Größe im Adress-Ende (=s88-c-k…) auf 96 px setzen, das spart Daten.
            url = System.Text.RegularExpressions.Regex.Replace(url, @"=s\d+(-[a-z0-9-]*)?$", "=s96-c");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { Notice = Loc.T("yt.avatar.badUrl"); return null; }
            var bytes = await Http.GetByteArrayAsync(uri);
            if (bytes.Length is > 0 and < 300_000) return bytes;
            Notice = Loc.F("yt.avatar.badSize", bytes.Length, uri.Host);
            return null;
        }
        catch (Exception ex)
        {
            Notice = Loc.F("yt.avatar.failed", ex.Message);
            return null;
        }
    }

    void OnMessage(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            if (d.RootElement.GetProperty("t").GetString() != "profile") return;
            _profileTcs?.TrySetResult(d.RootElement.Clone());
        }
        catch { }
    }

    static string AvatarFailure(JsonElement r, string photo)
    {
        var how = r.TryGetProperty("how", out var hw) ? hw.GetString() : "";
        var dbg = r.TryGetProperty("dbg", out var dg) ? dg.GetString() : "";
        var host = Uri.TryCreate(photo.StartsWith("//") ? "https:" + photo : photo, UriKind.Absolute, out var pu) ? pu.Host : "(keine)";
        return $"[Quelle {how}; {dbg}; Bild-Host {host}]";
    }

    /// <summary>
    /// Fehlt das Profilbild, wird es einmal pro App-Start nachgeladen. Läuft die Web-Ansicht nicht, wird sie dafür kurz
    /// versteckt gestartet und danach wieder beendet (außer YT spielt inzwischen). Läuft sie schon (Wiedergabe),
    /// wird das Konto-Menü nicht geöffnet.
    /// </summary>
    public async Task RefreshMissingAvatarAsync(Func<bool> ytPlaying)
    {
        if (_avatarRefreshTried || State != AccountState.LoggedIn || Profile?.Avatar is { Length: > 0 }) return;
        _avatarRefreshTried = true;
        using var scope = _perf.Begin("Yt.ProfileRefresh");
        var startedHere = false;
        try
        {
            startedHere = await _engine.EnsureRunningAsync(UrlNormalizer.Home);
            JsonElement? result = null;
            for (var attempt = 0; attempt < 3 && result == null; attempt++)
            {
                await Task.Delay(2000);
                if (State != AccountState.LoggedIn || !_engine.IsRunning) return;
                result = await RequestProfileAsync(allowMenu: startedHere && !ytPlaying());
            }
            if (result is not { } r || !r.TryGetProperty("loggedIn", out var li) || !li.GetBoolean())
            {
                scope.Fail();
                Notice = Loc.T("yt.avatar.noProfile");
                Changed?.Invoke();
                return;
            }
            var photo = r.TryGetProperty("photo", out var p) ? p.GetString() ?? "" : "";
            var avatar = await LoadAvatarAsync(photo);
            if (avatar == null || State != AccountState.LoggedIn || Profile == null)
            {
                scope.Fail();
                Notice = $"{Notice} {AvatarFailure(r, photo)}".Trim();
                Changed?.Invoke();
                return;
            }
            var name = r.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nn ? nn : Profile.Name;
            var email = r.TryGetProperty("email", out var m) && m.GetString() is { Length: > 0 } mm ? mm : Profile.Email;
            Profile = new AccountProfile(name, email, avatar);
            _store.Save("yt-profile", new YtProfileCache { Name = name, Email = email, Avatar = avatar, LoggedIn = true });
            Notice = "";
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            scope.Fail();
            Notice = "Profilbild: " + ex.Message;
            Changed?.Invoke();
        }
        finally
        {
            if (startedHere && !ytPlaying() && !_engine.LoginMode)
            {
                try { await _engine.StopAsync(); } catch { }
            }
        }
    }

    void OnNavigated(Uri? uri)
    {
        if (State != AccountState.LoginPanelOpen || uri == null) return;
        if (uri.Host.EndsWith("accounts.google.com")) _sawAccounts = true;
        else if (_sawAccounts && uri.Host == "music.youtube.com")
        {
            _sawAccounts = false;
            _ = AutoFinishAsync(_loginGeneration);
        }
    }

    async Task AutoFinishAsync(int gen)
    {
        await Task.Delay(2500);
        if (State == AccountState.LoginPanelOpen && gen == _loginGeneration) await FinishLoginAsync();
    }

    void SetState(AccountState s)
    {
        State = s;
        Changed?.Invoke();
    }
}
