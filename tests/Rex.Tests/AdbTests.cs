using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class AdbTests
{
    [Fact]
    public void ParseDevices_HandlesEveryState()
    {
        var devices = AdbParsing.ParseDevices("""
            List of devices attached
            USB123       device product:oriole model:Pixel_6 device:oriole transport_id:1
            192.168.1.40:5555 device product:oriole model:Pixel_6 transport_id:2
            AUTH         unauthorized transport_id:3
            OFF          offline transport_id:4
            NOPERM       no permissions transport_id:5
            garbage line

            """);

        Assert.Equal(5, devices.Count);
        Assert.Equal("USB123", devices[0].Serial);
        Assert.False(devices[0].IsTcp);
        Assert.Equal("Pixel 6", devices[0].Model);
        Assert.True(devices[1].IsTcp);
        Assert.Equal("wireless", devices[1].Transport);
        Assert.Equal(["unauthorized", "offline", "no permissions"], devices.Skip(2).Select(d => d.State));
    }

    [Fact]
    public void ParseProperties_AndMarketingName()
    {
        var props = AdbParsing.ParseProperties("[ro.product.model]: [SM-G998B]\n[ro.product.marketname]: [Galaxy S21 Ultra]\n");
        Assert.Equal("SM-G998B", props["ro.product.model"]);
        Assert.Equal("Galaxy S21 Ultra", AdbParsing.PickMarketingName(props));
        Assert.Equal(string.Empty, AdbParsing.PickMarketingName(new Dictionary<string, string>()));
    }

    [Fact]
    public void ParseWmSize_PrefersOverride()
    {
        Assert.Equal((1440, 3200), AdbParsing.ParseWmSize("Physical size: 1440x3200\n"));
        Assert.Equal((1080, 2400), AdbParsing.ParseWmSize("Physical size: 1440x3200\nOverride size: 1080x2400\n"));
        Assert.Equal((0, 0), AdbParsing.ParseWmSize("nothing"));
    }

    [Fact]
    public void ParseBattery_ReadsLevelAndCharging()
    {
        var battery = AdbParsing.ParseBattery("  USB powered: true\n  status: 2\n  level: 74\n");
        Assert.Equal(74, battery!.Level);
        Assert.True(battery.Charging);
        Assert.Null(AdbParsing.ParseBattery("no level here"));
    }

    [Fact]
    public void ParseIpCandidates_PrefersWifiInterfaces()
    {
        const string text = """
            3: rmnet0    inet 10.123.45.67/32 scope global rmnet0
            12: swlan0    inet 192.168.43.1/24 brd 192.168.43.255 scope global swlan0
            14: rndis0    inet 192.168.42.129/24 brd 192.168.42.255 scope global rndis0
            """;
        Assert.Equal(["192.168.43.1"], AdbParsing.ParseIpCandidates(text));
        Assert.Equal(["192.168.99.1", "10.0.0.2"], AdbParsing.ParseIpCandidates("8: mystery0 inet 192.168.99.1/24 scope global\n9: other0 inet 10.0.0.2/24 scope global\n"));
    }

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("256.1.2.3", false)]
    [InlineData("foo", false)]
    public void IsPrivateIPv4(string ip, bool expected) => Assert.Equal(expected, AdbParsing.IsPrivateIPv4(ip));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "''")]
    [InlineData("has space", "'has space'")]
    [InlineData("it's", "'it'\"'\"'s'")]
    [InlineData("$(rm -rf /)", "'$(rm -rf /)'")]
    public void ShellQuoting_QuotesUnsafeValues(string input, string expected) => Assert.Equal(expected, ShellQuoting.Quote(input));

    [Theory]
    [InlineData("secure", "adb_enabled", AndroidSettings.RiskProtected)]
    [InlineData("global", "adb_wifi_enabled", AndroidSettings.RiskProtected)]
    [InlineData("global", "wifi_on", AndroidSettings.RiskSensitive)]
    [InlineData("secure", "font_scale", AndroidSettings.RiskAdvanced)]
    [InlineData("system", "font_scale", AndroidSettings.RiskNormal)]
    public void Risk_ClassifiesKeys(string ns, string key, string expected) => Assert.Equal(expected, AndroidSettings.Risk(ns, key));

    [Fact]
    public async Task PutSetting_RefusesProtectedKeysBeforeRunningAnything()
    {
        var runner = new FakeProcessRunner();
        var adb = new AdbClient("adb.exe", runner);

        var result = await adb.PutSettingAsync("S", "global", "adb_enabled", "0", TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("protected", result.Text);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task PutSetting_QuotesValueForTheDeviceShell()
    {
        var runner = new FakeProcessRunner();
        var adb = new AdbClient("adb.exe", runner);

        var result = await adb.PutSettingAsync("S", "system", "font_scale", "1.15 x", TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(["-s", "S", "shell", "settings put system font_scale '1.15 x'"], call.Arguments);
    }

    [Fact]
    public async Task FriendlySettings_ValidateAndMapToCommands()
    {
        var runner = new FakeProcessRunner();
        var adb = new AdbClient("adb.exe", runner);

        Assert.False((await adb.ApplyFriendlySettingAsync("S", "brightness", "999", TestContext.Current.CancellationToken)).Ok);
        Assert.Empty(runner.Calls);

        Assert.True((await adb.ApplyFriendlySettingAsync("S", "brightness", "200", TestContext.Current.CancellationToken)).Ok);
        Assert.Equal("settings put system screen_brightness 200", runner.Calls[^1].Arguments[^1]);

        Assert.True((await adb.ApplyFriendlySettingAsync("S", "wifi", "enable", TestContext.Current.CancellationToken)).Ok);
        Assert.Equal(["-s", "S", "shell", "svc", "wifi", "enable"], runner.Calls[^1].Arguments);

        Assert.True((await adb.ApplyFriendlySettingAsync("S", "animation-scale", "0.5", TestContext.Current.CancellationToken)).Ok);
        Assert.Contains("animator_duration_scale 0.5", runner.Calls[^1].Arguments[^1]);

        Assert.False((await adb.ApplyFriendlySettingAsync("S", "wm-size", "big", TestContext.Current.CancellationToken)).Ok);
        Assert.True((await adb.ApplyFriendlySettingAsync("S", "wm-size", "reset", TestContext.Current.CancellationToken)).Ok);
    }

    [Fact]
    public async Task RotationOverride_SetsTargetBeforeLockingAndRestoresAuto()
    {
        var runner = new FakeProcessRunner();
        var adb = new AdbClient("adb.exe", runner);

        var forced = await adb.SetRotationOverrideAsync("S", "1", TestContext.Current.CancellationToken);
        Assert.True(forced.Ok);
        Assert.Contains("90", forced.Text);
        Assert.Equal("settings put system user_rotation 1", runner.Calls[0].Arguments[^1]);
        Assert.Equal("settings put system accelerometer_rotation 0", runner.Calls[1].Arguments[^1]);

        var auto = await adb.SetRotationOverrideAsync("S", "auto", TestContext.Current.CancellationToken);
        Assert.True(auto.Ok);
        Assert.Equal("settings put system accelerometer_rotation 1", runner.Calls[^1].Arguments[^1]);

        Assert.False((await adb.SetRotationOverrideAsync("S", "5", TestContext.Current.CancellationToken)).Ok);
    }

    [Fact]
    public async Task RotationOverride_DoesNotLockWhenTargetIsRefused()
    {
        var runner = new FakeProcessRunner { Respond = args => args[^1].Contains("user_rotation") ? new ProcessResult(1, "", "denied") : new ProcessResult(0, "", "") };
        var adb = new AdbClient("adb.exe", runner);

        var result = await adb.SetRotationOverrideAsync("S", "2", TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task KeyEvent_RejectsMalformedKeycodes()
    {
        var runner = new FakeProcessRunner();
        var adb = new AdbClient("adb.exe", runner);

        Assert.False((await adb.KeyEventAsync("S", "KEYCODE_HOME; rm", TestContext.Current.CancellationToken)).Ok);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task DumpUiHierarchy_ExtractsXmlFromTtyOutput()
    {
        var runner = new FakeProcessRunner { Respond = _ => new ProcessResult(0, "UI hierchary dumped to: /dev/tty\n<?xml version='1.0'?><hierarchy rotation=\"0\"><node bounds=\"[0,0][1,1]\"/></hierarchy>\n", "") };
        var adb = new AdbClient("adb.exe", runner);

        var xml = await adb.DumpUiHierarchyAsync("S", TestContext.Current.CancellationToken);

        Assert.StartsWith("<?xml", xml);
        Assert.EndsWith("</hierarchy>", xml);
        Assert.Contains("exec-out", runner.Calls[0].Arguments);
    }

    [Fact]
    public void DeviceStateText_ExplainsUnauthorized()
    {
        var text = DeviceStateText.Describe([new AdbDevice("X", "unauthorized", false, "", "")]);
        Assert.Contains("Allow", text);
        Assert.Contains("Connect", DeviceStateText.Describe([]));
    }

    [Fact]
    public void DeviceSelection_PrefersConfiguredThenUsbThenFirst()
    {
        var usb = new AdbDevice("USB1", "device", false, "", "");
        var wifi = new AdbDevice("10.0.0.2:5555", "device", true, "", "");
        var locked = new AdbDevice("LOCKED", "unauthorized", false, "", "");

        Assert.Equal("USB1", DeviceSelection.Select([wifi, usb, locked], "", preferUsb: true)!.Serial);
        Assert.Equal("10.0.0.2:5555", DeviceSelection.Select([wifi, usb], "", preferUsb: false)!.Serial);
        Assert.Equal("10.0.0.2:5555", DeviceSelection.Select([wifi, usb], "10.0.0.2:5555", preferUsb: true)!.Serial);
        Assert.Equal("USB1", DeviceSelection.Select([wifi, usb], "MISSING", preferUsb: true)!.Serial);
        Assert.Null(DeviceSelection.Select([locked], "", preferUsb: true));
    }
}
