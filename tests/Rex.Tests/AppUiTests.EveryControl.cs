using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The settings that only config.json used to hold, now in the tab, and seeing what you changed:
/// the Changed chip's list, putting one setting back, and putting a whole group back.
/// </summary>
public sealed partial class AppUiTests
{
    private static async Task<AppProcess> OpenSettingsAsync(TestPackage package, params string[] groups)
    {
        var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        foreach (var group in groups)
        {
            app.Ui.ExpandGroup(group);
        }

        return app;
    }

    [Fact(Timeout = 150_000)]
    public async Task Settings_TheLastValuesHaveRowsThatSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupDisplay", "GroupAudio", "GroupSession", "GroupZoom", "GroupInput", "GroupLockScreen");
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);

        foreach (var (id, check) in new (string, Func<RexConfig, bool>)[]
                 {
                     ("KeepActive", c => !c.Session.KeepActive),
                     ("WakeBeforeMirror", c => !c.Session.WakeBeforeMirror),
                     ("DismissKeyguard", c => !c.Session.DismissKeyguard),
                     ("WheelZoom", c => !c.Zoom.WheelZoom),
                     ("PinchZoom", c => !c.Zoom.PinchZoom),
                     ("PatternAsk", c => !c.PatternGuide.AskPerDevice),
                     ("PatternTrail", c => !c.PatternGuide.ShowCursorTrail),
                 })
        {
            app.Ui.Toggle(id, on: false);
            await app.WaitUntilAsync(() => check(Saved()), Soon, id + " to save");
        }

        app.Ui.SelectComboItem("AudioCodec", "FLAC (lossless)");
        await app.WaitUntilAsync(() => Saved().Mirror.AudioCodec == "flac", Soon, "the sound format to save");

        // Touchpad gestures off: the gestures that depend on it cannot be changed, and say so by being off.
        app.Ui.Toggle("TouchpadEnabled", on: false);
        await app.WaitUntilAsync(() => !Saved().Touchpad.Enabled, Soon, "touchpad gestures to be turned off");
        await app.WaitUntilAsync(() => !app.Ui.Read("TwoFinger", e => e.IsEnabled), Soon, "two-finger gestures to wait for the touchpad");
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task Settings_ConnectionAndLogRowsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupConnection");
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);

        app.Ui.Toggle("PreferUsb", on: false);
        await app.WaitUntilAsync(() => !Saved().Session.PreferUsb, Soon, "USB first to be turned off");

        // An address that is not on this network is refused in words, and nothing is saved.
        app.Ui.SetText("HostToAdd", "8.8.8.8");
        app.Ui.Invoke("HostAdd");
        await app.WaitUntilAsync(() => app.Ui.Exists("HostProblem"), Soon, "the address to be refused");
        Assert.StartsWith("That address is not on a home or office network.", app.Ui.Read("HostProblem", e => e.Name), StringComparison.Ordinal);
        Assert.Empty(Saved().Wireless.ManualHosts);

        app.Ui.SetText("HostToAdd", "192.168.1.30");
        app.Ui.Invoke("HostAdd");
        await app.WaitUntilAsync(() => Saved().Wireless.ManualHosts.SequenceEqual(["192.168.1.30:5555"]), Soon, "the address to be added");
        await app.WaitUntilAsync(() => app.Ui.Exists("host-remove-192.168.1.30:5555"), Soon, "the address to be listed");
        app.Ui.Invoke("host-remove-192.168.1.30:5555");
        await app.WaitUntilAsync(() => Saved().Wireless.ManualHosts.Count == 0, Soon, "the address to be removed");

        app.Ui.SelectComboItem("LogSize", "10 MB");
        await app.WaitUntilAsync(() => Saved().Logging.MaxBytes == 10 * 1024 * 1024, Soon, "the log size to save");
        app.Ui.Toggle("LoggingEnabled", on: false);
        await app.WaitUntilAsync(() => !Saved().Logging.Enabled, Soon, "the log to be turned off");
        await app.WaitUntilAsync(() => !app.Ui.Read("LogSize", e => e.IsEnabled), Soon, "the log's rows to wait for it");
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task Settings_ChangedListsWhatYouChangedAndPutsItBack()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Mirror.MaxFps = 30;
            c.Session.KeepActive = false;
            c.Session.WakeBeforeMirror = false;
        });
        using var app = await OpenSettingsAsync(package);
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);
        Assert.Equal("Show only the 3 settings you changed", app.Ui.Read("SettingsChangedOnly", e => e.Name));

        app.Ui.Toggle("SettingsChangedOnly", on: true);
        await app.WaitUntilAsync(() => app.Ui.Exists("ChangedCount"), Soon, "the list of what you changed");
        Assert.Equal("3 settings are not as the app ships.", app.Ui.Read("ChangedCount", e => e.Name));
        Assert.True(app.Ui.Exists("putback-Mirror.MaxFps"));
        Assert.Equal("Put Frame rate back to how it ships", app.Ui.Read("putback-Mirror.MaxFps", e => e.Name));
        await app.SaveScreenshotAsync("ui-settings-changed.png");

        // One back: only that one moves.
        app.Ui.Invoke("putback-Mirror.MaxFps");
        await app.WaitUntilAsync(() => Saved().Mirror.MaxFps == new RexConfig().Mirror.MaxFps, Soon, "the frame rate to go back");
        Assert.False(Saved().Session.KeepActive);
        await app.WaitUntilAsync(() => app.Ui.Read("ChangedCount", e => e.Name) == "2 settings are not as the app ships.", Soon, "the list to follow");

        // A group back, after asking.
        app.Ui.Toggle("SettingsChangedOnly", on: false);
        app.Ui.ExpandGroup("GroupSession");
        app.Ui.Invoke("putback-group-GroupSession");
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", e => e.Name) == "Put it back", Soon, "the question");
        app.Ui.Invoke("ConfirmAccept");
        await app.WaitUntilAsync(() => Saved().Session is { KeepActive: true, WakeBeforeMirror: true }, Soon, "the group to go back");
        await app.WaitUntilAsync(() => !app.Ui.Read("putback-group-GroupSession", e => e.IsEnabled), Soon, "the group to have nothing left to put back");
        await app.QuitAsync();
    }
}
