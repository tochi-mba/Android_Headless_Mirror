using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Quitting while the soft background is being captured. The capture runs on a worker thread, and
/// releasing its graphics device under it crashed the app on the way out, so the window is closed
/// again and again with captures as frequent as they go, and every exit must be a clean one.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private const int QuitCycles = 10;

    [Fact(Timeout = 600_000)]
    public async Task QuittingWhileTheBackgroundIsCapturedNeverCrashes()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Ambient.FrameRate = 60;
            c.Zoom.NavigatorAlways = true;
        });
        File.WriteAllText(package.AnimateMarker, string.Empty);

        for (var cycle = 0; cycle < QuitCycles; cycle++)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            using var app = new AppProcess(package);
            await app.WaitForPhaseAsync("mirroring", StartupTimeout);
            await app.WaitForStatusAsync(
                s => s["ambientFrame"]!.GetValue<bool>() && s["navigatorPicture"]!.GetValue<bool>(),
                StartupTimeout,
                "the soft background and the navigator picture");
            // QuitAsync fails on any exit other than a clean one, which a crash never is.
            await app.QuitAsync();
        }
    }
}
