using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using YTSPlayer.Modules.YtConnect;
using YTSPlayer.Platform.Windows;
using YTSPlayer.Shell;

namespace YTSPlayer.Desktop.SelfTests;

/// <summary>
/// Oberflächen-Selbsttest (--selftest-ui): schaltet durch die Seiten, speichert Bilder (shot-*.png neben der EXE),
/// öffnet die eingebettete Anmeldung und macht davon ein Bildschirmfoto. Es wird nichts eingegeben und nichts angemeldet.
/// </summary>
static class UiShots
{
    public static async Task RunAsync(ShellApp shell, Action quit)
    {
        var o = new StringBuilder();
        void L(string t) => o.AppendLine(t);
        try
        {
            L($"UI-Test {DateTime.Now:s}");
            await Task.Delay(3500);
            var win = shell.MainWindow;
            win.Topmost = true;
            win.Activate();
            await Task.Delay(500);
            L("Seiten: " + string.Join(", ", shell.ViewModel.Nav.Select(p => p.Id)));
            foreach (var id in shell.ViewModel.Nav.Select(p => p.Id).ToList())
            {
                shell.ViewModel.NavigateTo(id);
                await Task.Delay(1500);
                Render(win, $"shot-{id}.png");
            }

            if (shell.Host.Account is { } fake and not YtAccountSession)
            {
                L($"Test-Konto: {fake.Profile?.Name} / {fake.Profile?.Email}");
                shell.ViewModel.NavigateTo("music");
                await Task.Delay(800);
                Render(win, "shot-konto-angemeldet.png");
            }
            shell.ViewModel.OpenWidget?.Invoke();
            shell.ViewModel.OpenDiagnostics?.Invoke();
            await Task.Delay(2500);
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dl)
                foreach (var w in dl.Windows.Where(w => w != win && w.IsVisible && w.Title != null && !w.Title.StartsWith("Fledgr –")))
                    Render(w, "shot-" + (w.Title!.Contains("Widget") ? "widget" : "diagnose") + ".png");
            // Das Widget hat das Hauptfenster versteckt. Zurückwechseln, damit die weiteren Schritte das Hauptfenster sehen.
            shell.ViewModel.ShowMain?.Invoke();
            await Task.Delay(800);
            if (shell.Host.Account is YtAccountSession acc)
            {
                L($"Konto-Zustand vorher: {acc.State}");
                await acc.SignInAsync();
                L($"Konto-Zustand nach SignIn: {acc.State}");
                await Task.Delay(9000);
                Screen(win, "shot-login-screen.png");
                Render(win, "shot-login.png");
                await acc.CancelLoginAsync();
                await Task.Delay(1500);
                L($"Konto-Zustand nach Abbrechen: {acc.State}");
                shell.ViewModel.NavigateTo("yt");
                await Task.Delay(800);
                Render(win, "shot-yt-nach-abbruch.png");
            }
            L("Fertig.");
        }
        catch (Exception ex) { L("FEHLER: " + ex); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest-ui-result.txt"), o.ToString());
        try { await shell.Host.ShutdownAsync(); } catch { }
        quit();
    }

    /// <summary>Rendert den Inhalt des Fensters (nur Avalonia-Teile, ohne eingebettete Fremdfenster).</summary>
    static void Render(Avalonia.Controls.Window win, string file)
    {
        var scale = win.RenderScaling;
        var size = new PixelSize((int)(win.Bounds.Width * scale), (int)(win.Bounds.Height * scale));
        var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        rtb.Render(win);
        rtb.Save(Path.Combine(AppContext.BaseDirectory, file));
    }

    /// <summary>Foto vom Bildschirm-Bereich des Fensters (zeigt auch die eingebettete Web-Ansicht).</summary>
    static void Screen(Avalonia.Controls.Window win, string file)
    {
        var h = win.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var (x, y, w, ht) = Native.WindowBounds(h);
        var bytes = Native.CaptureScreen(x, y, w, ht);
        if (bytes == null) return;
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            using var bmp = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pin.AddrOfPinnedObject(), new PixelSize(w, ht), new Vector(96, 96), w * 4);
            bmp.Save(Path.Combine(AppContext.BaseDirectory, file));
        }
        finally { pin.Free(); }
    }
}
