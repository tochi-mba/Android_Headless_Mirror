using System.Runtime.InteropServices;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The window preferences that reach outside the window: a screenshot saved as a JPG and put on
/// the clipboard, and a word from the tray when the phone comes or goes while the window is away.
/// </summary>
public sealed partial class AppEndToEndTests
{
    [Fact]
    public async Task Screenshots_AreSavedAsJpgAndCopiedWhenAsked()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.App.ScreenshotFormat = "jpg";
            c.App.CopyScreenshots = true;
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        OnSta(() =>
        {
            System.Windows.Clipboard.Clear();
            return true;
        });

        var shot = await app.SendAsync(new IpcRequest("screenshot"));
        Assert.True(shot.Ok, shot.Error);
        var path = shot.Data!["path"]!.GetValue<string>();
        Assert.EndsWith(".jpg", path, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, File.ReadAllBytes(path)[..2]);
        await app.WaitUntilAsync(() => OnSta(System.Windows.Clipboard.ContainsImage), TimeSpan.FromSeconds(5), "the screenshot on the clipboard");
        await app.QuitAsync();
    }

    [Fact]
    public async Task NotifyConnections_SaysSoFromTheTrayWhileTheWindowIsAway()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.App.NotifyConnections = true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        app.CloseWindow();
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>(), StartupTimeout, "the window to go to the tray");

        package.WriteScenario(new { Devices = Array.Empty<object>() });
        await app.WaitForStatusAsync(
            s => s["window"]!["lastNotification"]!.GetValue<string>().StartsWith("Phone disconnected", StringComparison.Ordinal),
            StartupTimeout,
            "word that the phone left");
        await app.KillMirrorAsync();

        File.Delete(package.FakeAdbScenario);
        await app.WaitForStatusAsync(
            s => s["window"]!["lastNotification"]!.GetValue<string>().StartsWith("Phone connected", StringComparison.Ordinal),
            StartupTimeout,
            "word that the phone is back");
        await app.QuitAsync();
    }

    /// <summary>The clipboard belongs to single-threaded apartments; it is also busy now and then, so this asks again.</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    result = work();
                    error = null;
                    return;
                }
                catch (ExternalException ex) when (attempt < 10)
                {
                    error = ex;
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    error = ex;
                    return;
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }

        return result;
    }
}
