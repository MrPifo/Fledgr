using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using YTSPlayer.Shell.ViewModels;
using YTSPlayer.Contracts.Localization;

namespace YTSPlayer.Shell.Services;

/// <summary>
/// Tray-Symbol mit Menü. Zeigt bei Anmeldung das Profilbild als Symbol und Name/E-Mail im Tooltip und als Menü-Kopf.
/// Nur für Desktop gedacht. Auf Android wird dieser Dienst nicht erzeugt.
/// </summary>
public sealed class TrayService : IDisposable
{
    readonly Application _app;
    readonly ShellViewModel _vm;
    readonly PluginHost _host;
    readonly WindowIcon _defaultIcon;
    readonly TrayIcon _icon;

    public TrayService(Application app, ShellViewModel vm, PluginHost host, Stream defaultIcon)
    {
        _app = app;
        _vm = vm;
        _host = host;
        _defaultIcon = new WindowIcon(defaultIcon);
        _icon = new TrayIcon { Icon = _defaultIcon, ToolTipText = "Fledgr", IsVisible = true };
        _icon.Clicked += (_, _) => vm.ShowMain?.Invoke();
        host.AccountChanged += Rebuild;
        host.Changed += Rebuild;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.CanNext) or nameof(ShellViewModel.CanPrevious) or nameof(ShellViewModel.Title)
                or nameof(ShellViewModel.Artist) or nameof(ShellViewModel.PlayIcon) or nameof(ShellViewModel.SleepOn)) Rebuild();
        };
        TrayIcon.SetIcons(app, [_icon]);
        Rebuild();
        YTSPlayer.Contracts.Diagnostics.AppLog.Info("Tray", "Tray-Symbol angelegt");
    }

    public void Dispose() => _icon.Dispose();

    void Rebuild()
    {
        var menu = new NativeMenu();
        var account = _host.Account;
        if (account != null)
        {
            if (_vm.IsLoggedIn)
            {
                menu.Add(new NativeMenuItem($"{_vm.AccountName}  ({_vm.AccountEmail})") { IsEnabled = false });
                menu.Add(Item(Loc.T("shell.account.signout"), () => _vm.SignOutCommand.Execute(null)));
            }
            else if (_vm.CanSignIn)
            {
                menu.Add(new NativeMenuItem(Loc.T("shell.tray.signedOut")) { IsEnabled = false });
                menu.Add(Item(Loc.T("shell.account.signin"), () => _vm.SignInCommand.Execute(null)));
            }
            menu.Add(new NativeMenuItemSeparator());
        }
        // Kopf: was gerade läuft. Darunter die Steuerung und der Sleep-Timer.
        var playing = _vm.PlayIcon == Ui.Icons.Pause;
        if (!_vm.IsIdle && _vm.Title is { Length: > 0 } title)
            menu.Add(new NativeMenuItem(_vm.Artist.Length > 0 ? $"{title} – {_vm.Artist}" : title) { IsEnabled = false });
        menu.Add(Item(Loc.T(playing ? "shell.tray.pause" : "shell.tray.play"), () => _vm.TogglePlayCommand.Execute(null)));
        if (_vm.CanNext) menu.Add(Item(Loc.T("shell.tray.next"), () => _vm.NextCommand.Execute(null)));
        if (_vm.CanPrevious) menu.Add(Item(Loc.T("shell.tray.previous"), () => _vm.PreviousCommand.Execute(null)));
        menu.Add(new NativeMenuItem(Loc.T("shell.settings.sleep")) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = _vm.SleepOn, Command = _vm.ToggleSleepCommand });
        menu.Add(new NativeMenuItemSeparator());
        foreach (var e in _host.TrayEntries) menu.Add(Item(e.Text, e.Invoke));
        if (_host.TrayEntries.Count > 0) menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(Loc.T("shell.tray.show"), () => _vm.ShowMain?.Invoke()));
        menu.Add(Item(Loc.T("shell.tray.widget"), () => _vm.OpenWidget?.Invoke()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(Loc.T("shell.nav.quit"), () => _vm.QuitApp?.Invoke()));
        _icon.Menu = menu;

        if (account != null && _vm.IsLoggedIn)
        {
            _icon.ToolTipText = $"Fledgr – {_vm.AccountName} ({_vm.AccountEmail})";
            _icon.Icon = account.Profile?.Avatar is { Length: > 0 } b ? AvatarIcon(b) ?? _defaultIcon : _defaultIcon;
        }
        else
        {
            _icon.ToolTipText = account != null ? "Fledgr – " + Loc.T("shell.tray.signedOut") : "Fledgr";
            _icon.Icon = _defaultIcon;
        }
        // Läuft etwas, steht es im Tooltip (kurz gehalten, Windows schneidet lange Texte ab).
        if (!_vm.IsIdle && _vm.Title is { Length: > 0 } t && playing)
        {
            var line = _vm.Artist.Length > 0 ? $"{t} – {_vm.Artist}" : t;
            _icon.ToolTipText = line.Length > 100 ? line[..99] + "…" : line;
        }
    }

    static NativeMenuItem Item(string text, Action act)
    {
        var i = new NativeMenuItem(text);
        i.Click += (_, _) => act();
        return i;
    }

    /// <summary>Schneidet das Profilbild rund zu und macht daraus ein Tray-Symbol (32 px).</summary>
    static WindowIcon? AvatarIcon(byte[] data)
    {
        try
        {
            using var src = CoverLoader.Decode(data, 64);
            if (src == null) return null;
            const int size = 32;
            var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            using (var ctx = target.CreateDrawingContext())
            using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(0, 0, size, size))))
            {
                ctx.DrawImage(src, new Rect(src.Size), new Rect(0, 0, size, size));
            }
            return new WindowIcon(target);
        }
        catch
        {
            return null;
        }
    }
}
