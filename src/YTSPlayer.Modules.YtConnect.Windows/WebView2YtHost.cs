using System.Drawing;
using Avalonia.Controls;
using Microsoft.Web.WebView2.Core;
using YTSPlayer.Contracts.Diagnostics;
using YTSPlayer.Contracts.Settings;
using YTSPlayer.Modules.YtConnect.Scripts;

namespace YTSPlayer.Modules.YtConnect.Windows;

/// <summary>
/// WebView2-Umsetzung der Web-Ansicht. Läuft versteckt in einem Fenster außerhalb des Bildschirms.
/// Für die Anmeldung wird sie in die <see cref="LoginSurface"/> des Hauptfensters umgehängt.
/// </summary>
public sealed class WebView2YtHost(YtSettings settings, IAppPaths paths) : IYtWebHost, IDisposable
{
    HiddenHostWindow? _win;
    LoginSurface? _surface;
    CoreWebView2Environment? _env;
    CoreWebView2Controller? _ctl;
    CoreWebView2? _web;
    TaskCompletionSource? _navTcs;
    bool _login;
    bool _onSurface;

    string DataDir => Path.Combine(paths.Cache, "WebView2");

    public bool IsRunning => _web != null;
    public bool BlockActive { get; set; } = true;
    public long Requests { get; private set; }
    public long Blocked { get; private set; }
    public long Bytes { get; private set; }

    public Control LoginSurface => _surface ??= CreateSurface();

    public event Action<string>? Message;
    public event Action<Uri?>? Navigated;
    public event Action? Crashed;

    /// <summary>Die Browser-Argumente, mit denen WebView2 gestartet wird (für Selbsttests).</summary>
    public string Args => BuildArgs();

    LoginSurface CreateSurface()
    {
        var s = new LoginSurface();
        s.Attached += () => { if (_login) MoveToSurface(); };
        s.Detaching += MoveToHidden;
        s.Resized += UpdateBounds;
        return s;
    }

