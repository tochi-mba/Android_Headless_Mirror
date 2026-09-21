using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class BridgeClientTests
{
    [Fact]
    public async Task Status_ParsesPackageAndDeviceState()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, """
            setup chatter
            {"Ok":true,"SetupComplete":true,"AutostartEnabled":true,"PersistentOff":false,"SupervisorRunning":true,"MirrorRunning":false,"AdbPath":"C:\adb.exe","ScrcpyPath":"C:\scrcpy.exe","Devices":[{"Serial":"USB123","State":"device","IsTcp":false,"Manufacturer":"Google","Model":"Pixel 9","DisplayName":"Google Pixel 9"}]}
            """, "")
        };
        var client = new BridgeClient(package.Paths, runner);

        var status = await client.GetStatusAsync();

        Assert.True(status.SetupComplete);
        Assert.True(status.AutostartEnabled);
        Assert.True(status.SupervisorRunning);
        Assert.False(status.MirrorRunning);
        var device = Assert.Single(status.Devices);
        Assert.Equal("USB123", device.Serial);
        Assert.Equal("Google Pixel 9", device.DisplayName);
        Assert.False(device.IsTcp);
    }

    [Fact]
    public async Task Devices_ParsesMultipleTransportsAndStates()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                """{"Ok":true,"Devices":[{"Serial":"USB1","State":"device","IsTcp":false,"Manufacturer":"Samsung","Model":"S24","DisplayName":"Samsung S24"},{"Serial":"192.168.1.20:5555","State":"device","IsTcp":true,"Manufacturer":"Google","Model":"Pixel","DisplayName":"Google Pixel"},{"Serial":"LOCKED","State":"unauthorized","IsTcp":false,"Manufacturer":"","Model":"","DisplayName":""}]}""",
                "")
        };
        var client = new BridgeClient(package.Paths, runner);

        var devices = await client.GetDevicesAsync();

        Assert.Equal(3, devices.Count);
        Assert.False(devices[0].IsTcp);
        Assert.True(devices[1].IsTcp);
        Assert.Equal("unauthorized", devices[2].State);
    }

    [Fact]
    public async Task AndroidSettings_ParsesRiskMetadata()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                """{"Ok":true,"Rows":[{"Namespace":"global","Key":"adb_enabled","Value":"1","Risk":"protected"},{"Namespace":"global","Key":"window_animation_scale","Value":"1","Risk":"advanced"}]}""",
                "")
        };
        var client = new BridgeClient(package.Paths, runner);

        var rows = await client.ListAndroidSettingsAsync("USB123", "global");

        Assert.Equal(2, rows.Count);
        Assert.Equal(("adb_enabled", "1", "protected"), rows[0]);
        Assert.Equal(("window_animation_scale", "1", "advanced"), rows[1]);
    }

    [Fact]
    public async Task Invoke_BuildsExactBridgeArguments()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, """{"Ok":true,"Text":"OK"}""", "")
        };
        var client = new BridgeClient(package.Paths, runner);

        using var _ = await client.InvokeAsync(
            "settings-set",
            serial: "USB123",
            ns: "secure",
            key: "font_scale",
            value: "1.15");

        var call = Assert.Single(runner.Calls);
        Assert.Equal("powershell", call.Kind);
        Assert.Equal(package.Paths.Bridge, call.FileName);
        Assert.Equal(
            new[]
            {
                "-Action", "settings-set",
                "-Serial", "USB123",
                "-Namespace", "secure",
                "-Key", "font_scale",
                "-Value", "1.15"
            },
            call.Arguments);
    }

    [Fact]
    public async Task Invoke_UsesLastJsonLineAfterHumanReadableChatter()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                "PowerShell warning\r\nAnother line\r\n{\"Ok\":true,\"Text\":\"READY\"}\r\n",
                "")
        };
        var client = new BridgeClient(package.Paths, runner);

        using var result = await client.InvokeAsync("status");

        Assert.Equal("READY", result.RootElement.GetProperty("Text").GetString());
    }

    [Fact]
    public async Task Invoke_ThrowsBridgeErrorMessage()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(1,
                """{"Ok":false,"Error":"permission denied"}""",
                "")
        };
        var client = new BridgeClient(package.Paths, runner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeAsync("settings-set", serial: "USB", ns: "secure", key: "x", value: "1"));

        Assert.Equal("permission denied", ex.Message);
    }

    [Fact]
    public async Task Invoke_ThrowsWhenNoJsonPayloadExists()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(17, "plain output", "bridge crashed")
        };
        var client = new BridgeClient(package.Paths, runner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InvokeAsync("status"));

        Assert.Contains("bridge crashed", ex.Message);
    }

    [Fact]
    public async Task Invoke_OmitsEmptyOptionalArguments()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, """{"Ok":true}""", "")
        };
        var client = new BridgeClient(package.Paths, runner);

        using var _ = await client.InvokeAsync("status");

        var call = Assert.Single(runner.Calls);
        Assert.Equal(new[] { "-Action", "status" }, call.Arguments);
    }
}
