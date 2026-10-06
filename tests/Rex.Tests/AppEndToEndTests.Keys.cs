using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Your own keys in the real app, with real key presses: a moved key does its action and the key
/// it had does nothing, and browse mode plays with a key of one's own.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static int HomePresses(TestPackage package) =>
        package.AdbCalls().Count(line => line.EndsWith("input keyevent KEYCODE_HOME", StringComparison.Ordinal));

    [Fact]
    public async Task Keys_AMovedKeyWorksAndTheOldOneDoesNot()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
            c.Keys.Window = [new KeyBinding { Action = "home", Key = "Ctrl+Alt+J" }]);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.FocusAsync();

        await app.PressCtrlAltKeyAsync((byte)'J');
        await app.WaitUntilAsync(() => HomePresses(package) == 1, TimeSpan.FromSeconds(10), "the new key to go home");

        // The key home had is free now: it reaches the phone as a key, not as Home.
        await app.PressCtrlAltKeyAsync((byte)'H');
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(1, HomePresses(package));
        await app.QuitAsync();
    }

    [Fact]
    public async Task Keys_BrowseModePlaysWithAKeyOfOnesOwn()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c =>
            c.Keys.Browse = [new KeyBinding { Action = "back", Key = "J" }]);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.FocusAsync();
        await app.ActionAsync("browse");
        await app.WaitForStatusAsync(s => s["keyboard"]!["browse"]!.GetValue<bool>(), TimeSpan.FromSeconds(10), "browse mode");

        await app.PressChordAsync((byte)'J');
        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(line => line.EndsWith("input keyevent KEYCODE_BACK", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10),
            "J to go back in browse mode");
        await app.QuitAsync();
    }
}
