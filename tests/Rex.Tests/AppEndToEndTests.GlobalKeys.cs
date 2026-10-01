using System.Text.RegularExpressions;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Keys from anywhere with real input: the window brought back from behind another one and from the
/// tray, hidden again, opened straight to fullscreen, an action played while the window is away, and
/// the keys that must still reach the app in front (AltGr, and everything when the switch is off).
/// </summary>
public sealed partial class AppEndToEndTests
{
    private const byte KeyCtrl = 0xA2, KeyAlt = 0xA4, KeyShift = 0xA0, KeyM = (byte)'M', KeyF10 = 0x79;
    private static readonly TimeSpan KeyTimeout = TimeSpan.FromSeconds(10);

    private static bool Shown(System.Text.Json.Nodes.JsonObject s) =>
        s["windowVisible"]!.GetValue<bool>() && s["window"]!["foreground"]!.GetValue<bool>();

    private static int ShowHideCount(AppProcess app) => Regex.Matches(app.ReadLog(), "Show or hide key: ").Count;

    [Fact(Timeout = 180_000)]
    public async Task ShowHideKey_BringsTheWindowBackFromAnywhereAndHidesItAgain()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => s["globalKeys"]!["hookInstalled"]!.GetValue<bool>(), StartupTimeout, "the keyboard hook");

        // Behind another window: the key brings it to the front.
        await app.PutAnotherWindowInFrontAsync();
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        var front = await app.WaitForStatusAsync(Shown, KeyTimeout, "the window in front");
        Assert.NotEqual("refused", front["globalKeys"]!["foregroundStep"]!.GetValue<string>());
        Assert.DoesNotContain((int)KeyM, app.KeysReachingTheWindowInFront);

        // In front: the key hides it in the tray.
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>(), KeyTimeout, "the window to hide");

        // Hidden: the key shows it again, in front. A held key acts once, however long it repeats.
        var before = ShowHideCount(app);
        await app.HoldChordAsync(4, KeyCtrl, KeyAlt, KeyM);
        await app.WaitForStatusAsync(Shown, KeyTimeout, "the window back in front");
        await app.WaitUntilAsync(() => ShowHideCount(app) > before, KeyTimeout, "the key to be logged");
        Assert.Equal(before + 1, ShowHideCount(app));
        await app.SaveScreenshotAsync("global-key-shown.png");

        // While a shortcut box records, the key is the box's: the window stays where it is.
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupGlobalKeys");
        app.Ui.Focus("GlobalShowHide");
        await app.WaitForStatusAsync(s => s["globalKeys"]!["recording"]!.GetValue<bool>(), KeyTimeout, "the box to record");
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        Assert.True(Shown((await app.SendAsync(new IpcRequest("status"))).Data!.AsObject()));
        Assert.Equal("Ctrl+Alt+M", app.Ui.TextOf("GlobalShowHide"));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task ShowHideKey_WorksWhenTheAppStartedInTheTray()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.App.OpenOnConnect = false;
            c.GlobalKeys.ShowFullscreen = true;
        });
        using var app = new AppProcess(package, background: true);
        await app.WaitForStatusAsync(s => s["globalKeys"]!["hookInstalled"]!.GetValue<bool>(), StartupTimeout, "the keyboard hook, with no window shown");
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["windowVisible"]!.GetValue<bool>());

        await app.PutAnotherWindowInFrontAsync();
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        await app.WaitForStatusAsync(s => Shown(s) && s["fullscreen"]!.GetValue<bool>(), KeyTimeout, "the window, straight to fullscreen");

        // Hidden from fullscreen, it leaves fullscreen first.
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>() && !s["fullscreen"]!.GetValue<bool>(), KeyTimeout, "the window to hide, windowed");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task KeysFromAnywhere_LeaveAltGrAndTheSwitchedOffKeysToTheAppInFront()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => s["globalKeys"]!["hookInstalled"]!.GetValue<bool>(), StartupTimeout, "the keyboard hook");
        await app.PutAnotherWindowInFrontAsync();

        // AltGr+M types on many layouts; it is never taken.
        await app.PressAltGrAsync(KeyM);
        await app.WaitUntilAsync(() => app.KeysReachingTheWindowInFront.Contains(KeyM), KeyTimeout, "AltGr+M to reach the window in front");
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["window"]!["foreground"]!.GetValue<bool>());
        Assert.Equal(0, ShowHideCount(app));

        // With the switch off, the key itself reaches the window in front.
        new ConfigStore(package.Paths.Config).Set("GlobalKeys.Enabled", "false");
        await app.WaitForStatusAsync(s => !s["globalKeys"]!["enabled"]!.GetValue<bool>(), KeyTimeout, "keys from anywhere to be off");
        var seen = app.KeysReachingTheWindowInFront.Count(k => k == KeyM);
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        await app.WaitUntilAsync(() => app.KeysReachingTheWindowInFront.Count(k => k == KeyM) > seen, KeyTimeout, "Ctrl+Alt+M to reach the window in front");
        Assert.Equal(0, ShowHideCount(app));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task AKeyFromAnywhere_TakesAScreenshotWhileTheWindowIsHidden()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.GlobalKeys.Announce = true;
            c.GlobalKeys.Actions = [new GlobalKeyAction { Key = "Ctrl+Shift+F10", Action = "screenshot" }];
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.FocusAsync();
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyM);
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>(), KeyTimeout, "the window to hide");

        await app.PressChordAsync(KeyCtrl, KeyShift, KeyF10);
        var said = await app.WaitForStatusAsync(
            s => s["window"]!["lastNotification"]!.GetValue<string>().StartsWith("Screenshot: Screenshot saved", StringComparison.Ordinal),
            KeyTimeout,
            "the tray to say the screenshot was saved");
        Assert.Contains(package.AdbCalls(), line => line.Contains("screencap", StringComparison.Ordinal));
        Assert.False(said["windowVisible"]!.GetValue<bool>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task TabKeysNeedExactlyCtrlAlt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        app.Ui.Select("TabSettings");
        await app.FocusAsync();

        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyShift, (byte)'1');
        await app.PressChordAsync(KeyCtrl, KeyAlt, 0x70);
        var status = (await app.SendAsync(new IpcRequest("status"))).Data!;
        Assert.Equal("settings", status["sidebarTab"]!.GetValue<string>());
        Assert.False(status["tour"]!["visible"]!.GetValue<bool>());

        // The key with exactly its own modifiers still works.
        await app.PressChordAsync(KeyCtrl, KeyAlt, (byte)'1');
        await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == "controls", KeyTimeout, "the Controls tab");
        await app.QuitAsync();
    }
}
