using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using YTSPlayer.Contracts.Account;
using YTSPlayer.Contracts.Plugins;

namespace YTSPlayer.Desktop.SelfTests;

/// <summary>Nur für --selftest-ui: täuscht ein angemeldetes Konto vor, damit man Kopfzeile und Avatar sehen kann.</summary>
sealed class FakeAccountPlugin : IPlugin
{
    sealed class Session : IAccountSession
    {
        public string DisplayName => "Test";
        public AccountState State { get; private set; } = AccountState.LoggedIn;
        public AccountProfile? Profile { get; private set; } = new("Max Muster", "max.muster@example.com", MakeAvatar());
        public event Action? Changed;

        public Task SignInAsync() => Task.CompletedTask;

        public Task SignOutAsync()
        {
            State = AccountState.LoggedOut;
            Profile = null;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    public string Id => "fake-account";
    public string DisplayName => "Test-Konto";
    public bool IsCore => false;
    public bool EnabledByDefault => true;

    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        context.SetAccount(new Session());
        return Task.CompletedTask;
    }

    public Task DeactivateAsync() => Task.CompletedTask;

    static byte[] MakeAvatar()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawRectangle(Brushes.OrangeRed, null, new Rect(0, 0, 64, 64));
            ctx.DrawEllipse(Brushes.Gold, null, new Point(32, 32), 18, 18);
        }
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}
