using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services;

namespace Rex.Tests;

/// <summary>
/// The window and behaviour settings without a window: how config.json values are kept sane,
/// the frame rate counter's lines and flag, how far and how fast keyboard swipes go, and what a
/// screenshot is saved as.
/// </summary>
public sealed class WindowPreferencesTests
{
    [Fact]
    public void WindowSettingsAreKeptSane()
    {
        var config = new RexConfig();
        config.App.SidebarSide = "LEFT";
        config.App.TopBarButtons = ["screenshot", "bogus", "home", "HOME", " back "];
        config.App.ScreenshotFormat = "gif";
        config.Input.SwipeLength = 9;
        config.Input.SwipeMilliseconds = 5;
        config.Normalize();

        Assert.Equal("left", config.App.SidebarSide);
        // Known buttons only, once each, in the top bar's own order.
        Assert.Equal(new[] { "home", "back", "screenshot" }, config.App.TopBarButtons);
        Assert.Equal("png", config.App.ScreenshotFormat);
        Assert.Equal(InputSettings.SwipeLengthMax, config.Input.SwipeLength);
        Assert.Equal(InputSettings.SwipeMillisecondsMin, config.Input.SwipeMilliseconds);

        config.App.SidebarSide = "middle";
        config.App.TopBarButtons = null!;
        config.Input.SwipeLength = double.NaN;
        config.Input.SwipeMilliseconds = 99_999;
        config.Normalize();
        Assert.Equal("right", config.App.SidebarSide);
        Assert.Empty(config.App.TopBarButtons);
        Assert.Equal(1, config.Input.SwipeLength);
        Assert.Equal(InputSettings.SwipeMillisecondsMax, config.Input.SwipeMilliseconds);

        var fresh = new RexConfig();
        Assert.Equal(AppSettings.QuickButtons, fresh.App.TopBarButtons);
        Assert.NotSame(fresh.App.TopBarButtons, fresh.Copy().App.TopBarButtons);
        Assert.True(fresh.Zoom.ZoomAtPointer);
        Assert.False(fresh.Zoom.InvertWheel);
        Assert.False(fresh.Zoom.ResetOnRotate);
    }

    [Theory]
    [InlineData("INFO: 60 fps", 60)]
    [InlineData("INFO: 58 fps (+2 frames skipped)", 58)]
    [InlineData("  INFO:   120 fps", 120)]
    [InlineData("INFO: Texture: 1080x2400", null)]
    [InlineData("WARN: 60 fps", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FrameRateLinesAreReadFromScrcpysCounter(string? line, int? rate) =>
        Assert.Equal(rate, ScrcpyArguments.ParseFrameRate(line));

    [Fact]
    public void TheFrameRateCounterStartsWithTheMainSessionButNeverAsksForARestart()
    {
        var config = new RexConfig();
        config.App.ShowFrameRate = true;
        Assert.Contains(ScrcpyArguments.PrintFps, ScrcpyArguments.Build(config, "S", false, "T", null, null));
        Assert.DoesNotContain(ScrcpyArguments.PrintFps, ScrcpyArguments.Build(config, "S", false, "T", null, null, copyIndex: 0));

        // It is switched on and off in the running session, so it is not a launch difference.
        Assert.Equal(ScrcpyArguments.LaunchSettings(new RexConfig(), false), ScrcpyArguments.LaunchSettings(config, false));
    }

    [Fact]
    public void KeyboardSwipesGoAsFarAsAsked()
    {
        var surface = new RECT { Left = 0, Top = 0, Right = 1000, Bottom = 2000 };

        // The usual swipe crosses 44% of the height through the middle.
        var usual = KeyboardTouch.Strokes("swipe-up", surface).Single();
        Assert.Equal((500, 1440), usual[0]);
        Assert.Equal((500, 560), usual[^1]);

        var longer = KeyboardTouch.Strokes("swipe-up", surface, 1.5).Single();
        Assert.Equal((500, 1660), longer[0]);
        Assert.Equal((500, 340), longer[^1]);

        var shorter = KeyboardTouch.Strokes("swipe-left", surface, 0.5).Single();
        Assert.Equal((640, 1000), shorter[0]);
        Assert.Equal((360, 1000), shorter[^1]);

        // Out of range is held to the range, and never leaves the screen.
        Assert.Equal(longer, KeyboardTouch.Strokes("swipe-up", surface, 40).Single());
        Assert.Equal(usual, KeyboardTouch.Strokes("swipe-up", surface, double.NaN).Single());
    }

    [Theory]
    [InlineData(200, 29)]
    [InlineData(80, 11)]
    [InlineData(800, 114)]
    [InlineData(1, 1)]
    public void AKeyboardSwipeTakesAboutAsLongAsAsked(int swipe, int step) =>
        Assert.Equal(step, KeyboardTouch.StepMilliseconds(swipe));

    [Fact]
    public void ScreenshotsAreSavedAsChosen()
    {
        var png = SmallPng();
        Assert.Same(png, ScreenshotFile.Encode(png, "png"));
        Assert.Equal(".png", ScreenshotFile.Extension("png"));
        Assert.Equal(".jpg", ScreenshotFile.Extension("jpg"));

        var jpg = ScreenshotFile.Encode(png, "jpg");
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpg[..2]);
        var decoded = ScreenshotFile.Decode(jpg);
        var original = ScreenshotFile.Decode(png);
        Assert.Equal(original.PixelWidth, decoded.PixelWidth);
        Assert.Equal(original.PixelHeight, decoded.PixelHeight);
        Assert.True(decoded.IsFrozen);
    }

    /// <summary>A small picture with transparency, the way a phone's PNG can be.</summary>
    private static byte[] SmallPng()
    {
        var pixels = Enumerable.Range(0, 8 * 16 * 4).Select(i => (byte)(i % 4 == 3 ? 0x80 : i % 251)).ToArray();
        var bitmap = BitmapSource.Create(8, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
