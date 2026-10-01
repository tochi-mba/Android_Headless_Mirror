using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The camera for the website: takes the real window, showing a made-up home screen rather than any
/// real phone's, in the states the site's pages describe, and saves each picture to
/// artifacts/screens/site. CI runs it on every pull request so the pictures can be copied into
/// docs/img when a page needs a new one; locally it only runs when REX_SITE_SHOTS=1.
/// </summary>
[Collection("desktop")]
public sealed class SiteScreenshots
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private static bool Enabled => Environment.GetEnvironmentVariable("REX_SITE_SHOTS") == "1";

    /// <summary>Every picture this class promises, so a missing one fails the run.</summary>
    public static readonly string[] Pictures =
        ["window.png", "controls.png", "settings.png", "fullscreen.png", "copies.png", "zoomed.png"];

    [Fact(Timeout = 300_000)]
    public async Task EveryPictureTheSiteUses()
    {
        Assert.SkipUnless(Enabled, "Set REX_SITE_SHOTS=1 to take the website's pictures.");
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.HomeScreenMarker, string.Empty);
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { TipsSeen = [.. Tips.All.Select(t => t.Id)], TourSeenVersion = 99 });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        var folder = Path.Combine(RepoPaths.Screens, "site");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.png"))
        {
            File.Delete(old);
        }

        var monitor = app.MonitorBounds();
        var scale = app.DpiScale();
        app.MoveWindow(monitor.Left + 20, monitor.Top + 20,
            Math.Min((int)(1180 * scale), monitor.Width - 40), Math.Min((int)(780 * scale), monitor.Height - 40));
        // Time for the new size to reach the embedded picture is itself what is being waited for.
        await Task.Delay(800, TestContext.Current.CancellationToken);

        async Task Take(string name)
        {
            using var bitmap = await app.CaptureWindowAsync();
            bitmap.Save(Path.Combine(folder, name), System.Drawing.Imaging.ImageFormat.Png);
        }

        app.Ui.Select("TabInfo");
        await Take("window.png");
        app.Ui.Select("TabControls");
        await Take("controls.png");
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupDisplay");
        await Take("settings.png");

        app.Ui.Select("TabControls");
        await app.ActionAsync("copy-add");
        await app.WaitForStatusAsync(s => s["copies"]!["running"]!.GetValue<int>() == 1, Startup, "a copy to open");
        await Take("copies.png");
        await app.ActionAsync("copy-remove");
        await app.WaitForStatusAsync(s => s["copies"]!["running"]!.GetValue<int>() == 0, Startup, "the copy to close");

        await app.ActionAsync("zoom-in");
        await app.ActionAsync("zoom-in");
        await app.WaitForStatusAsync(s => s["zoom"]!.GetValue<double>() > 1.2, Soon, "the view to zoom");
        await Take("zoomed.png");
        await app.ActionAsync("zoom-reset");

        await app.ActionAsync("fullscreen");
        await app.WaitForStatusAsync(s => s["fullscreen"]!.GetValue<bool>(), Soon, "fullscreen");
        await Take("fullscreen.png");
        await app.ActionAsync("fullscreen");

        Assert.Equal(Pictures.Order(StringComparer.Ordinal), Directory.GetFiles(folder, "*.png").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        await app.QuitAsync();
    }
}
