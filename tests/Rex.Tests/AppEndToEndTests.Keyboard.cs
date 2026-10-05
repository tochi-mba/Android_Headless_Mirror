using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The keyboard end of the desktop tests: which keyboard scrcpy is given, what a held Alt does,
/// browse mode, and the Ctrl+Alt gestures. Part of <see cref="AppEndToEndTests"/>, so it runs in the
/// same desktop job with the same fakes.
/// </summary>
public sealed partial class AppEndToEndTests
{
    [Fact]
    public async Task KeyboardMode_FallsBackOnceWhenAnOldPhoneDeniesUhid()
    {
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.DenyUhidMarker, string.Empty);
        using var app = new AppProcess(package);

        var status = await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var launches = package.ScrcpyLog().Where(line => line.StartsWith("args ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, launches.Length);
        Assert.Contains("--keyboard=uhid", launches[0], StringComparison.Ordinal);
        Assert.Contains("--keyboard=sdk", launches[1], StringComparison.Ordinal);
        Assert.Contains("--raw-key-events", launches[1], StringComparison.Ordinal);
        Assert.Equal("sdk", status["keyboard"]!["mode"]!.GetValue<string>());
        await app.QuitAsync();

        // The refusal is remembered, so the next session starts in compatibility mode without
        // failing first.
        Assert.True(new StateStore(package.Paths.State).GetDevice("FAKE123")!.CompatibilityKeyboard);
        using var second = new AppProcess(package);
        await second.WaitForPhaseAsync("mirroring", StartupTimeout);
        launches = package.ScrcpyLog().Where(line => line.StartsWith("args ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, launches.Length);
        Assert.Contains("--keyboard=sdk", launches[2], StringComparison.Ordinal);
        await second.QuitAsync();
    }

    [Fact]
    public async Task HoldingAltForThePcView_NeverReachesThePhone()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // A plain key reaches the phone, so the mirror has the keyboard to begin with.
        await app.PressKeyAsync(0x41);
        await app.WaitUntilAsync(() => package.ScrcpyLog().Any(line => line.StartsWith("key vk=65", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5), "a letter to reach the phone");

        // Held on its own, left Alt is the PC's: Alt + wheel zooms and the phone never hears Alt,
        // which is what made Android show its keyboard shortcut list in the middle of a pinch.
        await app.AltWheelOverMirrorAsync(2, whileHeld: () =>
            app.WaitForStatusAsync(s => s["keyboard"]!["altHeldForPc"]!.GetValue<bool>(), TimeSpan.FromSeconds(5), "Alt to be held for the PC"));
        await app.WaitForStatusAsync(s => s["zoom"]!.GetValue<double>() > 1 && !s["keyboard"]!["altHeldForPc"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5), "Alt + wheel to zoom and the hold to end");
        Assert.DoesNotContain(package.ScrcpyLog(), line => line.StartsWith("key vk=18 ", StringComparison.Ordinal));

        // Once Alt is up the keyboard is the phone's again.
        var letters = package.ScrcpyLog().Count(line => line.StartsWith("key vk=66", StringComparison.Ordinal));
        await app.PressKeyAsync(0x42);
        await app.WaitUntilAsync(() => package.ScrcpyLog().Count(line => line.StartsWith("key vk=66", StringComparison.Ordinal)) > letters,
            TimeSpan.FromSeconds(5), "typing to reach the phone again after Alt");
        await app.QuitAsync();
    }

    [Fact]
    public async Task BrowseMode_TurnsPlainKeysIntoGesturesUntilEscape()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.Equal("uhid", status["keyboard"]!["mode"]!.GetValue<string>());
        Assert.False(status["keyboard"]!["browse"]!.GetValue<bool>());

        await app.ActionAsync("browse");
        await app.WaitForStatusAsync(data => data["keyboard"]!["browse"]!.GetValue<bool>(), TimeSpan.FromSeconds(5), "browse mode to switch on");

        await app.PressKeyAsync(0x28); // Down: next item, as a swipe up.
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(line => line.StartsWith("pointerup", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "a plain arrow key to swipe the phone");
        Assert.DoesNotContain(package.ScrcpyLog(), line => line.StartsWith("key vk=40", StringComparison.Ordinal));

        await app.PressKeyAsync(0x4D); // M: mute, over ADB.
        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(line => line.EndsWith("input keyevent KEYCODE_VOLUME_MUTE", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "M to mute the phone");

        await app.PressKeyAsync(0x1B); // Esc leaves the mode.
        await app.WaitForStatusAsync(data => !data["keyboard"]!["browse"]!.GetValue<bool>(), TimeSpan.FromSeconds(5), "browse mode to switch off");
        var touches = package.ScrcpyLog().Count(line => line.StartsWith("pointerdown", StringComparison.Ordinal));

        // The same key now types into the phone instead.
        await app.PressKeyAsync(0x28);
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(line => line.StartsWith("key vk=40", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "the arrow key to reach the phone as a key again");
        Assert.Equal(touches, package.ScrcpyLog().Count(line => line.StartsWith("pointerdown", StringComparison.Ordinal)));

        // Gestures also work from the command line, and from the tray with no picture to touch.
        await app.ActionAsync("swipe-left");
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(line => line.StartsWith("pointerdown", StringComparison.Ordinal)) > touches,
            TimeSpan.FromSeconds(5),
            "a command-line gesture to touch the phone");
        await app.SendAsync(new IpcRequest("hide", new Dictionary<string, string>()));
        await app.ActionAsync("like");
        await app.WaitUntilAsync(
            () => package.AdbCalls().Count(line => line.EndsWith("input tap 540 1200", StringComparison.Ordinal)) >= 2,
            TimeSpan.FromSeconds(5),
            "a hidden window to play the gesture through Android instead");
        await app.QuitAsync();
    }

    [Fact]
    public async Task KeyboardOnlyFeedControls_SendSwipesTapsAndPhoneNavigation()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        // Real keys go to whatever window is in front.
        await app.FocusAsync();

        await app.PressCtrlAltKeyAsync(0x28); // Down: next feed item.
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(line => line.StartsWith("pointerup", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "keyboard swipe to finish");
        var firstTouches = package.ScrcpyLog().Count(line => line.StartsWith("pointerdown", StringComparison.Ordinal));

        await app.PressCtrlAltKeyAsync(0x0D); // Enter: tap the centre.
        // A touch Windows refuses to inject (error 87 on a busy runner) is played by Android itself
        // over ADB instead; either way the tap reaches the phone. The fallback waits for the refusal
        // first, which a busy runner can take seconds to give.
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(line => line.StartsWith("pointerdown", StringComparison.Ordinal)) > firstTouches ||
                  package.AdbCalls().Any(line => line.EndsWith("input tap 540 1200", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(15),
            "keyboard tap to reach the phone");
        await app.PressCtrlAltKeyAsync(0x08); // Backspace: Android back.
        await app.PressCtrlAltKeyAsync((byte)'R'); // Recent apps.

        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(line => line.EndsWith("input keyevent KEYCODE_BACK", StringComparison.Ordinal)) &&
                  package.AdbCalls().Any(line => line.EndsWith("input keyevent KEYCODE_APP_SWITCH", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "keyboard phone navigation");
        await app.QuitAsync();
    }
}
