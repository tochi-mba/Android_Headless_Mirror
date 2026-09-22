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
    public async Task ShortcutInstall_UsesShortcutScriptAndReturnsJson()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();

        var result = await MachineMode.RunAsync(
            new[] { "shortcut", "install" },
            package.Paths,
            runner,
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("powershell", call.Kind);
        Assert.Equal(package.Paths.InstallRexShortcut, call.FileName);

        using var doc = JsonDocument.Parse(result.Json);
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("installed").GetBoolean());
    }

    [Fact]
    public async Task ShortcutRemove_UsesRemovalScriptAndReturnsJson()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();

        var result = await MachineMode.RunAsync(
            new[] { "shortcut", "remove" },
            package.Paths,
            runner,
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(package.Paths.RemoveRexShortcut, call.FileName);

        using var doc = JsonDocument.Parse(result.Json);
        Assert.False(doc.RootElement.GetProperty("data").GetProperty("installed").GetBoolean());
    }

    [Fact]
    public async Task SmartMachineMode_StartsReadyDeviceWithoutPrompting()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true, false, false, false, false,
                "adb.exe", "scrcpy.exe",
                new[] { new RexDevice("USB123", "device", false, "", "", "Android") })
        };

        var result = await MachineMode.RunAsync(
            new[] { "smart" },
            package.Paths,
            runner,
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(runner.Calls);

        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("StartMirror", data.GetProperty("decision").GetString());
        Assert.False(data.GetProperty("interactiveRequired").GetBoolean());
    }

    [Fact]
    public async Task SmartMachineMode_ReportsWizardRequirementInsteadOfPrompting()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                false, false, false, false, false,
                "", "", Array.Empty<RexDevice>())
        };

        var result = await MachineMode.RunAsync(
            new[] { "smart" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("SetupRequired", data.GetProperty("decision").GetString());
        Assert.True(data.GetProperty("interactiveRequired").GetBoolean());
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
    [Fact]
    public async Task DisplayProbe_ReturnsStructuredTransportCapabilities()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true,
                false,
                false,
                false,
                true,
                "adb.exe",
                "scrcpy.exe",
                new[]
                {
                    new RexDevice(
                        "USB123",
                        "device",
                        false,
                        "Samsung",
                        "SM-G998B",
                        "Samsung Galaxy S21 Ultra")
                })
        };

        var result = await MachineMode.RunAsync(
            new[] { "display", "probe" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("scrcpy", data.GetProperty("currentTransport").GetString());
        Assert.Equal("reported", data.GetProperty("adbControl").GetString());

        var transports = data.GetProperty("transports");
        Assert.Equal(2, transports.GetArrayLength());
        Assert.Contains(
            transports.EnumerateArray(),
            x => x.GetProperty("id").GetString() == "scrcpy");
        Assert.Contains(
            transports.EnumerateArray(),
            x => x.GetProperty("id").GetString() == "windows-miracast");
    }

    [Fact]
    public async Task DisplayVerify_ReturnsStringEnumsAndPersistsState()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            new[] { "display", "verify", "protected", "pass" },
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);

        using var doc = JsonDocument.Parse(result.Json);
        var verification = doc.RootElement
            .GetProperty("data")
            .GetProperty("verification");

        Assert.Equal(
            "passed",
            verification.GetProperty("protectedPlayback").GetString());
    }

    [Fact]
    public async Task Capabilities_AdvertiseDisplayCommands()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            new[] { "capabilities" },
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Contains(
            data.GetProperty("commands").EnumerateArray(),
            x => x.GetString() == "display");
        Assert.True(data.GetProperty("displayCommands").GetArrayLength() >= 6);
    }

    [Fact]
    public async Task DisplayStartScrcpy_ReturnsRequestedTransport()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();

        var result = await MachineMode.RunAsync(
            new[] { "display", "start", "--transport", "scrcpy" },
            package.Paths,
            runner,
            new FakeBridgeClient(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Equal("scrcpy", data.GetProperty("transport").GetString());
        Assert.True(data.GetProperty("requested").GetBoolean());
    }

    [Fact]
    public async Task RootStatus_IsPassiveStructuredJson()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "2000", "")
        };
        var bridge = RootBridge();

        var result = await MachineMode.RunAsync(
            new[] { "root", "status", "--serial", "USB123" },
            package.Paths,
            runner,
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("root.status", root.GetProperty("command").GetString());
        var data = root.GetProperty("data");
        Assert.Equal("USB123", data.GetProperty("serial").GetString());
        Assert.Equal("suDetected", data.GetProperty("state").GetString());
        Assert.False(data.GetProperty("fromCachedVerification").GetBoolean());

        Assert.DoesNotContain(
            runner.Calls,
            call => call.Arguments.Contains("su"));
    }

    [Fact]
    public async Task RootRequest_ExplicitlyExercisesPrivilegeProbe()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "0", "")
        };
        var bridge = RootBridge();

        var result = await MachineMode.RunAsync(
            new[] { "root", "request", "--serial", "USB123" },
            package.Paths,
            runner,
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        Assert.Equal(
            "adbdRoot",
            doc.RootElement.GetProperty("data").GetProperty("state").GetString());
        Assert.True(
            doc.RootElement.GetProperty("data").GetProperty("capabilities").GetArrayLength() >= 7);
    }

    [Fact]
    public async Task RootProcesses_UsesVerifiedPerBootState()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "2000", "")
        };
        var bridge = RootBridge();
        new RootStateStore(package.Paths.RootState).Set(
            RootCache("USB123", "2000"));

        var result = await MachineMode.RunAsync(
            new[] { "root", "processes", "--serial", "USB123" },
            package.Paths,
            runner,
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal("root.processes", data.GetProperty("operation").GetString());
        Assert.Equal("readOnly", data.GetProperty("risk").GetString());
        Assert.True(data.GetProperty("sections").TryGetProperty("processes", out _));
    }

    [Fact]
    public async Task RootCriticalFilesystemPath_ReturnsMachineFailure()
    {
        using var package = new TempPackage();
        var bridge = RootBridge();

        var result = await MachineMode.RunAsync(
            new[] { "root", "files", "read", "/dev/block/by-name/userdata", "--serial", "USB123" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.Equal(1, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(
            "Root v1 blocks",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Capabilities_AdvertiseRootContractAndSafety()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            new[] { "capabilities" },
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Contains(
            data.GetProperty("commands").EnumerateArray(),
            x => x.GetString() == "root");
        Assert.True(data.GetProperty("rootCommands").GetArrayLength() >= 10);
        var safety = data.GetProperty("rootSafety");
        Assert.True(safety.GetProperty("passiveStatusDoesNotInvokeSu").GetBoolean());
        Assert.False(safety.GetProperty("rawShell").GetBoolean());
        Assert.False(safety.GetProperty("rootV1Writes").GetBoolean());
    }

    private static FakeBridgeClient RootBridge()
    {
        var device = new RexDevice(
            "USB123",
            "device",
            false,
            "Samsung",
            "SM-G998B",
            "Samsung Galaxy S21 Ultra");

        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true,
                false,
                false,
                false,
                false,
                "adb.exe",
                "scrcpy.exe",
                new[] { device })
        };
        bridge.Devices.Add(device);
        return bridge;
    }

    private static RootSessionCache RootCache(string serial, string bootId) =>
        new(
            serial,
            bootId,
            RootAccessState.Granted,
            RootProvider.Magisk,
            "30",
            new RootPrivilegeProfile(
                0,
                0,
                new[] { 0 },
                "u:r:su:s0",
                "ffff",
                "ffff",
                "ffff"),
            new[]
            {
                new RootCapability(
                    RootCapabilityIds.PrivateAppData,
                    CapabilityState.Verified,
                    PrivilegeRisk.ReadOnly,
                    "Private app data")
            },
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task RootStatus_ReturnsStructuredPassiveMachineJson()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "2000\n", "")
        };
        var bridge = RootBridge();

        var result = await MachineMode.RunAsync(
            new[] { "root", "status" },
            package.Paths,
            runner,
            bridge,
            package.Config);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("root.status", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        Assert.Equal("USB123", data.GetProperty("serial").GetString());
        Assert.Equal("suDetected", data.GetProperty("state").GetString());
        Assert.Equal(2000, data.GetProperty("adbUid").GetInt32());
        Assert.True(data.GetProperty("suVisible").GetBoolean());
    }

    [Fact]
    public async Task RootStatus_DoesNotInvokeSuInMachineMode()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "2000\n", "")
        };

        var result = await MachineMode.RunAsync(
            new[] { "root", "probe" },
            package.Paths,
            runner,
            RootBridge(),
            package.Config);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(
            runner.Calls,
            x => x.Arguments.Contains("su") && x.Arguments.Contains("-c"));
    }

    [Fact]
    public async Task Capabilities_AdvertiseRootSurfaceAndSafetyContract()
    {
        using var package = new TempPackage();

        var result = await MachineMode.RunAsync(
            new[] { "capabilities" },
            package.Paths,
            new FakeProcessRunner(),
            new FakeBridgeClient(),
            package.Config);

        using var doc = JsonDocument.Parse(result.Json);
        var data = doc.RootElement.GetProperty("data");

        Assert.Contains(
            data.GetProperty("commands").EnumerateArray(),
            x => x.GetString() == "root");

        var rootCommands = data.GetProperty("rootCommands");
        Assert.True(rootCommands.GetArrayLength() >= 10);
        Assert.Contains(
            rootCommands.EnumerateArray(),
            x => x.GetString() == "root request");

        var safety = data.GetProperty("rootSafety");
        Assert.True(safety.GetProperty("passiveStatusDoesNotInvokeSu").GetBoolean());
        Assert.False(safety.GetProperty("rawShell").GetBoolean());
        Assert.False(safety.GetProperty("rootV1Writes").GetBoolean());
    }

    [Fact]
    public async Task RootStatus_MultipleDevicesRequiresSerialInMachineMode()
    {
        using var package = new TempPackage();
        var bridge = RootBridge();
        bridge.Devices.Add(
            new RexDevice("USB456", "device", false, "Google", "Pixel", "Pixel"));

        var result = await MachineMode.RunAsync(
            new[] { "root", "status" },
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config);

        Assert.NotEqual(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.Json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains(
            "--serial",
            doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    private static FakeBridgeClient RootBridge()
    {
        var device = new RexDevice(
            "USB123",
            "device",
            false,
            "Samsung",
            "SM-G998B",
            "Samsung Galaxy S21 Ultra");

        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true,
                false,
                false,
                false,
                false,
                "adb.exe",
                "scrcpy.exe",
                new[] { device })
        };
        bridge.Devices.Add(device);
        return bridge;
    }

}
