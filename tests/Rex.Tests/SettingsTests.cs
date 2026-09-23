using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class SettingsTests
{
    [Theory]
    [InlineData(0, -0.1, -0.1)]
    [InlineData(1, 0.1, -0.1)]
    [InlineData(2, -0.1, 0.1)]
    [InlineData(3, 0.1, 0.1)]
    public void NavigatorResize_KeepsOppositeCornerFixed(int corner, double dx, double dy)
    {
        var view = new RectD(0.25, 0.25, 0.5, 0.5);
        var result = NavigatorMath.Resize(view, corner, dx, dy);
        Assert.Equal(1.2, result.Scale, 6);
        var sx = corner % 2 == 0 ? -1 : 1;
        var sy = corner / 2 == 0 ? -1 : 1;
        Assert.Equal(0.5 - sx * 0.25, result.X - sx * view.Width * result.Scale / 2, 6);
        Assert.Equal(0.5 - sy * 0.25, result.Y - sy * view.Height * result.Scale / 2, 6);
        Assert.True(double.IsFinite(NavigatorMath.Resize(new RectD(0, 0, 0, 0), corner, dx, dy).Scale));
    }
    [Fact]
    public void NavigatorDrag_FollowsThePointerOnlyWhileTheButtonIsHeld()
    {
        // Both have to agree. The release that ends a drag can happen where the window never hears
        // it, and a drag that keeps going after the finger lifts moves the view around the screen
        // with nothing but resetting the zoom to get out of it.
        Assert.True(NavigatorMath.KeepsDragging(dragging: true, eventSaysHeld: true, buttonIsDown: true));
        Assert.False(NavigatorMath.KeepsDragging(dragging: true, eventSaysHeld: true, buttonIsDown: false));
        Assert.False(NavigatorMath.KeepsDragging(dragging: true, eventSaysHeld: false, buttonIsDown: true));
        Assert.False(NavigatorMath.KeepsDragging(dragging: false, eventSaysHeld: true, buttonIsDown: true));
    }

    [Fact]
    public void LaunchSettings_RevertingAudioRemovesDifference_AndLiveSettingsDoNotRequireRestart()
    {
        var config = new RexConfig();
        var running = ScrcpyArguments.LaunchSettings(config, false);
        config.Mirror.Audio = !config.Mirror.Audio;
        Assert.False(running.SequenceEqual(ScrcpyArguments.LaunchSettings(config, false)));
        config.Mirror.Audio = !config.Mirror.Audio;
        config.Zoom.Enabled = !config.Zoom.Enabled;
        config.App.ScreenshotDirectory = Path.GetTempPath();
        Assert.Equal(running, ScrcpyArguments.LaunchSettings(config, false));
        config.Mirror.RecordOnStart = true;
        Assert.False(running.SequenceEqual(ScrcpyArguments.LaunchSettings(config, false)));
    }

    [Fact]
    public void ScreenshotFolder_ExternalChoiceSurvivesSaveAndReload_AndDefaultStaysRelative()
    {
        using var package = new TestPackage();
        var config = new RexConfig();
        var folder = Path.Combine(Path.GetTempPath(), "Screenshots outside app");
        config.App.ScreenshotDirectory = folder;
        ConfigFile.Save(package.Paths.Config, config);
        var loaded = ConfigFile.Load(package.Paths.Config);
        Assert.Equal(folder, package.Paths.ScreenshotFolder(loaded.App.ScreenshotDirectory));
        Assert.Equal(package.Paths.Inside("captures/screenshots"),
            package.Paths.ScreenshotFolder(new RexConfig().App.ScreenshotDirectory));
        Assert.Throws<InvalidOperationException>(() => package.Paths.ScreenshotFolder("../outside"));
    }
}
