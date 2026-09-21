using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class ProgramTests
{
    [Theory]
    [InlineData("system", "system")]
    [InlineData("SECURE", "secure")]
    [InlineData("Global", "global")]
    public void NormalizeNamespace_AcceptsKnownNamespaces(string input, string expected)
    {
        Assert.Equal(expected, Program.NormalizeNamespace(input));
    }

    [Fact]
    public void NormalizeNamespace_RejectsUnknownNamespace()
    {
        var ex = Assert.Throws<ArgumentException>(() => Program.NormalizeNamespace("vendor"));
        Assert.Contains("system, secure, or global", ex.Message);
    }

    [Fact]
    public void GetOption_IsCaseInsensitive()
    {
        var args = new[] { "action", "sleep", "--SERIAL", "USB123" };
        Assert.Equal("USB123", Program.GetOption(args, "--serial"));
    }

    public static TheoryData<string[], string[]> MachineArgumentCases => new()
    {
        { new[] { "agent", "status" }, new[] { "status" } },
        { new[] { "--json", "status" }, new[] { "status" } },
        { new[] { "status", "--json" }, new[] { "status" } },
        { new[] { "--plain", "status" }, new[] { "status" } },
        {
            new[] { "agent", "--json", "android", "list", "global" },
            new[] { "android", "list", "global" }
        },
    };

    [Theory]
    [MemberData(nameof(MachineArgumentCases))]
    public void GetMachineArgs_NormalizesAgentAndMachineFlags(string[] input, string[] expected)
    {
        Assert.Equal(expected, Program.GetMachineArgs(input));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("help")]
    [InlineData("smart")]
    public void GetMachineArgs_ReturnsNullForNormalHumanCli(string command)
    {
        Assert.Null(Program.GetMachineArgs(new[] { command }));
    }

    [Fact]
    public void GetMachineArgs_AllowsCapabilityDiscoveryWithoutSubcommand()
    {
        Assert.Empty(Program.GetMachineArgs(new[] { "agent" })!);
        Assert.Empty(Program.GetMachineArgs(new[] { "--json" })!);
    }

    [Fact]
    public async Task ResolveSerial_UsesExplicitSerialWithoutDeviceLookup()
    {
        var bridge = new FakeBridgeClient();

        var serial = await Program.ResolveSerialAsync(bridge, "EXPLICIT");

        Assert.Equal("EXPLICIT", serial);
    }

    [Fact]
    public async Task ResolveSerial_AutomaticallyUsesSingleAuthorizedDevice()
    {
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "Samsung", "S24", "Samsung S24"));
        bridge.Devices.Add(new RexDevice("BAD", "unauthorized", false, "", "", ""));

        Assert.Equal("USB123", await Program.ResolveSerialAsync(bridge, null));
    }

    [Fact]
    public async Task ResolveSerial_RejectsNoAuthorizedDevice()
    {
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("LOCKED", "unauthorized", false, "", "", ""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Program.ResolveSerialAsync(bridge, null));

        Assert.Contains("No authorized Android device", ex.Message);
    }

    [Fact]
    public async Task ResolveSerial_RequiresExplicitChoiceForMultipleAuthorizedDevices()
    {
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("A", "device", false, "", "", ""));
        bridge.Devices.Add(new RexDevice("B", "device", false, "", "", ""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Program.ResolveSerialAsync(bridge, null));

        Assert.Contains("--serial", ex.Message);
    }

    [Fact]
    public async Task Start_RemovesPersistentStopAndUsesLauncher()
    {
        using var package = new TempPackage();
        File.WriteAllText(package.Paths.StopFlag, "off");
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient();

        var code = await Program.RunCommandAsync(
            new[] { "start" }, package.Paths, runner, bridge, package.Config);

        Assert.Equal(0, code);
        Assert.False(File.Exists(package.Paths.StopFlag));
        var call = Assert.Single(runner.Calls);
        Assert.Equal("open", call.Kind);
        Assert.Equal(package.Paths.StartBatch, call.FileName);
    }

    [Fact]
    public async Task SetupSkipAutostart_PassesSwitchToPowerShell()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient();

        var code = await Program.RunCommandAsync(
            new[] { "setup", "--skip-autostart" },
            package.Paths, runner, bridge, package.Config);

        Assert.Equal(0, code);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("powershell", call.Kind);
        Assert.Contains("-SkipAutostart", call.Arguments);
    }

    [Fact]
    public async Task Controls_LaunchesDetachedControlCenter()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "controls" },
            package.Paths, runner, bridge, package.Config);

        Assert.Equal(0, code);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("detached", call.Kind);
        Assert.Equal("powershell.exe", call.FileName);
        Assert.Contains(package.Paths.ControlCenter, call.Arguments);
        Assert.Contains("USB123", call.Arguments);
    }

    [Fact]
    public async Task Action_DispatchesNamedScrcpyAction()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "action", "sleep" },
            package.Paths, runner, bridge, package.Config);

        Assert.Equal(0, code);
        var call = Assert.Single(bridge.Calls);
        Assert.Equal("scrcpy-action", call.Action);
        Assert.Equal("USB123", call.Serial);
        Assert.Equal("sleep", call.Name);
    }

    [Fact]
    public async Task MirrorZoom_DispatchesHostOnlyMirrorCommand()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "mirror", "zoom-in" },
            package.Paths, new FakeProcessRunner(), bridge, package.Config);

        Assert.Equal(0, code);
        var call = Assert.Single(bridge.Calls);
        Assert.Equal("mirror-command", call.Action);
        Assert.Equal("USB123", call.Serial);
        Assert.Equal("zoom-in", call.Name);
    }

    [Theory]
    [InlineData("zoom-in")]
    [InlineData("zoom-out")]
    [InlineData("reset-zoom")]
    public async Task MirrorZoom_AllSupportedCommandsAreAccepted(string command)
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "mirror", command },
            package.Paths, new FakeProcessRunner(), bridge, package.Config);

        Assert.Equal(0, code);
        Assert.Equal(command, Assert.Single(bridge.Calls).Name);
    }

    [Fact]
    public async Task DeviceAnimationScale_UpdatesAllThreeAndroidScales()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "device", "set", "animation-scale", "0.5" },
            package.Paths, new FakeProcessRunner(), bridge, package.Config);

        Assert.Equal(0, code);
        Assert.Equal(
            new[] { "animation-window", "animation-transition", "animation-duration" },
            bridge.Calls.Select(x => x.Name).ToArray());
        Assert.All(bridge.Calls, x => Assert.Equal("0.5", x.Value));
    }

    [Fact]
    public async Task DeviceSet_PreservesFriendlySettingNameAndValue()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "device", "set", "brightness", "200" },
            package.Paths, new FakeProcessRunner(), bridge, package.Config);

        Assert.Equal(0, code);
        var call = Assert.Single(bridge.Calls);
        Assert.Equal("friendly-set", call.Action);
        Assert.Equal("brightness", call.Name);
        Assert.Equal("200", call.Value);
    }

    [Fact]
    public async Task ConfigSet_ChangesTypedConfigAndCreatesBackup()
    {
        using var package = new TempPackage();

        var code = await Program.RunCommandAsync(
            new[] { "config", "set", "MaxFps", "90" },
            package.Paths, new FakeProcessRunner(), new FakeBridgeClient(), package.Config);

        Assert.Equal(0, code);
        Assert.Equal("90", package.Config.Get("MaxFps").Value);
        Assert.True(File.Exists(package.Config.BackupPath));
    }

    [Fact]
    public async Task LockMode_RejectsUnknownModeBeforeBridgeCall()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Program.RunCommandAsync(
                new[] { "lock-mode", "USB123", "face-id" },
                package.Paths, new FakeProcessRunner(), bridge, package.Config));

        Assert.Contains("pattern, other, or none", ex.Message);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Screenshot_UsesSingleAuthorizedDevice()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("USB123", "device", false, "", "", "Android"));

        var code = await Program.RunCommandAsync(
            new[] { "screenshot" },
            package.Paths, new FakeProcessRunner(), bridge, package.Config);

        Assert.Equal(0, code);
        var call = Assert.Single(bridge.Calls);
        Assert.Equal("screenshot", call.Action);
        Assert.Equal("USB123", call.Serial);
    }
}
