using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class SmartLaunchTests
{
    private static RexStatus Status(
        bool setup = true,
        bool persistentOff = false,
        bool supervisor = false,
        bool mirror = false,
        params RexDevice[] devices) =>
        new(
            setup,
            false,
            persistentOff,
            supervisor,
            mirror,
            setup ? "adb.exe" : "",
            setup ? "scrcpy.exe" : "",
            devices);

    private static RexDevice Authorized(string serial = "USB123") =>
        new(serial, "device", false, "Samsung", "S24", "Samsung S24");

    private static RexDevice Unauthorized(string serial = "LOCKED") =>
        new(serial, "unauthorized", false, "", "", "");

    [Fact]
    public void Planner_SetupIncomplete_RequiresWizard()
    {
        Assert.Equal(
            SmartLaunchDecision.SetupRequired,
            SmartLaunchPlanner.Decide(Status(setup: false)));
    }

    [Fact]
    public void Planner_PersistentOffWithAuthorizedDevice_IsExplicitStart()
    {
        Assert.Equal(
            SmartLaunchDecision.StartMirror,
            SmartLaunchPlanner.Decide(Status(
                persistentOff: true,
                devices: new[] { Authorized() })));
    }

    [Fact]
    public void Planner_PersistentOffWithoutAuthorizedDevice_StartsAndWaits()
    {
        Assert.Equal(
            SmartLaunchDecision.StartAndWaitForDevice,
            SmartLaunchPlanner.Decide(Status(
                persistentOff: true,
                devices: new[] { Unauthorized() })));
    }

    [Fact]
    public void Planner_ActiveMirror_FocusesInsteadOfStartingDuplicate()
    {
        Assert.Equal(
            SmartLaunchDecision.FocusMirror,
            SmartLaunchPlanner.Decide(Status(
                supervisor: true,
                mirror: true,
                devices: new[] { Authorized() })));
    }

    [Fact]
    public void Planner_AuthorizedDeviceWithoutMirror_StartsMirror()
    {
        Assert.Equal(
            SmartLaunchDecision.StartMirror,
            SmartLaunchPlanner.Decide(Status(
                supervisor: false,
                mirror: false,
                devices: new[] { Authorized() })));
    }

    [Fact]
    public void Planner_AuthorizedDeviceWithSupervisorButNoMirror_StillRequestsStart()
    {
        Assert.Equal(
            SmartLaunchDecision.StartMirror,
            SmartLaunchPlanner.Decide(Status(
                supervisor: true,
                mirror: false,
                devices: new[] { Authorized() })));
    }

    [Fact]
    public void Planner_NoDeviceAndNoSupervisor_StartsWaitingSupervisor()
    {
        Assert.Equal(
            SmartLaunchDecision.StartAndWaitForDevice,
            SmartLaunchPlanner.Decide(Status()));
    }

    [Fact]
    public void Planner_UnauthorizedOnly_IsNotTreatedAsReady()
    {
        Assert.Equal(
            SmartLaunchDecision.StartAndWaitForDevice,
            SmartLaunchPlanner.Decide(Status(
                devices: new[] { Unauthorized() })));
    }

    [Fact]
    public void Planner_NoAuthorizedDeviceWithExistingSupervisor_OnlyWaits()
    {
        Assert.Equal(
            SmartLaunchDecision.WaitForDevice,
            SmartLaunchPlanner.Decide(Status(
                supervisor: true,
                devices: new[] { Unauthorized() })));
    }

    [Fact]
    public void Planner_MixedDeviceStates_UsesAuthorizedDeviceReadiness()
    {
        Assert.Equal(
            SmartLaunchDecision.StartMirror,
            SmartLaunchPlanner.Decide(Status(
                devices: new[] { Unauthorized(), Authorized("USB999") })));
    }

    [Fact]
    public async Task Launcher_SetupRequired_HasNoSideEffectsAndOpensInteractiveCli()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status(setup: false)
        };

        var result = await new SmartLauncher(package.Paths, runner, bridge).RunAsync();

        Assert.Equal(SmartLaunchDecision.SetupRequired, result.Decision);
        Assert.True(result.OpenInteractiveCli);
        Assert.Empty(runner.Calls);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Launcher_StartMirror_ClearsPersistentOffAndUsesCanonicalLauncher()
    {
        using var package = new TempPackage();
        File.WriteAllText(package.Paths.StopFlag, "off");

        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status(
                persistentOff: true,
                devices: new[] { Authorized() })
        };

        var result = await new SmartLauncher(package.Paths, runner, bridge).RunAsync();

        Assert.Equal(SmartLaunchDecision.StartMirror, result.Decision);
        Assert.False(result.OpenInteractiveCli);
        Assert.False(File.Exists(package.Paths.StopFlag));

        var call = Assert.Single(runner.Calls);
        Assert.Equal("open", call.Kind);
        Assert.Equal(package.Paths.StartBatch, call.FileName);
        Assert.Equal(package.Paths.Root, call.WorkingDirectory);
    }

    [Fact]
    public async Task Launcher_ActiveMirror_FocusesWindowAndDoesNotStartAgain()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status(
                supervisor: true,
                mirror: true,
                devices: new[] { Authorized() })
        };

        var result = await new SmartLauncher(package.Paths, runner, bridge).RunAsync();

        Assert.Equal(SmartLaunchDecision.FocusMirror, result.Decision);
        Assert.False(result.OpenInteractiveCli);
        Assert.Empty(runner.Calls);

        var call = Assert.Single(bridge.Calls);
        Assert.Equal("focus-active-mirror", call.Action);
        Assert.Null(call.Serial);
    }

    [Fact]
    public async Task Launcher_NoDevice_StartsSupervisorAndReturnsInteractiveWaitingState()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status()
        };

        var result = await new SmartLauncher(package.Paths, runner, bridge).RunAsync();

        Assert.Equal(SmartLaunchDecision.StartAndWaitForDevice, result.Decision);
        Assert.True(result.OpenInteractiveCli);
        Assert.Single(runner.Calls);
        Assert.Equal(package.Paths.StartBatch, runner.Calls[0].FileName);
    }

    [Fact]
    public async Task Launcher_ExistingWaitingSupervisor_DoesNotStartDuplicate()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status(
                supervisor: true,
                devices: new[] { Unauthorized() })
        };

        var result = await new SmartLauncher(package.Paths, runner, bridge).RunAsync();

        Assert.Equal(SmartLaunchDecision.WaitForDevice, result.Decision);
        Assert.True(result.OpenInteractiveCli);
        Assert.Empty(runner.Calls);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Launcher_StartFailure_IsSurfaced()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner { OpenExitCode = 7 };
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status(devices: new[] { Authorized() })
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SmartLauncher(package.Paths, runner, bridge).RunAsync());

        Assert.Contains("Could not request", ex.Message);
    }
}
