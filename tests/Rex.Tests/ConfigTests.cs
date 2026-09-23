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
