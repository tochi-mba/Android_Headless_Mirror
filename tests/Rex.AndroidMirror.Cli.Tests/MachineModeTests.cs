using System.Text.Json;
using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class MachineModeTests
{
    [Fact]
    public async Task Capabilities_ReturnsVersionedPromptFreeContract()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            new[] { "capabilities" },
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain('\u001B', result.Json);

        using var doc = JsonDocument.Parse(result.Json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(MachineMode.ProtocolVersion, root.GetProperty("protocolVersion").GetInt32());

        var data = root.GetProperty("data");
        Assert.Equal("non-interactive-json", data.GetProperty("mode").GetString());
        Assert.False(data.GetProperty("output").GetProperty("prompts").GetBoolean());
        Assert.False(data.GetProperty("output").GetProperty("ansi").GetBoolean());
        Assert.Contains(
            data.GetProperty("runtimeActions").EnumerateArray().Select(x => x.GetString()),
            x => x == "sleep");
        Assert.Contains(
            data.GetProperty("mirrorCommands").EnumerateArray().Select(x => x.GetString()),
            x => x == "reset-zoom");
        Assert.Contains(
            data.GetProperty("configPaths").EnumerateArray().Select(x => x.GetString()),
            x => x == "MirrorChrome.MaxZoom");
    }

    [Fact]
    public async Task EmptyMachineCommand_ReturnsCapabilities()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            Array.Empty<string>(),
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("help", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task Status_IsStructuredJson()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true, true, false, true, false,
                "adb.exe", "scrcpy.exe",
                new[]
                {
                    new RexDevice(
                        "USB123", "device", false,
                        "Samsung", "S24", "Samsung S24")
                })
        };

        var result = await MachineMode.RunAsync(
            new[] { "status" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");

        Assert.True(data.GetProperty("SetupComplete").GetBoolean());
        Assert.True(data.GetProperty("AutostartEnabled").GetBoolean());
        Assert.Equal(
            "USB123",
            data.GetProperty("Devices")[0].GetProperty("Serial").GetString());
    }

    [Fact]
    public async Task RuntimeAction_DispatchesWithoutPrompts()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice(
            "USB123", "device", false, "", "", "Android"));

        var result = await MachineMode.RunAsync(
            new[] { "action", "sleep" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(bridge.Calls);
        Assert.Equal("scrcpy-action", call.Action);
        Assert.Equal("sleep", call.Name);
        Assert.Equal("USB123", call.Serial);
    }

    [Fact]
    public async Task UnknownRuntimeAction_IsRejectedBeforeBridge()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice(
            "USB123", "device", false, "", "", "Android"));

        var result = await MachineMode.RunAsync(
            new[] { "action", "explode" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(bridge.Calls);

        using var doc = JsonDocument.Parse(result.Json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(
            "Unknown runtime action",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task MirrorCommand_IsStructuredAndDispatched()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice(
            "USB123", "device", false, "", "", "Android"));

        var result = await MachineMode.RunAsync(
            new[] { "mirror", "zoom-in" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(bridge.Calls);
        Assert.Equal("mirror-command", call.Action);
        Assert.Equal("zoom-in", call.Name);
    }

    [Fact]
    public async Task DeviceAnimationScale_UpdatesAllThreeScales()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice(
            "USB123", "device", false, "", "", "Android"));

        var result = await MachineMode.RunAsync(
            new[] { "device", "set", "animation-scale", "0.5" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            new[]
            {
                "animation-window",
                "animation-transition",
                "animation-duration"
            },
            bridge.Calls.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task AndroidList_ReturnsRowsAsObjects()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice(
            "USB123", "device", false, "", "", "Android"));
        bridge.AndroidSettings.Add(("adb_enabled", "1", "protected"));
        bridge.AndroidSettings.Add(("window_animation_scale", "1", "advanced"));

        var result = await MachineMode.RunAsync(
            new[] { "android", "list", "global" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        using var doc = JsonDocument.Parse(result.Json);
        var rows = doc.RootElement.GetProperty("data").GetProperty("rows");

        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal("adb_enabled", rows[0].GetProperty("key").GetString());
        Assert.Equal("protected", rows[0].GetProperty("risk").GetString());
    }

    [Fact]
    public async Task ConfigSet_ReturnsNewValueAndCreatesRollback()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            new[] { "config", "set", "MaxFps", "90" },
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("90", package.Config.Get("MaxFps").Value);
        Assert.True(File.Exists(package.Config.BackupPath));

        using var doc = JsonDocument.Parse(result.Json);
        Assert.Equal(
            "90",
            doc.RootElement.GetProperty("data").GetProperty("value").GetString());
    }

    [Fact]
    public async Task MultipleDevices_RequireExplicitSerial()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("A", "device", false, "", "", "A"));
        bridge.Devices.Add(new RexDevice("B", "device", false, "", "", "B"));

        var result = await MachineMode.RunAsync(
            new[] { "action", "sleep" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(bridge.Calls);

        using var doc = JsonDocument.Parse(result.Json);
        Assert.Contains(
            "--serial",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Start_RemovesPersistentStopAndReturnsJson()
    {
        using var package = new TempPackage();
        File.WriteAllText(package.Paths.StopFlag, "off");
        var runner = new FakeProcessRunner();

        var result = await MachineMode.RunAsync(
            new[] { "start" },
            package.Paths,
            runner,
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(package.Paths.StopFlag));
        Assert.Single(runner.Calls);

        using var doc = JsonDocument.Parse(result.Json);
        Assert.True(
            doc.RootElement.GetProperty("data").GetProperty("requested").GetBoolean());
    }

    [Fact]
    public async Task ProcessFailure_IsStillValidJson()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(19, "", "setup exploded")
        };

        var result = await MachineMode.RunAsync(
            new[] { "setup" },
            package.Paths,
            runner,
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(19, result.ExitCode);

        using var doc = JsonDocument.Parse(result.Json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "setup exploded",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }
}
