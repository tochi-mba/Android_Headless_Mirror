using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The launch settings through the Settings tab: mouse buttons, keys, clipboard, controllers,
/// the smoothing buffer, renderer, audio, the phone's timeout, keeping the PC awake, the app to
/// open and the recording format. Each saves, offers the restart, and reaches scrcpy after it.
/// </summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 75_000)]
    public async Task Settings_MouseKeyboardAudioAndSessionChoicesSaveAndApplyOnRestart()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");

        app.Ui.ExpandGroup("GroupInput");
        app.Ui.SelectComboItem("RightClick", "Click on the phone");
        app.Ui.SelectComboItem("ForwardButton", "Nothing");
        app.Ui.Toggle("ShiftClicks", on: false);
        app.Ui.Toggle("MouseHover", on: false);
        app.Ui.Toggle("LegacyPaste", on: true);
        app.Ui.Toggle("Gamepad", on: true);
        await app.WaitUntilAsync(
            () => ConfigFile.Load(package.Paths.Config).Input is { RightClick: "click", ForwardButton: "nothing", ShiftClicks: false, MouseHover: false, LegacyPaste: true, Gamepad: "uhid" },
            Soon,
            "the mouse and keyboard choices saved");
        await app.WaitForStatusAsync(s => s["restartRequired"]!.GetValue<bool>(), Soon, "restart notice");
        await app.SaveScreenshotAsync("ui-settings-input.png");

        // A source the phone cannot duplicate rules out keeping the audio playing there.
        app.Ui.ExpandGroup("GroupAdvanced");
        app.Ui.SelectComboItem("VideoBuffer", "100 ms");
        app.Ui.SelectComboItem("RenderDriver", "OpenGL");
        app.Ui.ExpandGroup("GroupAudio");
        app.Ui.SelectComboItem("AudioSource", "Microphone");
        app.Ui.SelectComboItem("AudioBitRate", "256 kbps");
        await app.WaitUntilAsync(() => !app.Ui.Read("AudioDup", e => e.IsEnabled), Soon, "keeping the audio on the phone to be ruled out");

        // The app to open is checked before it is kept.
        app.Ui.ExpandGroup("GroupSession");
        app.Ui.SelectComboItem("ScreenOffTimeout", "5 minutes");
        app.Ui.Toggle("KeepPcAwake", on: true);
        app.Ui.SetText("StartApp", "--no-video");
        LeaveTheSettingsTab(app);
        await app.WaitUntilAsync(() => app.Ui.Read("StartAppError", e => e.Name).Length > 0, Soon, "an option given as an app to be refused");
        Assert.Equal(string.Empty, ConfigFile.Load(package.Paths.Config).Session.StartApp);
        app.Ui.SetText("StartApp", "com.example.player");
        LeaveTheSettingsTab(app);

        app.Ui.ExpandGroup("GroupCaptures");
        app.Ui.SelectComboItem("RecordFormat", "MKV");
        await app.WaitUntilAsync(() =>
        {
            var config = ConfigFile.Load(package.Paths.Config);
            return config.Mirror is { VideoBufferMs: 100, RenderDriver: "opengl", AudioSource: "mic", AudioBitRate: "256K", RecordFormat: "mkv" } &&
                   config.Session is { ScreenOffTimeoutSeconds: 300, KeepPcAwake: true, StartApp: "com.example.player" };
        }, Soon, "the display, audio and session choices saved");
        Assert.Equal(string.Empty, app.Ui.Read("StartAppError", e => e.Name));
        await app.SaveScreenshotAsync("ui-settings-session.png");

        app.Ui.InvokeNamed("Restart now");
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(l => l.StartsWith("args ", StringComparison.Ordinal)) == 2,
            Startup,
            "the mirror to be relaunched");
        var relaunch = package.ScrcpyLog().Last(l => l.StartsWith("args ", StringComparison.Ordinal));
        foreach (var expected in new[]
        {
            "--video-buffer=100", "--render-driver=opengl", "--audio-source=mic", "--audio-bit-rate=256K",
            "--screen-off-timeout=300", "--disable-screensaver", "--start-app=com.example.player",
            "--mouse-bind=+hs-", "--no-mouse-hover", "--legacy-paste", "--gamepad=uhid",
        })
        {
            Assert.Contains(expected, relaunch, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--audio-dup", relaunch, StringComparison.Ordinal);
        await app.QuitAsync();
    }

    /// <summary>
    /// Moves away from a text box the way a person does, to another tab and back: a box that was
    /// being typed in commits when the keyboard leaves it, and one set from outside the keyboard
    /// already has.
    /// </summary>
    private static void LeaveTheSettingsTab(AppProcess app)
    {
        app.Ui.Select("TabControls");
        app.Ui.Select("TabSettings");
    }
}
