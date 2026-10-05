using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// More of scrcpy in the Settings tab: the phone's encoders read and one chosen, a part of the
/// screen, how it is captured and turned, smoothing, taps, sound, the time limit and the restarts,
/// each saved and each on the command line the mirror starts with next.
/// </summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 150_000)]
    public async Task Settings_MoreOfScrcpySavesAndReachesTheNextStart()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupDisplay", "GroupAudio", "GroupSession", "GroupCopies");
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);

        // The encoders come from the phone when asked; the list then offers the ones for the codec.
        Assert.Equal("Read them to choose one.", app.Ui.Read("VideoEncoderNote", e => e.Name));
        app.Ui.Invoke("VideoEncoderRead");
        await app.WaitUntilAsync(() => app.Ui.Read("VideoEncoderNote", e => e.Name).StartsWith("3 encoders for H.264 on ", StringComparison.Ordinal),
            Soon, "the phone's encoders to be read");
        app.Ui.SelectComboItem("VideoEncoder", "c2.exynos.h264.encoder (hardware)");
        await app.WaitUntilAsync(() => Saved().Mirror.VideoEncoder == "c2.exynos.h264.encoder", Soon, "the encoder to save");

        // A crop scrcpy could not use is refused in words, and nothing is saved.
        app.Ui.SetText("Crop", "1080x1200");
        LeaveTheSettingsTab(app);
        await app.WaitUntilAsync(() => app.Ui.Exists("CropError"), Soon, "the crop to be refused");
        Assert.StartsWith("Write it as width:height:x:y", app.Ui.Read("CropError", e => e.Name), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Saved().Mirror.Crop);
        app.Ui.SetText("Crop", "1080:1200:0:600");
        LeaveTheSettingsTab(app);
        await app.WaitUntilAsync(() => Saved().Mirror.Crop == "1080:1200:0:600", Soon, "the crop to save");

        app.Ui.SelectComboItem("CaptureOrientation", "It stays upright");
        app.Ui.SelectComboItem("StartOrientation", "Turned right");
        app.Ui.SetValue("Angle", 15);
        app.Ui.Toggle("SmoothScaling", on: false);
        app.Ui.Toggle("ShowTouches", on: true);
        app.Ui.SetValue("AudioOutputBuffer", 40);
        app.Ui.Toggle("RequireAudio", on: true);
        app.Ui.SetValue("RestartLimit", 7);
        app.Ui.SelectComboItem("TimeLimit", "30 minutes");
        app.Ui.SelectComboItem("CopiesMaxFps", "30 fps");
        await app.WaitUntilAsync(() => Saved() is
        {
            Mirror: { CaptureOrientation: "@0", StartOrientation: "90", Angle: 15, SmoothScaling: false, ShowTouches: true, AudioOutputBufferMs: 40, RequireAudio: true, TimeLimitMinutes: 30 },
            Session.RestartLimit: 7,
            Copies.MaxFps: 30,
        }, Soon, "every new option to save");
        Assert.Equal("7 times", app.Ui.Read("RestartLimitValue", e => e.Name));
        Assert.Equal("15°", app.Ui.Read("AngleValue", e => e.Name));

        // How many restarts means nothing while a closed mirror is not restarted at all.
        app.Ui.Toggle("RestartOnCrash", on: false);
        await app.WaitUntilAsync(() => !app.Ui.Read("RestartLimit", e => e.IsEnabled), Soon, "the restart count to wait for restarting");
        app.Ui.Toggle("RestartOnCrash", on: true);
        await app.SaveScreenshotAsync("ui-settings-scrcpy.png");

        app.Ui.InvokeNamed("Restart now");
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Count(l => l.StartsWith("args ", StringComparison.Ordinal)) == 2,
            Startup,
            "the mirror to be relaunched");
        var relaunch = package.ScrcpyLog().Last(l => l.StartsWith("args ", StringComparison.Ordinal));
        foreach (var expected in new[]
        {
            "--video-encoder=c2.exynos.h264.encoder", "--crop=1080:1200:0:600", "--capture-orientation=@0", "--angle=15",
            "--no-mipmaps", "--show-touches", "--time-limit=1800", "--display-orientation=90", "--audio-output-buffer=40", "--require-audio",
        })
        {
            Assert.Contains(expected, relaunch, StringComparison.Ordinal);
        }

        await app.QuitAsync();
    }
}