    public async Task StartAsync(string url, CancellationToken ct = default)
    {
        if (_web != null) { _web.Navigate(url); return; }
        BlockActive = !_login;
        _win ??= CreateWindow();
        _win.ShowOffscreen();
        var hwnd = _win.Hwnd;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Kein Fenster-Handle für WebView2.");
        Directory.CreateDirectory(DataDir);
        var opt = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = BuildArgs() };
        _env = await CoreWebView2Environment.CreateAsync(null, DataDir, opt);
        _ctl = await _env.CreateCoreWebView2ControllerAsync(hwnd);
        _web = _ctl.CoreWebView2;
        // CoreWebView2 darf nur auf dem UI-Thread angefasst werden. Die Diagnose läuft im Hintergrund, darum merken wir uns die PID hier.
        try { _browserPid = (int)_web.BrowserProcessId; } catch { _browserPid = 0; }
        var st = _web.Settings;
        st.AreDevToolsEnabled = false;
        st.IsStatusBarEnabled = false;
        // Die Seite darf nie ein Dialogfenster zeigen (z. B. „Website verlassen?“ beim Wegnavigieren). Wir bestätigen still.
        st.AreDefaultScriptDialogsEnabled = false;
        _web.ScriptDialogOpening += (_, e) => e.Accept();
        st.IsZoomControlEnabled = false;
        st.IsGeneralAutofillEnabled = false;
        st.IsPasswordAutosaveEnabled = false;
        st.AreDefaultContextMenusEnabled = _login;
        _web.WebMessageReceived += (_, e) => Message?.Invoke(e.WebMessageAsJson);
        _web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == "https") _web?.Navigate(e.Uri);
        };
        _web.ProcessFailed += (_, _) => Crashed?.Invoke();
        _web.NavigationCompleted += (_, _) =>
        {
            _navTcs?.TrySetResult();
            Navigated?.Invoke(Uri.TryCreate(_web?.Source, UriKind.Absolute, out var u) ? u : null);
        };
        await _web.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript.Build(settings.SongMode, settings.Gain));
        ConfigureBlocking();
        _ctl.IsVisible = true;
        UpdateBounds();
        _web.Navigate(url);
        if (_login) MoveToSurface();
    }

    public async Task StopAsync()
    {
        if (_web == null) return;
        // Chromium schreibt Cookies nur gesammelt (alle ~30 s) und beim sauberen Beenden des Browser-Prozesses.
        // Darum warten wir hier, bis der Prozess wirklich weg ist, sonst geht die Anmeldung beim App-Ende verloren.
        System.Diagnostics.Process? browser = null;
        try { browser = System.Diagnostics.Process.GetProcessById(_browserPid); } catch { }
        _browserPid = 0;
        try { _ctl?.Close(); } catch { }
        _ctl = null;
        _web = null;
        _env = null;
        _login = false;
        _onSurface = false;
        BlockActive = true;
        _win?.Hide();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (browser != null)
        {
            try { await Task.Run(() => browser.WaitForExit(8000)); } catch { }
            browser.Dispose();
        }
    }

    public void Navigate(string url) => _web?.Navigate(url);

    /// <summary>Zeigt HTML direkt an (für Selbsttests).</summary>
    public void NavigateToString(string html) => _web?.NavigateToString(html);

    public async Task NavigateAndWaitAsync(string url, int timeoutMs = 20000)
    {
        if (_web == null) return;
        _navTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _web.Navigate(url);
        await Task.WhenAny(_navTcs.Task, Task.Delay(timeoutMs));
    }

    public async Task<string> ExecAsync(string script) => _web == null ? "" : await _web.ExecuteScriptAsync(script);

    public void SetMemoryLow(bool low)
    {
        if (_web != null) _web.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal;
    }

    bool _noProcessInfos;
    volatile int _browserPid;

    public IReadOnlyList<ProcessRef> Processes()
    {
        if (_env == null) return [];
        if (!_noProcessInfos)
        {
            try
            {
                return _env.GetProcessInfos().Select(p => new ProcessRef(p.ProcessId, "WebView2 " + p.Kind)).ToList();
            }
            catch (Exception)
            {
                // Ältere WebView2-Runtime kennt GetProcessInfos nicht (NotImplemented / InvalidCast). Nicht mehr versuchen.
                _noProcessInfos = true;
            }
        }
        try
        {
            // Ersatz ohne GetProcessInfos: Browser-Prozess plus alle seine Unterprozesse (Renderer, GPU, Netzwerk …).
            var root = _browserPid;
            if (_web == null || root == 0) return [];
            var list = new List<ProcessRef> { new(root, "WebView2 Browser") };
            foreach (var (pid, exe) in YTSPlayer.Platform.Windows.Native.Descendants(root))
                list.Add(new ProcessRef(pid, "WebView2 " + Path.GetFileNameWithoutExtension(exe)));
            return list;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void SetLoginMode(bool on)
    {
        _login = on;
        BlockActive = !on;
        if (_web != null) _web.Settings.AreDefaultContextMenusEnabled = on;
        if (on) { if (_surface is { Hwnd: not 0 }) MoveToSurface(); }
        else MoveToHidden();
    }

    public async Task ClearDataAsync()
    {
        if (_web == null) return;
        await _web.Profile.ClearBrowsingDataAsync();
    }

    /// <summary>Lage des Fensters der Web-Ansicht als Text (für Selbsttests).</summary>
    public string WindowInfo() => YTSPlayer.Platform.Windows.Native.DescribeWindow(_onSurface ? _surface?.Hwnd ?? IntPtr.Zero : _win?.Hwnd ?? IntPtr.Zero);

    public void Dispose() => CloseWindow();

    /// <summary>Beendet das Host-Fenster endgültig (beim Beenden der App).</summary>
    public void CloseWindow()
    {
        if (_win == null) return;
        _win.AllowClose = true;
        _win.Close();
    }

    HiddenHostWindow CreateWindow()
    {
        var w = new HiddenHostWindow();
        w.Resized += UpdateBounds;
        return w;
    }

    void MoveToSurface()
    {
        if (_ctl == null || _surface is not { Hwnd: not 0 } s) return;
        _ctl.ParentWindow = s.Hwnd;
        _onSurface = true;
        _ctl.IsVisible = true;
        UpdateBounds();
    }

    void MoveToHidden()
    {
        _onSurface = false;
        if (_ctl == null || _win == null) return;
        _win.ShowOffscreen();
        _ctl.ParentWindow = _win.Hwnd;
        _ctl.IsVisible = true;
        UpdateBounds();
    }

    void UpdateBounds()
    {
        if (_ctl == null) return;
        int w, h;
        if (_onSurface && _surface != null) (w, h) = _surface.PixelSize;
        else if (_win != null)
        {
            var sc = _win.RenderScaling;
            w = (int)(_win.ClientSize.Width * sc);
            h = (int)(_win.ClientSize.Height * sc);
        }
        else return;
        _ctl.Bounds = new Rectangle(0, 0, w, h);
        _ctl.NotifyParentWindowPositionChanged();
    }

    string BuildArgs()
    {
        var a = new List<string> { "--autoplay-policy=no-user-gesture-required" };
        var off = new List<string>();
        if (settings.DisableGpu) { a.Add("--disable-gpu"); a.Add("--disable-gpu-compositing"); }
        if (settings.InProcessGpu) a.Add("--in-process-gpu");
        if (settings.LimitV8) a.Add("--js-flags=--scavenger_max_new_space_capacity_mb=8");
        if (settings.ProcessPerSite) a.Add("--process-per-site");
        if (settings.SiteIsolationOff) { off.Add("IsolateOrigins"); off.Add("site-per-process"); }
        if (settings.OffscreenInsteadOfHide) off.Add("CalculateNativeWinOcclusion");
        if (off.Count > 0) a.Add("--disable-features=" + string.Join(',', off));
        return string.Join(' ', a);
    }

    void ConfigureBlocking()
    {
        if (_web == null) return;
        if (settings.BlockImages) _web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
        if (settings.BlockStyles) _web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Stylesheet);
        if (settings.BlockFonts) _web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Font);
        _web.WebResourceRequested += (_, e) =>
        {
            if (!BlockActive || _env == null) return;
            var ct = e.ResourceContext switch
            {
                CoreWebView2WebResourceContext.Stylesheet => "text/css",
                CoreWebView2WebResourceContext.Font => "font/woff2",
                _ => "image/gif",
            };
            e.Response = _env.CreateWebResourceResponse(null, 204, "No Content", "Content-Type: " + ct);
            Blocked++;
        };
        _web.WebResourceResponseReceived += (_, e) =>
        {
            Requests++;
            try
            {
                var h = e.Response.Headers;
                if (h.Contains("Content-Length") && long.TryParse(h.GetHeader("Content-Length"), out var n)) Bytes += n;
            }
            catch { }
        };
    }
}
