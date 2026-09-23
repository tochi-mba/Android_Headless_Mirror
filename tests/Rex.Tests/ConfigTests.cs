using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class ConfigTests
{
    [Fact]
    public void FreshConfig_HasSafeDefaults()
    {
        var config = new RexConfig();
        config.Normalize();

        Assert.True(config.Session.TurnScreenOff);
        Assert.True(config.Session.PreferUsb);
        Assert.False(config.Wireless.Enabled);
        Assert.True(config.Touchpad.TwoFingerToAndroid);
        Assert.True(config.Zoom.Enabled);
        Assert.Equal("h264", config.Mirror.VideoCodec);
        Assert.Equal(RexConfig.CurrentVersion, config.Version);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var config = new RexConfig();
        config.Mirror.MaxFps = 999;
        config.Mirror.VideoBitRate = "lots";
        config.Mirror.VideoCodec = "vp9";
        config.Mirror.RecordDirectory = @"C:\elsewhere";
        config.Zoom.MaxZoom = 40;
        config.Touchpad.Sensitivity = double.NaN;
        config.Session.PollSeconds = 0;

        config.Normalize();

        Assert.Equal(MirrorSettings.FpsUpperBound, config.Mirror.MaxFps);
        Assert.Equal("12M", config.Mirror.VideoBitRate);
        Assert.Equal("h264", config.Mirror.VideoCodec);
        Assert.Equal("captures/recordings", config.Mirror.RecordDirectory);
        Assert.Equal(8.0, config.Zoom.MaxZoom);
        Assert.Equal(1.0, config.Touchpad.Sensitivity);
        Assert.Equal(1, config.Session.PollSeconds);
    }

    [Fact]
    public void Load_MigratesSchemaOneFile()
    {
        using var package = new TestPackage();
        File.WriteAllText(package.Paths.Config, """
            {
              "WindowTitle": "Android Device",
              "TurnPhysicalScreenOff": false,
              "StayAwakeWhenUsb": true,
              "MaxSize": 1600,
              "MaxFps": 90,
              "VideoBitRate": "8M",
              "Wireless": { "Enabled": true, "Port": 5556, "EnableTcpipWhenUsbAvailable": true, "ManualHosts": ["10.0.0.9"] },
              "MirrorChrome": { "NativeTouchpadGestures": true, "MaxZoom": 3, "CtrlWheelZoom": false, "ShowZoomMinimap": false },
              "PatternOverlay": { "Enabled": true, "PromptPerDevice": false, "Opacity": 0.5 },
              "ControlCenter": { "ConfirmSensitiveDeviceWrites": false, "ScreenshotDirectory": "captures/shots" },
              "ScrcpySession": { "VideoCodec": "h265", "AudioEnabled": false, "RecordOnStart": true },
              "Root": { "Enabled": true },
              "Display": { "DefaultTransport": "scrcpy" },
              "ExtraScrcpyArgs": "--render-fit=letterbox"
            }
            """);

        var config = ConfigFile.Load(package.Paths.Config);

        Assert.False(config.Session.TurnScreenOff);
        Assert.Equal(1600, config.Mirror.MaxSize);
        Assert.Equal(90, config.Mirror.MaxFps);
        Assert.Equal("8M", config.Mirror.VideoBitRate);
        Assert.Equal("h265", config.Mirror.VideoCodec);
        Assert.False(config.Mirror.Audio);
        Assert.True(config.Mirror.RecordOnStart);
        Assert.Equal("--render-fit=letterbox", config.Mirror.ExtraArgs);
        Assert.True(config.Wireless.Enabled);
        Assert.Equal(5556, config.Wireless.Port);
        Assert.Equal(["10.0.0.9"], config.Wireless.ManualHosts);
        Assert.Equal(3.0, config.Zoom.MaxZoom);
        Assert.False(config.Zoom.WheelZoom);
        Assert.False(config.Zoom.ShowNavigator);
        Assert.False(config.PatternGuide.AskPerDevice);
        Assert.Equal(0.5, config.PatternGuide.Opacity);
        Assert.False(config.App.ConfirmSensitiveWrites);
        Assert.Equal("captures/shots", config.App.ScreenshotDirectory);
    }

    [Fact]
    public void Save_IsAtomicAndKeepsBackup()
    {
        using var package = new TestPackage();
        var config = ConfigFile.Load(package.Paths.Config);
        config.Mirror.MaxFps = 30;
        ConfigFile.Save(package.Paths.Config, config);

        Assert.True(File.Exists(ConfigFile.BackupPath(package.Paths.Config)));
        Assert.False(File.Exists(package.Paths.Config + ".tmp"));
        Assert.Equal(30, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);

        Assert.True(ConfigFile.RestoreBackup(package.Paths.Config));
        Assert.Equal(60, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);
    }

    [Fact]
    public void Load_RejectsInvalidJsonWithHelpfulMessage()
    {
        using var package = new TestPackage();
        File.WriteAllText(package.Paths.Config, "{ not json");

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigFile.Load(package.Paths.Config));
        Assert.Contains("rex-backup", ex.Message);
    }

    [Fact]
    public void ConfigStore_GetSetPreserveTypesAndAreCaseInsensitive()
    {
        using var package = new TestPackage();
        var store = new ConfigStore(package.Paths.Config);

        Assert.Equal("60", store.Get("mirror.maxfps").Value);
        var leaf = store.Set("Mirror.MaxFps", "90");
        Assert.Equal("90", leaf.Value);
        Assert.Equal(System.Text.Json.JsonValueKind.Number, leaf.Kind);

        store.Set("Session.TurnScreenOff", "false");
        Assert.Equal("false", store.Get("Session.TurnScreenOff").Value);

        Assert.Throws<FormatException>(() => store.Set("Session.TurnScreenOff", "maybe"));
        Assert.Throws<KeyNotFoundException>(() => store.Set("Mirror.NoSuchKey", "1"));
        Assert.Throws<InvalidOperationException>(() => store.Set("Version", "9"));
        Assert.Contains(store.Flatten(), l => l.Path == "Zoom.MaxZoom");
    }

    [Fact]
    public async Task ConfigStore_ConcurrentIndependentUpdatesDoNotGetLost()
    {
        using var package = new TestPackage();
        var first = new ConfigStore(package.Paths.Config);
        var second = new ConfigStore(package.Paths.Config);

        for (var iteration = 0; iteration < 20; iteration++)
        {
            await Task.WhenAll(
                Task.Run(() => first.Set("Mirror.MaxFps", "90")),
                Task.Run(() => second.Set("Session.TurnScreenOff", "false")));
        }

        var reloaded = new ConfigStore(package.Paths.Config);
        Assert.Equal("90", reloaded.Get("Mirror.MaxFps").Value);
        Assert.Equal("false", reloaded.Get("Session.TurnScreenOff").Value);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(package.Paths.Config)!,
            "." + Path.GetFileName(package.Paths.Config) + ".*.tmp"));
    }

    [Fact]
    public void ConfigStore_SetNormalizesThroughTypedModel()
    {
        using var package = new TestPackage();
        var store = new ConfigStore(package.Paths.Config);

        var leaf = store.Set("Mirror.MaxFps", "5000");

        Assert.Equal(MirrorSettings.FpsUpperBound.ToString(), leaf.Value);
        var json = JsonNode.Parse(File.ReadAllText(package.Paths.Config))!.AsObject();
        Assert.Equal(RexConfig.CurrentVersion, json["Version"]!.GetValue<int>());
    }

    [Fact]
    public void Normalize_ClampsEverySoftBackgroundKnob()
    {
        var config = new RexConfig();
        config.Ambient.Placement = "LEFT";
        config.Ambient.Scaling = "sideways";
        config.Ambient.Opacity = 7;
        config.Ambient.Blur = -3;
        config.Ambient.Size = double.NaN;
        config.Ambient.OffsetX = -9;
        config.Ambient.OffsetY = 0.25;
        config.Normalize();
        Assert.Equal("left", config.Ambient.Placement);
        Assert.Equal("cover", config.Ambient.Scaling);
        Assert.Equal(1, config.Ambient.Opacity);
        Assert.Equal(0, config.Ambient.Blur);
        Assert.Equal(1, config.Ambient.Size);
        Assert.Equal(-1, config.Ambient.OffsetX);
        Assert.Equal(0.25, config.Ambient.OffsetY);
        config.Ambient.TintHue = 400;
        config.Ambient.FrameRate = 500;
        config.Zoom.NavigatorCorner = "middle";
        config.Zoom.NavigatorWidth = 5;
        config.Normalize();
        Assert.Equal(360, config.Ambient.TintHue);
        Assert.Equal(30, config.Ambient.FrameRate);
        Assert.Equal("bottom-right", config.Zoom.NavigatorCorner);
        Assert.Equal(100, config.Zoom.NavigatorWidth);

        using var package = new TestPackage();
        var store = new ConfigStore(package.Paths.Config);
        Assert.Equal("0.5", store.Set("Ambient.Size", "0.5").Value);
        Assert.Equal("bottom", store.Set("Ambient.Placement", "bottom").Value);
    }

    [Fact]
    public void Hud_KeepsItsChoicesUsableAndItsButtonsReal()
    {
        var config = new RexConfig();
        config.Hud.Position = "MIDDLE";
        config.Hud.Scale = 9;
        config.Hud.Opacity = 0;
        config.Hud.HideSeconds = 99;
        config.Hud.Buttons = ["home", "home", "not-an-action", "screenshot"];
        config.Normalize();

        Assert.Equal("top", config.Hud.Position);
        Assert.Equal(1.75, config.Hud.Scale);
        Assert.Equal(0.3, config.Hud.Opacity);
        Assert.Equal(15, config.Hud.HideSeconds);
        Assert.Equal(["home", "screenshot"], config.Hud.Buttons);

        // A copy must not share the button list with the original.
        var copy = config.Copy();
        copy.Hud.Buttons.Add("back");
        Assert.Equal(["home", "screenshot"], config.Hud.Buttons);
        Assert.All(HudSettings.DefaultButtons, id => Assert.NotNull(MirrorActions.Find(id)));
    }

    [Fact]
    public void HudLayout_PinsTheBarAndListensAtThatEdge()
    {
        Assert.Equal((440, 12), HudLayout.Anchor("top", 120, 40, 1000, 800, 12));
        Assert.Equal((440, 748), HudLayout.Anchor("bottom", 120, 40, 1000, 800, 12));
        Assert.Equal((12, 380), HudLayout.Anchor("left", 120, 40, 1000, 800, 12));
        Assert.Equal((868, 12), HudLayout.Anchor("top-right", 120, 40, 1000, 800, 12));

        var top = HudLayout.HoverZone("top", 1000, 800);
        Assert.True(HudLayout.Contains(top, 500, 2));
        Assert.False(HudLayout.Contains(top, 500, 400));

        var bottomRight = HudLayout.HoverZone("bottom-right", 1000, 800);
        Assert.True(HudLayout.Contains(bottomRight, 980, 780));
        Assert.False(HudLayout.Contains(bottomRight, 20, 780));
        Assert.False(HudLayout.Contains(HudLayout.HoverZone("left", 1000, 800), 980, 400));
        Assert.True(HudLayout.Contains(HudLayout.HoverZone("right", 1000, 800), 995, 400));
        Assert.True(HudLayout.IsVertical("left"));
        Assert.False(HudLayout.IsVertical("top-left"));
    }

    [Fact]
    public void HudLayout_HonoursADraggedPositionAndBringsItBackIntoView()
    {
        // With nothing dragged the bar sits where it is pinned.
        Assert.Equal((440, 12), HudLayout.Place("top", null, null, 120, 40, 1000, 800, 12));
        Assert.Equal((12, 380), HudLayout.Place("left", null, null, 120, 40, 1000, 800, 12));

        // A dragged position is kept as a fraction, so the bar lands in the same relative spot on
        // any window size, and it is always measured to the middle of the bar.
        Assert.Equal((190, 140), HudLayout.Place("top", 0.25, 0.2, 120, 40, 1000, 800, 12));
        Assert.Equal((380, 280), HudLayout.Place("top", 0.25, 0.2, 240, 80, 2000, 1600, 12));

        // A bar dragged to the very edge, or one too big for the window, still shows.
        Assert.Equal((880, 760), HudLayout.Place("top", 1, 1, 120, 40, 1000, 800, 12));
        Assert.Equal((0, 0), HudLayout.Place("top", 0, 0, 120, 40, 1000, 800, 12));
        Assert.Equal((0, 380), HudLayout.Place("top", 0.5, 0.5, 4000, 40, 1000, 800, 12));

        Assert.Equal((0.25, 0.2), HudLayout.Fraction(250, 160, 1000, 800));
        Assert.Equal((1.0, 0.0), HudLayout.Fraction(9000, -50, 1000, 800));
        Assert.Equal((0.5, 0.5), HudLayout.Fraction(10, 10, 0, 800));

        // Where the bar was dropped is where reaching for it brings it back.
        var zone = HudLayout.HoverZone(0.25, 0.2, 120, 40, 1000, 800, "top", 12);
        Assert.True(HudLayout.Contains(zone, 250, 160));
        Assert.False(HudLayout.Contains(zone, 700, 600));

        // With nothing dragged it falls back to the strip along the pinned edge.
        Assert.Equal(HudLayout.HoverZone("bottom", 1000, 800), HudLayout.HoverZone(null, null, 120, 40, 1000, 800, "bottom", 12));
    }

    [Fact]
    public void HudSettings_KeepADraggedPositionOnlyWhenItMakesSense()
    {
        var hud = new HudSettings { X = 0.4, Y = 0.9 };
        hud.Normalize();
        Assert.True(hud.IsPlaced);
        Assert.Equal(0.4, hud.X);

        var offscreen = new HudSettings { X = 4, Y = -2 };
        offscreen.Normalize();
        Assert.Equal(1, offscreen.X);
        Assert.Equal(0, offscreen.Y);

        var broken = new HudSettings { X = double.NaN, Y = 0.5 };
        broken.Normalize();
        Assert.False(broken.IsPlaced);
        Assert.Null(broken.X);

        Assert.False(new HudSettings().IsPlaced);
    }

    [Fact]
    public void AmbientLayout_PlacesTheCaptureAndPicksTheMargins()
    {
        var cover = new AmbientSettings();
        Assert.Equal((900, 2000), AmbientLayout.ImageSize(cover, 450, 1000, 900, 600));
        Assert.Equal((270, 600), AmbientLayout.ImageSize(new AmbientSettings { Scaling = "fit" }, 450, 1000, 900, 600));
        Assert.Equal((1800, 1200), AmbientLayout.ImageSize(new AmbientSettings { Scaling = "stretch", Size = 2 }, 450, 1000, 900, 600));
        Assert.Equal((0, 0), AmbientLayout.ImageSize(cover, 0, 0, 900, 600));

        var phone = new RectD(300, 0, 300, 600);
        Assert.Equal(new RectD(0, 0, 900, 600), AmbientLayout.Region("around", phone, 900, 600));
        Assert.Equal(new RectD(0, 0, 300, 600), AmbientLayout.Region("left", phone, 900, 600));
        Assert.Equal(new RectD(600, 0, 300, 600), AmbientLayout.Region("right", phone, 900, 600));
        Assert.Equal(new RectD(0, 0, 900, 0), AmbientLayout.Region("top", phone, 900, 600));
        Assert.Equal(new RectD(0, 600, 900, 0), AmbientLayout.Region("bottom", phone, 900, 600));

        Assert.Equal(((byte)255, (byte)0, (byte)0), AmbientLayout.HueToRgb(0));
        Assert.Equal(((byte)0, (byte)255, (byte)0), AmbientLayout.HueToRgb(120));
        Assert.Equal(((byte)0, (byte)0, (byte)255), AmbientLayout.HueToRgb(240));
        Assert.Equal((12, 12), AmbientLayout.NavigatorPosition("top-left", 150, 100, 900, 600, 12));
        Assert.Equal((738, 488), AmbientLayout.NavigatorPosition("bottom-right", 150, 100, 900, 600, 12));
        // A navigator wider than the window still starts inside it.
        Assert.Equal((0, 488), AmbientLayout.NavigatorPosition("bottom-right", 2000, 100, 900, 600, 12));
    }

    [Fact]
    public void AmbientBlur_CapturesSmallAndSoftensWithoutShiftingTheColour()
    {
        // A soft background has no detail to lose, so a sharper look is the only reason to carry
        // more pixels. The captured copy stays small whatever the window is doing.
        Assert.Equal(480, AmbientBlur.CaptureWidth(0));
        Assert.Equal(256, AmbientBlur.CaptureWidth(20));
        Assert.Equal(160, AmbientBlur.CaptureWidth(80));

        // The radius is expressed against the window, so it has to shrink with the copy.
        Assert.Equal(0, AmbientBlur.SmallRadius(0, 256, 1920));
        Assert.Equal(4, AmbientBlur.SmallRadius(30, 256, 1920));
        Assert.Equal(16, AmbientBlur.SmallRadius(400, 256, 1920));
        Assert.Equal(1, AmbientBlur.SmallRadius(2, 256, 1920));
        Assert.Equal(0, AmbientBlur.SmallRadius(30, 256, 0));

        // One bright pixel in a black field spreads outwards and keeps its total brightness.
        const int width = 32, height = 32;
        var pixels = new byte[width * height * 4];
        var centre = ((height / 2) * width + (width / 2)) * 4;
        pixels[centre] = pixels[centre + 1] = pixels[centre + 2] = pixels[centre + 3] = 255;
        AmbientBlur.Apply(pixels, width, height, radius: 4);

        Assert.True(pixels[centre] < 255, "The bright pixel must be spread, not left alone.");
        var neighbour = ((height / 2) * width + (width / 2) + 2) * 4;
        Assert.True(pixels[neighbour] > 0, "Light must reach the pixels around it.");
        Assert.All(pixels, value => Assert.InRange(value, (byte)0, (byte)255));
        Assert.Equal(0, pixels[0]);

        // A radius of zero, or a buffer too small to describe the picture, is left untouched.
        var untouched = new byte[width * height * 4];
        untouched[centre] = 255;
        AmbientBlur.Apply(untouched, width, height, radius: 0);
        Assert.Equal(255, untouched[centre]);
        AmbientBlur.Apply(untouched, width * 4, height, radius: 3);
        Assert.Equal(255, untouched[centre]);
    }

    [Fact]
    public void ZoomMath_Refit_FillsTheViewportWhenThePhoneRotates()
    {
        // Rotating the phone changes the picture's shape. Carrying the old view forward would keep
        // a landscape picture inside the tall rectangle the portrait one left behind.
        var portrait = ZoomMath.Refit(1000, 800, 0.45, 1.0);
        Assert.Equal(800, portrait.SurfaceHeight, 3);
        Assert.Equal(360, portrait.SurfaceWidth, 3);

        var landscape = ZoomMath.Refit(1000, 800, 1000 / 450.0, 1.0);
        Assert.Equal(1000, landscape.SurfaceWidth, 3);
        Assert.Equal(450, landscape.SurfaceHeight, 3);
        Assert.Equal(0, landscape.OffsetX, 3);
        Assert.Equal(175, landscape.OffsetY, 3);

        // Zoom survives the rotation, centred on the middle of the new picture.
        var zoomed = ZoomMath.Refit(1000, 800, 1000 / 450.0, 2.0);
        Assert.Equal(2000, zoomed.SurfaceWidth, 3);
        Assert.Equal(-500, zoomed.OffsetX, 3);
    }

    [Fact]
    public void Normalize_DropsMalformedOrManagedExtraArgs()
    {
        var malformed = new RexConfig();
        malformed.Mirror.ExtraArgs = "\"unterminated";
        malformed.Normalize();
        Assert.Equal(string.Empty, malformed.Mirror.ExtraArgs);

        var managed = new RexConfig();
        managed.Mirror.ExtraArgs = "--serial=OTHER";
        managed.Normalize();
        Assert.Equal(string.Empty, managed.Mirror.ExtraArgs);
    }

    [Theory]
    [InlineData("\"unterminated")]
    [InlineData("'unterminated")]
    [InlineData("--serial=OTHER")]
    [InlineData("--fullscreen")]
    public void ConfigStore_RejectsInvalidExtraArgsWithoutChangingConfig(string value)
    {
        using var package = new TestPackage();
        var store = new ConfigStore(package.Paths.Config);
        var before = store.Get("Mirror.ExtraArgs").Value;

        Assert.Throws<FormatException>(() => store.Set("Mirror.ExtraArgs", value));

        Assert.Equal(before, store.Get("Mirror.ExtraArgs").Value);
    }

    [Fact]
    public void ConfigStore_AcceptsQuotedExtraArgs()
    {
        using var package = new TestPackage();
        var store = new ConfigStore(package.Paths.Config);

        var leaf = store.Set("Mirror.ExtraArgs", "--render-fit=letterbox \"--background-color=#123456\"");

        Assert.Equal("--render-fit=letterbox \"--background-color=#123456\"", leaf.Value);
    }

    [Theory]
    [InlineData("captures/shots", true)]
    [InlineData("shots", true)]
    [InlineData("../outside", false)]
    [InlineData(@"C:\abs", false)]
    [InlineData("", false)]
    public void PathRules_SafeRelativePath(string value, bool expected) =>
        Assert.Equal(expected, PathRules.IsSafeRelativePath(value));
}
