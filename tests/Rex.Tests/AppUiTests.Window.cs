using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The window and behaviour settings through the Settings tab, each applying at once: staying on
/// top, the panel on the left, the top bar's buttons, the hints, the live frame rate, and how zoom
/// answers the wheel and a phone that turns.
/// </summary>
public sealed partial class AppUiTests
{
    private static JsonNode Window(JsonObject status) => status["window"]!;

    [Fact(Timeout = 75_000)]
    public async Task Settings_WindowPreferencesApplyAtOnce()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupStartup");

        app.Ui.Toggle("AlwaysOnTop", on: true);
        await app.WaitForStatusAsync(s => Window(s)["topmost"]!.GetValue<bool>(), Soon, "the window to stay on top");

        // The side panel moves to the other side of the mirror.
        app.Ui.SelectComboItem("SidebarSide", "On the left");
        var left = await app.WaitForStatusAsync(s => Window(s)["sidebarSide"]!.GetValue<string>() == "left", Soon, "the panel to move left");
        var tabs = app.Ui.Find("TabControls").Current.BoundingRectangle;
        var picture = left["copies"]!["views"]![0]!;
        Assert.True(tabs.Right <= picture["x"]!.GetValue<int>(), $"The side panel ({tabs}) is left of the mirror ({picture.ToJsonString()}).");
        await app.SaveScreenshotAsync("ui-sidebar-left.png");

        // Only the chosen phone buttons stay in the top bar.
        app.Ui.Toggle("quick-button home", on: false);
        app.Ui.Toggle("quick-button screenshot", on: false);
        await app.WaitForStatusAsync(
            s => Window(s)["quickButtons"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(["back", "recents", "sleep"]),
            Soon,
            "the top bar to keep only the chosen buttons");
        Assert.False(app.Ui.Exists("QuickHome"));
        Assert.True(app.Ui.Exists("QuickBack"));

        app.Ui.Toggle("ShowHints", on: false);
        await app.WaitForStatusAsync(s => !Window(s)["hints"]!.GetValue<bool>(), Soon, "the hints to go");

        // The frame rate comes and goes in the running session, without a restart.
        app.Ui.Toggle("ShowFrameRate", on: true);
        await app.WaitForStatusAsync(s => Window(s)["frameRate"]?.GetValue<int>() == 60, Soon, "the frame rate to show");
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["restartRequired"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("ui-frame-rate.png");
        app.Ui.Toggle("ShowFrameRate", on: false);
        await app.WaitForStatusAsync(s => Window(s)["frameRate"] is null, Soon, "the frame rate to go");
        await app.QuitAsync();

        var config = ConfigFile.Load(package.Paths.Config);
        Assert.True(config.App.AlwaysOnTop);
        Assert.Equal("left", config.App.SidebarSide);
        Assert.Equal(new[] { "back", "recents", "sleep" }, config.App.TopBarButtons);
        Assert.False(config.App.ShowHints);
        Assert.False(config.App.ShowFrameRate);
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_SwipeLengthAndSpeedSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupInput");
        app.Ui.SetValue("SwipeLength", 1.3);
        app.Ui.SetValue("SwipeMilliseconds", 350);
        await app.WaitUntilAsync(
            () => ConfigFile.Load(package.Paths.Config).Input is { SwipeLength: 1.3, SwipeMilliseconds: 350 },
            Soon,
            "the swipe settings saved");
        Assert.Equal("130% of the usual", app.Ui.Read("SwipeLengthValue", e => e.Name));
        Assert.Equal("350 ms", app.Ui.Read("SwipeTimeValue", e => e.Name));
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Zoom_FollowsTheWheelDirectionTheAnchorAndAPhoneThatTurns()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Zoom.InvertWheel = true;
            c.Zoom.ZoomAtPointer = false;
            c.Zoom.ResetOnRotate = true;
        });
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", Startup);
        var view = status["copies"]!["views"]![0]!;

        // Towards you zooms in with the wheel inverted, and near a corner it still zooms on the middle.
        await app.AltWheelAtAsync(view["x"]!.GetValue<int>() + 24, view["y"]!.GetValue<int>() + 24, -3);
        var zoomed = await app.WaitForStatusAsync(s => s["zoom"]!.GetValue<double>() > 1, Soon, "the inverted wheel to zoom in");
        var surface = zoomed["surface"]!;
        var centred = (surface["viewportWidth"]!.GetValue<double>() - surface["width"]!.GetValue<double>()) / 2;
        Assert.InRange(surface["x"]!.GetValue<double>(), centred - 2, centred + 2);

        // Turning the phone zooms back out to the whole picture.
        await app.ActionAsync("rotation-landscape");
        await app.WaitForStatusAsync(
            s => s["zoom"]!.GetValue<double>() == 1 && s["surface"]!["width"]!.GetValue<double>() > s["surface"]!["height"]!.GetValue<double>(),
            Startup,
            "the picture to turn and zoom back out");
        await app.QuitAsync();
    }
}
