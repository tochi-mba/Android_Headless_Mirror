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
    public void ConfigStore_SetNormalizesThroughTypedModel()
    {
        using var package = new TestPackage();
        var store = new ConfigStore(package.Paths.Config);

        var leaf = store.Set("Mirror.MaxFps", "5000");

        Assert.Equal(MirrorSettings.FpsUpperBound.ToString(), leaf.Value);
        var json = JsonNode.Parse(File.ReadAllText(package.Paths.Config))!.AsObject();
        Assert.Equal(RexConfig.CurrentVersion, json["Version"]!.GetValue<int>());
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
