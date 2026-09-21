using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class DisplayManagerTests
{
    [Fact]
    public async Task Probe_ReportsScrcpyAndIndependentAdbControl()
    {
        using var package = new TempPackage();
        var bridge = SamsungBridge(mirrorRunning: true);
        var host = new FakeDisplayHostProbe(
            new DisplayHostCapabilities(
                CapabilityState.Reported,
                CapabilityState.Reported,
                "ready"));

        var manager = new DisplayManager(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            host,
            new DisplayVerificationStore(package.Paths.DisplayVerification));

        var result = await manager.ProbeAsync();

        Assert.Equal(DisplayTransportIds.Scrcpy, result.CurrentTransport);
        Assert.Equal(CapabilityState.Reported, result.AdbControl);
        Assert.Equal(CapabilityState.Reported, result.SamsungDexCandidate);

        var scrcpy = Assert.Single(result.Transports, x => x.Id == DisplayTransportIds.Scrcpy);
        Assert.Equal(CapabilityState.Reported, scrcpy.Availability);
        Assert.Equal(CapabilityState.Unsupported, scrcpy.ProtectedOutput);
        Assert.Equal(VerificationOutcome.Failed, scrcpy.ProtectedPlaybackVerification);
    }

    [Fact]
    public async Task Probe_WirelessProtectedPlaybackStartsUnknown()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Reported,
            CapabilityState.Reported);

        var result = await manager.ProbeAsync();
        var wireless = Assert.Single(
            result.Transports,
            x => x.Id == DisplayTransportIds.WindowsMiracast);

        Assert.Equal(CapabilityState.Reported, wireless.Availability);
        Assert.Equal(CapabilityState.Unknown, wireless.ProtectedOutput);
        Assert.Equal(VerificationOutcome.Unknown, wireless.ProtectedPlaybackVerification);
    }

    [Fact]
    public async Task VerifyProtectedPass_PromotesProtectedOutputToVerified()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Reported,
            CapabilityState.Reported);

        manager.Verify(
            DisplayTransportIds.WindowsMiracast,
            "protected",
            "pass",
            "manual Netflix test");

        var result = await manager.ProbeAsync();
        var wireless = Assert.Single(
            result.Transports,
            x => x.Id == DisplayTransportIds.WindowsMiracast);

        Assert.Equal(CapabilityState.Verified, wireless.ProtectedOutput);
        Assert.Equal(VerificationOutcome.Passed, wireless.ProtectedPlaybackVerification);
        Assert.True(File.Exists(package.Paths.DisplayVerification));
    }

    [Fact]
    public async Task VerifyProtectedFailure_ReportsUnsupportedForCurrentVerifiedPath()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Reported,
            CapabilityState.Reported);

        manager.Verify(DisplayTransportIds.WindowsMiracast, "protected", "fail");

        var result = await manager.ProbeAsync();
        var wireless = Assert.Single(
            result.Transports,
            x => x.Id == DisplayTransportIds.WindowsMiracast);

        Assert.Equal(CapabilityState.Unsupported, wireless.ProtectedOutput);
        Assert.Equal(VerificationOutcome.Failed, wireless.ProtectedPlaybackVerification);
    }

    [Fact]
    public async Task VerifyNormalPass_PromotesWirelessVideoToVerified()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Reported,
            CapabilityState.Reported);

        manager.Verify(DisplayTransportIds.WindowsMiracast, "normal", "pass");

        var result = await manager.ProbeAsync();
        var wireless = Assert.Single(
            result.Transports,
            x => x.Id == DisplayTransportIds.WindowsMiracast);

        Assert.Equal(CapabilityState.Verified, wireless.Video);
        Assert.Equal(VerificationOutcome.Passed, wireless.NormalPlaybackVerification);
    }

    [Fact]
    public void VerifyClear_RemovesStoredVerification()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Reported,
            CapabilityState.Reported);

        manager.Verify(DisplayTransportIds.WindowsMiracast, "protected", "pass");
        var cleared = manager.Verify(
            DisplayTransportIds.WindowsMiracast,
            "protected",
            "clear");

        Assert.Equal(VerificationOutcome.Unknown, cleared.NormalPlayback);
        Assert.Equal(VerificationOutcome.Unknown, cleared.ProtectedPlayback);
    }

    [Theory]
    [InlineData("scrcpy", DisplayTransportIds.Scrcpy)]
    [InlineData("SCRCPY", DisplayTransportIds.Scrcpy)]
    [InlineData("miracast", DisplayTransportIds.WindowsMiracast)]
    [InlineData("wireless-display", DisplayTransportIds.WindowsMiracast)]
    [InlineData("windows-miracast", DisplayTransportIds.WindowsMiracast)]
    public void NormalizeTransport_AcceptsSupportedAliases(string input, string expected)
    {
        Assert.Equal(expected, DisplayManager.NormalizeTransport(input));
    }

    [Fact]
    public void NormalizeTransport_RejectsUnknownValue()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DisplayManager.NormalizeTransport("hdmi-capture"));

        Assert.Contains("scrcpy or windows-miracast", ex.Message);
    }

    [Fact]
    public async Task StartScrcpy_RemovesPersistentStopAndUsesExistingLauncher()
    {
        using var package = new TempPackage();
        File.WriteAllText(package.Paths.StopFlag, "off");
        var runner = new FakeProcessRunner();
        var manager = new DisplayManager(
            package.Paths,
            runner,
            SamsungBridge(),
            package.Config,
            new FakeDisplayHostProbe(DefaultHost()),
            new DisplayVerificationStore(package.Paths.DisplayVerification));

        var result = await manager.StartAsync(DisplayTransportIds.Scrcpy);

        Assert.True(result.Requested);
        Assert.False(File.Exists(package.Paths.StopFlag));
        var call = Assert.Single(runner.Calls);
        Assert.Equal("open", call.Kind);
        Assert.Equal(package.Paths.StartBatch, call.FileName);
    }

    [Fact]
    public async Task StartWireless_OpensWindowsReceiverSetupWithoutStartingScrcpy()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var host = new FakeDisplayHostProbe(DefaultHost());
        var manager = new DisplayManager(
            package.Paths,
            runner,
            SamsungBridge(),
            package.Config,
            host,
            new DisplayVerificationStore(package.Paths.DisplayVerification));

        var result = await manager.StartAsync(DisplayTransportIds.WindowsMiracast);

        Assert.True(result.Requested);
        Assert.Equal(1, host.OpenCount);
        Assert.Empty(runner.Calls);
        Assert.Contains("Smart View or Wireless DeX", result.Message);
    }

    [Fact]
    public async Task StartWireless_PropagatesReceiverOpenFailure()
    {
        using var package = new TempPackage();
        var host = new FakeDisplayHostProbe(DefaultHost()) { OpenExitCode = 9 };
        var manager = new DisplayManager(
            package.Paths,
            new FakeProcessRunner(),
            SamsungBridge(),
            package.Config,
            host,
            new DisplayVerificationStore(package.Paths.DisplayVerification));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.StartAsync(DisplayTransportIds.WindowsMiracast));

        Assert.Contains("Projecting to this PC", ex.Message);
    }

    [Fact]
    public async Task Probe_WirelessIsUnsupportedWhenOptionalFeatureIsMissing()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Unsupported,
            CapabilityState.Reported);

        var result = await manager.ProbeAsync();
        var wireless = Assert.Single(
            result.Transports,
            x => x.Id == DisplayTransportIds.WindowsMiracast);

        Assert.Equal(CapabilityState.Unsupported, wireless.Availability);
    }

    [Fact]
    public async Task Probe_WirelessIsUnknownWhenHostProbeIsInconclusive()
    {
        using var package = new TempPackage();
        var manager = Manager(
            package,
            SamsungBridge(),
            CapabilityState.Unknown,
            CapabilityState.Reported);

        var result = await manager.ProbeAsync();
        var wireless = Assert.Single(
            result.Transports,
            x => x.Id == DisplayTransportIds.WindowsMiracast);

        Assert.Equal(CapabilityState.Unknown, wireless.Availability);
    }

    [Fact]
    public async Task Probe_NonSamsungDeviceDoesNotClaimDex()
    {
        using var package = new TempPackage();
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
                new[]
                {
                    new RexDevice(
                        "USB1",
                        "device",
                        false,
                        "Google",
                        "Pixel",
                        "Pixel")
                })
        };

        var result = await Manager(
            package,
            bridge,
            CapabilityState.Reported,
            CapabilityState.Reported).ProbeAsync();

        Assert.Equal(CapabilityState.Unsupported, result.SamsungDexCandidate);
    }

    [Fact]
    public async Task Probe_NoAuthorizedDeviceLeavesDexAndAdbUnknown()
    {
        using var package = new TempPackage();
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
                new[]
                {
                    new RexDevice(
                        "USB1",
                        "unauthorized",
                        false,
                        "Samsung",
                        "SM-G998B",
                        "Samsung Galaxy")
                })
        };

        var result = await Manager(
            package,
            bridge,
            CapabilityState.Reported,
            CapabilityState.Reported).ProbeAsync();

        Assert.Equal(CapabilityState.Unknown, result.SamsungDexCandidate);
        Assert.Equal(CapabilityState.Unknown, result.AdbControl);
    }

    private static DisplayManager Manager(
        TempPackage package,
        FakeBridgeClient bridge,
        CapabilityState feature,
        CapabilityState receive) =>
        new(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            new FakeDisplayHostProbe(
                new DisplayHostCapabilities(feature, receive, "test")),
            new DisplayVerificationStore(package.Paths.DisplayVerification));

    private static FakeBridgeClient SamsungBridge(bool mirrorRunning = false) =>
        new()
        {
            DefaultStatus = new RexStatus(
                true,
                false,
                false,
                false,
                mirrorRunning,
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

    private static DisplayHostCapabilities DefaultHost() =>
        new(CapabilityState.Reported, CapabilityState.Reported, "ready");
}

internal sealed class FakeDisplayHostProbe : IDisplayHostProbe
{
    private readonly DisplayHostCapabilities _capabilities;

    public int OpenCount { get; private set; }
    public int OpenExitCode { get; set; }

    public FakeDisplayHostProbe(DisplayHostCapabilities capabilities) =>
        _capabilities = capabilities;

    public Task<DisplayHostCapabilities> ProbeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_capabilities);

    public Task<int> OpenReceiverSetupAsync(
        CancellationToken cancellationToken = default)
    {
        OpenCount++;
        return Task.FromResult(OpenExitCode);
    }
}
