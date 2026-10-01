using System.Runtime.InteropServices;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Shortcuts from anywhere through the Settings tab, with real keys pressed into the boxes: a key
/// recorded and saved, turned off and put back, refused when the window already uses it, warned
/// about when another app has claimed it, and actions with keys of their own added and removed.
/// </summary>
public sealed partial class AppUiTests
{
    private const byte Ctrl = 0xA2, Shift = 0xA0, Alt = 0xA4, F8 = 0x77, F9 = 0x78, Backspace = 0x08, Esc = 0x1B;

    private static string ShowHideSaved(TestPackage package) => ConfigFile.Load(package.Paths.Config).GlobalKeys.ShowHide;

    private static async Task<AppProcess> OpenGlobalKeysAsync(TestPackage package)
    {
        var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupGlobalKeys");
        return app;
    }

    [Fact(Timeout = 120_000)]
    public async Task GlobalKeys_RecordAChordAndItSaves()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenGlobalKeysAsync(package);
        await app.FocusAsync();

        app.Ui.Focus("GlobalShowHide");
        await app.WaitForStatusAsync(s => s["globalKeys"]!["recording"]!.GetValue<bool>(), Soon, "the box to record");
        await app.PressChordAsync(Ctrl, Shift, F9);
        await app.WaitUntilAsync(() => ShowHideSaved(package) == "Ctrl+Shift+F9", Soon, "the new key to be saved");
        Assert.Equal("Ctrl+Shift+F9", app.Ui.TextOf("GlobalShowHide"));
        Assert.Equal("Works from anywhere.", app.Ui.Read("GlobalShowHideNote", i => i.Name));
        await app.SaveScreenshotAsync("ui-global-keys.png");

        // Esc puts back what was there; Backspace turns the key off.
        await app.PressChordAsync(Ctrl, Alt);
        await app.PressChordAsync(Esc);
        Assert.Equal("Ctrl+Shift+F9", app.Ui.TextOf("GlobalShowHide"));
        await app.PressChordAsync(Backspace);
        await app.WaitUntilAsync(() => ShowHideSaved(package).Length == 0, Soon, "the key to be turned off");
        Assert.Equal("Press the keys", app.Ui.TextOf("GlobalShowHide"));
        var status = await app.WaitForStatusAsync(s => s["globalKeys"]!["showHide"]!.GetValue<string>().Length == 0, Soon, "the window to drop the key");
        Assert.True(status["globalKeys"]!["recording"]!.GetValue<bool>());

        // A key the window uses for something else is refused, and nothing is saved.
        await app.PressChordAsync(Ctrl, Alt, (byte)'S');
        await app.WaitUntilAsync(
            () => app.Ui.Read("GlobalShowHideNote", i => i.Name) == "In this window the key already means: Save a screenshot.",
            Soon,
            "the refusal");
        Assert.Equal(string.Empty, ShowHideSaved(package));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task GlobalKeys_WarnsWhenAnotherAppHasTheKey()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenGlobalKeysAsync(package);
        using var claim = new HotKeyClaim(0x0002 | 0x0004, F8);
        await app.FocusAsync();

        app.Ui.Focus("GlobalShowHide");
        await app.PressChordAsync(Ctrl, Shift, F8);

        await app.WaitUntilAsync(() => ShowHideSaved(package) == "Ctrl+Shift+F8", Soon, "the key to be saved anyway");
        Assert.Contains("Another app uses this key too.", app.Ui.Read("GlobalShowHideNote", i => i.Name), StringComparison.Ordinal);
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task GlobalKeys_AddAndRemoveAShortcutForAnAction()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenGlobalKeysAsync(package);
        await app.FocusAsync();

        app.Ui.SelectComboItem("GlobalNewAction", "Screenshot");
        app.Ui.Invoke("GlobalAddAction");
        // The new box takes the keyboard by itself.
        await app.WaitForStatusAsync(s => s["globalKeys"]!["recording"]!.GetValue<bool>(), Soon, "the new box to record");
        await app.PressChordAsync(Ctrl, Shift, F9);
        await app.WaitUntilAsync(
            () => ConfigFile.Load(package.Paths.Config).GlobalKeys.Actions is [{ Key: "Ctrl+Shift+F9", Action: "screenshot" }],
            Soon,
            "the action's key to be saved");
        await app.WaitUntilAsync(() => app.Ui.TextOf("GlobalActionKey_screenshot") == "Ctrl+Shift+F9", Soon, "the box to show the key");

        // The same key for another action is refused there.
        app.Ui.SelectComboItem("GlobalNewAction", "Home");
        app.Ui.Invoke("GlobalAddAction");
        app.Ui.Focus("GlobalActionKey_home");
        await app.PressChordAsync(Ctrl, Shift, F9);
        await app.WaitUntilAsync(
            () => app.Ui.Read("GlobalActionNote_home", i => i.Name) == "Another shortcut from anywhere uses this key.",
            Soon,
            "the refusal under Home");
        Assert.Single(ConfigFile.Load(package.Paths.Config).GlobalKeys.Actions);

        app.Ui.Invoke("GlobalActionRemove_screenshot");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).GlobalKeys.Actions.Count == 0, Soon, "the action's key to go");
        await app.WaitUntilAsync(() => !app.Ui.Exists("GlobalActionKey_screenshot"), Soon, "the row to go");
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task GlobalKeys_RowsFollowTheMasterSwitch()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenGlobalKeysAsync(package);

        app.Ui.Toggle("GlobalKeysEnabled", on: false);
        await app.WaitForStatusAsync(s => !s["globalKeys"]!["enabled"]!.GetValue<bool>(), Soon, "keys from anywhere to be off");
        foreach (var id in new[] { "GlobalShowHide", "GlobalWhenBehind", "GlobalHideTo", "GlobalAnnounce", "GlobalAddAction" })
        {
            Assert.False(app.Ui.Read(id, i => i.IsEnabled), $"{id} can still be changed.");
            Assert.StartsWith("Turn on", app.Ui.Read(id, i => i.HelpText), StringComparison.Ordinal);
        }

        app.Ui.Toggle("GlobalKeysEnabled", on: true);
        await app.WaitUntilAsync(() => app.Ui.Read("GlobalShowHide", i => i.IsEnabled), Soon, "the rows to come back");
        app.Ui.SelectComboItem("GlobalHideTo", "The taskbar");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).GlobalKeys.HideTo == "taskbar", Soon, "the choice to be saved");
        await app.QuitAsync();
    }

    /// <summary>A hot key this test process claims for as long as it lasts, as another app would.</summary>
    private sealed class HotKeyClaim : IDisposable
    {
        private const int Id = 0x7A11;

        public HotKeyClaim(uint modifiers, uint key) =>
            Assert.True(RegisterHotKey(IntPtr.Zero, Id, modifiers, key), "The test could not claim its hot key.");

        public void Dispose() => UnregisterHotKey(IntPtr.Zero, Id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
