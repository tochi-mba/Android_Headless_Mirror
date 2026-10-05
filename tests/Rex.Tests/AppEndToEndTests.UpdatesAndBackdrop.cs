using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The optional update check in the real app (offered once there is something newer, skipped for
/// good, never asked while off), the black backdrop, and the floating controls in the window.
/// </summary>
public sealed partial class AppEndToEndTests
{
    [Fact]
    public async Task UpdateCheck_OffersANewerVersionAndSkippingItKeepsItAway()
    {
        // Without the lock question, which takes the notice bar first whenever it is waiting.
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.App.CheckForUpdates = true;
            c.PatternGuide.AskPerDevice = false;
        });
        package.SetLatestRelease("v99.0.0");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        await app.WaitForStatusAsync(s => s["window"]!["updateOffered"]?.GetValue<string>() == "99.0.0", StartupTimeout, "the newer version to be offered");
        Assert.NotNull(new StateStore(package.Paths.State).Ui.LastUpdateCheck);

        await app.WaitUntilAsync(() => app.Ui.Exists("NoticeUpdateSkip"), StartupTimeout, "the offer in the notice bar");
        app.Ui.Invoke("NoticeUpdateNotes");
        await app.WaitUntilAsync(() => package.OpenedPages().Any(p => p.Contains("#v99-0-0", StringComparison.Ordinal)), TimeSpan.FromSeconds(10), "what is new to open");
        app.Ui.Invoke("NoticeUpdateSkip");
        await app.WaitForStatusAsync(s => s["window"]!["updateOffered"] is null, TimeSpan.FromSeconds(10), "the offer to go");
        Assert.Equal("99.0.0", new StateStore(package.Paths.State).Ui.SkippedVersion);
        await app.QuitAsync();
    }

    [Fact]
    public async Task UpdateCheck_NeverAsksWhileItIsOff()
    {
        using var package = new TestPackage(withFakeTools: true);
        package.SetLatestRelease("v99.0.0");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var status = (await app.SendAsync(new IpcRequest("status"))).Data!;
        Assert.Null(status["window"]!["updateOffered"]);
        Assert.Null(new StateStore(package.Paths.State).Ui.LastUpdateCheck);
        Assert.DoesNotContain("Asked GitHub", app.ReadLog(), StringComparison.Ordinal);
        await app.QuitAsync();
    }

    [Fact]
    public async Task Backdrop_BlackPaintsTheMirrorAreaAndScrcpysEdges()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Mirror.Backdrop = "black");
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.Equal("black", status["window"]!["backdrop"]!.GetValue<string>());
        Assert.Contains("--background-color=#000000", Launches(package)[0], StringComparison.Ordinal);
        await app.QuitAsync();
    }

    [Fact]
    public async Task FloatingControls_StayUpInTheWindowWhenAsked()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Hud.ShowInWindow = true;
            c.Hud.AutoHide = false;
            c.Hud.HideSeconds = 1;
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => !s["fullscreen"]!.GetValue<bool>() && s["hudVisible"]!.GetValue<bool>(), StartupTimeout, "the controls in the window");

        // Past the time they would have hidden after: still there.
        await Task.Delay(TimeSpan.FromSeconds(2.5), TestContext.Current.CancellationToken);
        Assert.True((await app.SendAsync(new IpcRequest("status"))).Data!["hudVisible"]!.GetValue<bool>());
        await app.QuitAsync();
    }
}
