using System.Text.RegularExpressions;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The window and captures your way, in the real app: screenshots named as asked and never
/// overwritten, flashed and opened; a word from the tray when the mirror stops by itself; asking
/// before quitting; and fitting the window to the phone.
/// </summary>
public sealed partial class AppEndToEndTests
{
    [Fact]
    public async Task Screenshots_AreNamedAsAskedFlashedAndOpened()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.App.CaptureNames = "{model}-{n}";
            c.App.ScreenshotFlash = true;
            c.App.OpenScreenshots = true;
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        var first = await app.SendAsync(new IpcRequest("screenshot"));
        var second = await app.SendAsync(new IpcRequest("screenshot"));
        Assert.True(first.Ok && second.Ok, first.Error ?? second.Error);
        var names = new[] { first, second }.Select(r => Path.GetFileName(r.Data!["path"]!.GetValue<string>())).ToArray();
        Assert.Matches(new Regex(@"^.+-1\.png$"), names[0]);
        Assert.Equal(names[0].Replace("-1.png", "-2.png", StringComparison.Ordinal), names[1]);

        var flashed = await app.WaitForStatusAsync(s => s["window"]!["lastFlash"] is not null, TimeSpan.FromSeconds(5), "the view to flash");
        Assert.NotNull(flashed["window"]!["lastFlash"]);
        await app.WaitUntilAsync(() => package.OpenedPages().Count(p => p.StartsWith("file:", StringComparison.Ordinal)) == 2, TimeSpan.FromSeconds(5), "both screenshots to open");
        Assert.EndsWith(names[1], package.OpenedPages().Last(), StringComparison.Ordinal);
        await app.QuitAsync();
    }

    [Fact]
    public async Task NotifyMirrorStops_SaysSoFromTheTrayWhenTheMirrorStopsByItself()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.App.NotifyMirrorStops = true;
            c.Session.RestartOnUnexpectedExit = false;
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        app.CloseWindow();
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>(), StartupTimeout, "the window to go to the tray");

        await app.KillMirrorAsync();
        await app.WaitForStatusAsync(
            s => s["window"]!["lastNotification"]!.GetValue<string>().StartsWith("The mirror stopped: ", StringComparison.Ordinal),
            StartupTimeout,
            "word that the mirror stopped");
        await app.QuitAsync();
    }

    [Fact]
    public async Task ConfirmQuit_AsksBeforeQuittingWhileAPhoneIsMirrored()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.App.RunInBackground = false;
            c.App.ConfirmQuit = true;
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // Closing the window asks first; Cancel keeps everything as it was.
        app.CloseWindow();
        await app.WaitUntilAsync(() => app.Ui.Exists("ConfirmAccept") && app.Ui.Read("ConfirmAccept", e => e.Name) == "Quit", StartupTimeout, "the question");
        app.Ui.Invoke("ConfirmCancel");
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // The command line's quit never asks: there is nobody to answer. It exits at once.
        await app.QuitAsync();
    }

    [Fact]
    public async Task FitWindow_MakesTheMirrorAreaThePhonesShape()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var before = await app.WaitForStatusAsync(s => s["copies"]!["views"]!.AsArray().Count > 0, StartupTimeout, "the phone's view");
        static double Shape(System.Text.Json.Nodes.JsonNode view) => view["width"]!.GetValue<double>() / view["height"]!.GetValue<double>();
        var was = Shape(before["copies"]!["views"]![0]!);

        var fitted = await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "fit-window" }));
        Assert.True(fitted.Ok, fitted.Error);
        Assert.StartsWith("The window fits the phone", fitted.Data!["text"]!.GetValue<string>(), StringComparison.Ordinal);

        // The phone's view takes the phone's shape as nearly as the screen allows (a test runner's
        // screen is small, so the window may meet its edge before the shape is exact).
        const double phone = 1080.0 / 2400;
        var status = await app.WaitForStatusAsync(s => Math.Abs(Shape(s["copies"]!["views"]![0]!) - was) > 0.01, StartupTimeout, "the view after fitting");
        var now = Shape(status["copies"]!["views"]![0]!);
        Assert.True(Math.Abs(now - phone) < Math.Abs(was - phone), $"The view went from {was:0.000} to {now:0.000}, not nearer the phone's {phone:0.000}.");
        await app.QuitAsync();
    }
}
