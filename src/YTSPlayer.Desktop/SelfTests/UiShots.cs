using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using YTSPlayer.Contracts.Playback;
using YTSPlayer.Modules.YtConnect;
using YTSPlayer.Platform.Windows;
using YTSPlayer.Shell;

namespace YTSPlayer.Desktop.SelfTests;

/// <summary>
/// Oberflächen-Selbsttest (--selftest-ui, mit --only=teil1,teil2 nur einzelne Teile): schaltet durch die Seiten, speichert Bilder (shot-*.png neben der EXE),
/// öffnet die eingebettete Anmeldung und macht davon ein Bildschirmfoto. Es wird nichts eingegeben und nichts angemeldet.
/// </summary>
static class UiShots
{
    public static async Task RunAsync(ShellApp shell, Action quit)
    {
        var o = new StringBuilder();
        void L(string t) => o.AppendLine(t);
        // --only=home,settings zeigt nur diese Teile (pages, settings, yt, home, player, konto, widget, login). Ohne --only läuft alles.
        var only = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal))?[7..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool Want(string part) => only == null || only.Contains(part, StringComparer.OrdinalIgnoreCase);
        try
        {
            L($"UI-Test {DateTime.Now:s}");
            await Task.Delay(3500);
            var win = shell.MainWindow;
            win.Topmost = true;
            win.Activate();
            await Task.Delay(500);
            L("Seiten: " + string.Join(", ", shell.ViewModel.Nav.Select(p => p.Id)));
            if (Want("pages"))
            {
                foreach (var id in shell.ViewModel.Nav.Select(p => p.Id).ToList())
                {
                    shell.ViewModel.NavigateTo(id);
                    await Task.Delay(1500);
                    Render(win, $"shot-{id}.png");
                }
            }

            if (Want("settings"))
            {
                // Einstellungen sind ein Popup über der zuletzt offenen Seite.
                shell.ViewModel.NavigateTo("settings");
                await Task.Delay(1200);
                Render(win, "shot-settings.png");
                shell.ViewModel.CloseSettingsCommand.Execute(null);
                await Task.Delay(500);
            }

            // Startseite von YouTube Music mit Beispieldaten (kein Konto, kein Netz nötig).
            if (Want("yt") || Want("home"))
            {
                if (shell.ViewModel.Nav.FirstOrDefault(p => p.Id == "yt")?.ViewModel is YTSPlayer.Modules.YtConnect.ViewModels.YtViewModel yt)
                {
                    TrackInfo T(string title, string artist, string id) => new(id, title, artist, "3:42", "");
                    TrackInfo C(string title, string artist, string id) => new(id, title, artist, "", "", "", "playlist", id);
                    yt.ApplyPreview(
                    [
                        new ArtistSectionData("Schnell wieder einsteigen", [T("Nachtfahrt", "Lena Vorst", "a1"), T("Kleine Fenster", "Ronja Maar", "a2"), T("Weißer Sand", "Mara Lind", "a3"), T("Gelbes Licht", "Tomas Ehre", "a4")], SectionLayout.Rows),
                        new ArtistSectionData("Mixe für dich", [C("Heimweg-Mix", "Lena Vorst und mehr", "m1"), C("Abendlicht", "Ronja Maar und mehr", "m2"), C("Frühschicht", "Mara Lind und mehr", "m3"), C("Sonntag", "Tomas Ehre und mehr", "m4"), C("Regen", "Wir sind wild", "m5")]),
                    ],
                    [new HomeChip("Entspannen", "p1", false), new HomeChip("Fokus", "p2", false), new HomeChip("Training", "p3", false), new HomeChip("Pendeln", "p4", true), new HomeChip("Party", "p5", false)]);
                    shell.ViewModel.NavigateTo("yt");
                    await Task.Delay(1500);
                    Render(win, "shot-yt-startseite.png");
                    // Hover von Hand auf die erste Karte setzen (Abdunkeln mit Punktraster, Bild wächst leicht).
                    if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<Avalonia.Controls.StackPanel>().FirstOrDefault(p => p.Classes.Contains("tile")) is { } tile)
                    {
                        ((Avalonia.Controls.IPseudoClasses)tile.Classes).Add(":pointerover");
                        await Task.Delay(400);
                        Render(win, "shot-yt-karte-hover.png");
                        ((Avalonia.Controls.IPseudoClasses)tile.Classes).Remove(":pointerover");
                    }

                    // Playlist-Liste mit Beispielen: eine Playlist enthält den laufenden Titel (Hervorhebung und Balken).
                    if (Want("yt"))
                    {
                        yt.Items.Clear();
                        foreach (var (id, t, sub) in new[] { ("p1", "Abendmix", "24 Titel"), ("p2", "Fokus-Playlist", "58 Titel"), ("p3", "Roadtrip", "112 Titel") })
                            yt.Items.Add(new YTSPlayer.Modules.YtConnect.PlaylistInfo(id, t, sub));
                        yt.Items[1].SetPlaying(true, true);
                        yt.Selected = null;
                        yt.State = YTSPlayer.Contracts.LoadState.Ready;
                        yt.ShowPlaylistsTabCommand.Execute(null);
                        await Task.Delay(800);
                        Render(win, "shot-yt-playlists.png");
                        // Hover von Hand auf die markierte Zeile setzen: die Hervorhebung muss weichen.
                        if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(win).OfType<Avalonia.Controls.ListBoxItem>().FirstOrDefault(i => i.DataContext == yt.Items[1]) is { } hov)
                        {
                            ((Avalonia.Controls.IPseudoClasses)hov.Classes).Add(":pointerover");
                            await Task.Delay(300);
                            Render(win, "shot-yt-playlists-hover.png");
                            ((Avalonia.Controls.IPseudoClasses)hov.Classes).Remove(":pointerover");
                        }
                        yt.ShowHomeTabCommand.Execute(null);
                    }
                }
            }

            if (Want("home"))
            {
                // Home mit Beispieldaten: Playlist, Verlauf und die YT-Startseite von oben.
                if (shell.ViewModel.Nav.FirstOrDefault(p => p.Id == "music")?.ViewModel is YTSPlayer.Modules.MusicPlayer.ViewModels.LibraryViewModel music)
                {
                    music.NewPlaylistName = "Abendmix";
                    music.CreatePlaylistCommand.Execute(null);
                }
                HistoryEntry H(string src, string title, string artist, int min) => new() { SourceId = src, ItemId = title, Title = title, Artist = artist, PlayedUtc = DateTime.UtcNow.AddMinutes(-min), DurationSeconds = src == "radio" ? 0 : 252 };
                shell.History.Add(H("radio", "Hitradio Ö3", "Österreich", 190));
                shell.History.Add(H("local", "Kleine Fenster", "Ronja Maar", 75));
                shell.History.Add(H("yt", "Nachtfahrt", "Lena Vorst", 12));
                shell.ViewModel.NavigateTo("home");
                var normalHeight = win.Height;
                win.Height = 1300;
                await Task.Delay(1500);
                Render(win, "shot-home-voll.png");
                win.Height = normalHeight;
            await Task.Delay(600);
            }

            // Menü mit Scrollbalken (Teil „menu“): ein ContextMenu mit vielen Einträgen, frei in ein Fenster gesetzt und gerendert.
            if (Want("menu"))
            {
                var menu = new Avalonia.Controls.ContextMenu();
                for (var i = 1; i <= 40; i++) menu.Items.Add(new Avalonia.Controls.MenuItem { Header = "Eintrag " + i });
                var holder = new Avalonia.Controls.Window
                {
                    Width = 320, Height = 460, Content = new Avalonia.Controls.Border { Child = menu, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual, Position = new PixelPoint(60, 60),
                };
                holder.Show();
                await Task.Delay(1000);
                Render(holder, "shot-menu.png");
                holder.Close();
            }

            // Listen-Seite mit Scrollbalken (Teil „list“): ListBox.page in einer Seiten-Hülle wie im Hauptfenster.
            if (Want("list"))
            {
                var list = new Avalonia.Controls.ListBox { Classes = { "page" }, ItemsSource = Enumerable.Range(1, 60).Select(i => "Playlist " + i).ToList() };
                var page = new Avalonia.Controls.UserControl { Content = new Avalonia.Controls.Grid { Children = { new YTSPlayer.Ui.Controls.LoadingState { State = YTSPlayer.Contracts.LoadState.Ready, Content = list } } } };
                var shell2 = new Avalonia.Controls.TransitioningContentControl { Content = page, Margin = new Thickness(16, 12, 16, 12) };
                var holder = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = shell2, WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual, Position = new PixelPoint(60, 60) };
                holder.Show();
                await Task.Delay(1200);
                Render(holder, "shot-list.png");
                // Hover von Hand setzen (die Maus gibt es im Test nicht): zweite Zeile.
                if (list.ContainerFromIndex(1) is Avalonia.Controls.ListBoxItem hover)
                {
                    ((Avalonia.Controls.IPseudoClasses)hover.Classes).Add(":pointerover");
                    await Task.Delay(300);
                    Render(holder, "shot-list-hover.png");
                }
                holder.Close();
            }

            // Knöpfe (Teil „buttons“): roter Knopf mit Punktraster, normal und beim Überfahren.
            if (Want("buttons"))
            {
                var normal = new Avalonia.Controls.Button { Content = "Abmelden", Classes = { "danger", "dots" } };
                var hover = new Avalonia.Controls.Button { Content = "Abmelden", Classes = { "danger", "dots" } };
                ((Avalonia.Controls.IPseudoClasses)hover.Classes).Add(":pointerover");
                var row = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Margin = new Thickness(20), Children = { normal, hover } };
                var holder = new Avalonia.Controls.Window { Width = 360, Height = 100, Content = row, WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual, Position = new PixelPoint(60, 60) };
                holder.Show();
                await Task.Delay(800);
                Render(holder, "shot-buttons.png");
                holder.Close();
            }

            if (Want("player"))
            {
                // Player-Karte mit einem laufenden Titel (nur Anzeige, nichts wird abgespielt).
                {
                    var vm = shell.ViewModel;
                    vm.Title = "Nachtfahrt";
                    vm.Artist = "Lena Vorst";
                    vm.PlayIcon = YTSPlayer.Ui.Icons.Pause;
                    vm.CanSeek = true; vm.CanNext = true; vm.CanPrevious = true; vm.CanVolume = true; vm.CanShuffle = true; vm.CanLike = true; vm.CanTrackMenu = true; vm.CanAddToPlaylist = true;
                    vm.IsShuffle = true; vm.IsLiked = true;
                    vm.DurationSeconds = 252; vm.Position = 108; vm.PositionText = "1:48"; vm.DurationText = "4:12"; vm.Volume = 0.7;
                    vm.Status = "Verbindet …";
                    await Task.Delay(800);
                    Render(win, "shot-player.png");
                    vm.SettingsVm.PlayerDocked = true;
                    await Task.Delay(500);
                    Render(win, "shot-player-docked.png");
                    vm.SettingsVm.PlayerDocked = false;
                }
            }

            if (Want("konto"))
            {
                if (shell.Host.Account is { } fake and not YtAccountSession)
                {
                    L($"Test-Konto: {fake.Profile?.Name} / {fake.Profile?.Email}");
                    shell.ViewModel.NavigateTo("music");
                    await Task.Delay(800);
                    Render(win, "shot-konto-angemeldet.png");
                }
            }

            if (Want("widget"))
            {
                shell.ViewModel.OpenWidget?.Invoke();
                shell.ViewModel.OpenDiagnostics?.Invoke();
                await Task.Delay(2500);
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime dl)
                    foreach (var w in dl.Windows.Where(w => w != win && w.IsVisible && w.Title != null && !w.Title.StartsWith("Fledgr –")))
                        Render(w, "shot-" + (w.Title!.Contains("Widget") ? "widget" : "diagnose") + ".png");
                // Das Widget hat das Hauptfenster versteckt. Zurückwechseln, damit die weiteren Schritte das Hauptfenster sehen.
                shell.ViewModel.ShowMain?.Invoke();
                await Task.Delay(800);
            }

            if (Want("login"))
            {
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
