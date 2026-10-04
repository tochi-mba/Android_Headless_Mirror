using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The side panel driven for real: the tiles, toggles and rows of each tab, through UI Automation
/// and real input against the fake phone.
/// </summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 75_000)]
    public async Task Controls_ThePauseTileBecomesResumeAndANewMirrorStartsLive()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        Assert.Equal("Pause", app.Ui.Read("tile-pause", e => e.Name));

        app.Ui.Invoke("tile-pause");
        await app.WaitForStatusAsync(s => s["paused"]!.GetValue<bool>(), Soon, "the picture to freeze");
        await app.WaitUntilAsync(() => app.Ui.Read("tile-pause", e => e.Name) == "Resume", Soon, "the tile to offer Resume");
        await app.SaveScreenshotAsync("ui-controls-paused.png");

        app.Ui.Invoke("tile-pause");
        await app.WaitForStatusAsync(s => !s["paused"]!.GetValue<bool>(), Soon, "the picture to run again");
        await app.WaitUntilAsync(() => app.Ui.Read("tile-pause", e => e.Name) == "Pause", Soon, "the tile to offer Pause again");

        // A frozen picture does not survive a restart: the new mirror is live, and the tile says so.
        app.Ui.Invoke("tile-pause");
        await app.WaitForStatusAsync(s => s["paused"]!.GetValue<bool>(), Soon, "the picture to freeze again");
        app.Ui.Invoke("RestartButton");
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(l => l.StartsWith("args ", StringComparison.Ordinal)) == 2, Startup, "the mirror to be relaunched");
        await app.WaitForStatusAsync(s => s["mirroring"]!.GetValue<bool>() && !s["paused"]!.GetValue<bool>(), Startup, "a live mirror");
        Assert.Equal("Pause", app.Ui.Read("tile-pause", e => e.Name));
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Controls_BrowseToggleAndOrientationAnswerAutomationAsWellAsClicks()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        // Turned on from the panel, the toggle shows it; turned off from the keyboard, it follows.
        app.Ui.Toggle("BrowseToggle", on: true);
        await app.WaitForStatusAsync(s => s["keyboard"]!["browse"]!.GetValue<bool>(), Soon, "browse mode from the toggle");
        await app.SaveScreenshotAsync("ui-controls-browse-on.png");
        await app.PressKeyAsync(0x1B); // Esc
        await app.WaitForStatusAsync(s => !s["keyboard"]!["browse"]!.GetValue<bool>(), Soon, "Esc to leave browse mode");
        await app.WaitUntilAsync(() => !app.Ui.IsOn("BrowseToggle"), Soon, "the toggle to follow the keyboard");

        // Choosing a segment the way Narrator does still turns the phone.
        app.Ui.Select("RotationLandscape");
        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(l => l.Contains("settings put system user_rotation 1", StringComparison.Ordinal)), Soon, "landscape to be written");

        // Rotating the phone asks Android, through scrcpy's own shortcut (right Ctrl + R).
        var keys = package.ScrcpyLog().Count(l => l.StartsWith("key vk=82 ", StringComparison.Ordinal));
        app.Ui.Invoke("RotateDevice");
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(l => l.StartsWith("key vk=82 ", StringComparison.Ordinal)) > keys, Soon, "the rotate shortcut to reach scrcpy");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Controls_WithNoPhoneEveryTileSaysWhyItIsOff()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { SetupDismissed = true });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("waiting", Startup);

        foreach (var id in new[] { "tile-home", "tile-pause", "tile-swipe-up", "RotationPortrait", "RotateDevice", "BrowseToggle", "KeyboardLayout", "ClipboardCopy" })
        {
            await app.WaitUntilAsync(() => !app.Ui.Read(id, e => e.IsEnabled), Soon, id + " to be off");
            Assert.Equal("Connect a phone to use these", app.Ui.Read(id, e => e.HelpText));
        }

        Assert.Equal("Waiting for phone…", app.Ui.Read("SessionButton", e => e.Name));
        Assert.False(app.Ui.Read("SessionButton", e => e.IsEnabled));
        await app.SaveScreenshotAsync("ui-controls-no-phone.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_ClickingARowsLabelFlipsItsSwitch()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        Assert.True(app.Ui.IsOn("AmbientEnabled"));

        var label = app.Ui.FindText("Soft background around the phone");
        // A collapsed group above Display can push this row just below a shorter desktop's
        // viewport. Scroll only when needed, then measure the real on-screen label: the point of
        // this test is the physical row click, not whether the default window happens to be tall.
        if (label.Current.IsOffscreen)
        {
            await app.ScrollSidebarAsync(-3);
            label = app.Ui.FindText("Soft background around the phone");
        }

        Assert.False(label.Current.IsOffscreen, "The setting label must be on screen before the physical click.");
        var (x, y) = app.Ui.Centre(label);
        await app.ClickAsync(x, y);
        await app.WaitUntilAsync(() => !ConfigFile.Load(package.Paths.Config).Ambient.Enabled, Soon, "the label click to turn the switch off");
        Assert.False(app.Ui.IsOn("AmbientEnabled"));

        await app.ClickAsync(x, y);
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Ambient.Enabled, Soon, "a second click to turn it back on");
        await app.QuitAsync();
    }
}
