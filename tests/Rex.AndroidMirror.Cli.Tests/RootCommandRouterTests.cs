using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RootCommandRouterTests
{
    [Fact]
    public async Task Status_UsesPassiveProbeAndDoesNotRequestRoot()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "2000\n", "")
        };
        var bridge = Bridge();

        var response = await RootCommandRouter.RunAsync(
            new[] { "root", "status" },
            package.Paths,
            runner,
            bridge,
            package.Config,
            TestContext.Current.CancellationToken);

        Assert.Equal("root.status", response.Command);
        var status = Assert.IsType<RootStatus>(response.Data);
        Assert.Equal(RootAccessState.SuDetected, status.State);

        Assert.DoesNotContain(
            runner.Calls,
            x => x.Arguments.Contains("su") &&
                 x.Arguments.Contains("-c"));
    }

    [Fact]
    public async Task Clear_RemovesOnlyRexCachedVerification()
    {
        using var package = new TempPackage();
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(new RootSessionCache(
            "USB123",
            "boot",
            RootAccessState.Granted,
            RootProvider.Magisk,
            "30",
            null,
            Array.Empty<RootCapability>(),
            DateTimeOffset.UtcNow));

        var response = await RootCommandRouter.RunAsync(
            new[] { "root", "clear" },
            package.Paths,
            new FakeProcessRunner(),
            Bridge(),
            package.Config,
            TestContext.Current.CancellationToken);

        Assert.Equal("root.clear", response.Command);
        Assert.Null(store.Get("USB123", "boot"));
    }

    [Fact]
    public async Task Files_RequiresSubcommandAndAbsolutePath()
    {
        using var package = new TempPackage();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            RootCommandRouter.RunAsync(
                new[] { "root", "files", "list" },
                package.Paths,
                new FakeProcessRunner(),
                Bridge(),
                package.Config,
                TestContext.Current.CancellationToken));

        Assert.Contains("root files", ex.Message);
    }

    [Fact]
    public async Task MultipleDevices_RequireExplicitSerial()
    {
        using var package = new TempPackage();
        var bridge = Bridge();
        bridge.Devices.Add(new RexDevice(
            "USB456", "device", false, "Google", "Pixel", "Pixel"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RootCommandRouter.RunAsync(
                new[] { "root", "status" },
                package.Paths,
                new FakeProcessRunner(),
                bridge,
                package.Config,
                TestContext.Current.CancellationToken));

        Assert.Contains("--serial", ex.Message);
    }

    [Fact]
    public async Task ExplicitSerial_WorksWithMultipleDevices()
    {
        using var package = new TempPackage();
        var bridge = Bridge();
        var other = new RexDevice(
            "USB456", "device", false, "Google", "Pixel", "Pixel");
        bridge.Devices.Add(other);
        bridge.DefaultStatus = bridge.DefaultStatus with
        {
            Devices = bridge.DefaultStatus.Devices.Concat(new[] { other }).ToArray()
        };

        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "2000\n", "")
        };

        var response = await RootCommandRouter.RunAsync(
            new[] { "root", "status", "--serial", "USB456" },
            package.Paths,
            runner,
            bridge,
            package.Config,
            TestContext.Current.CancellationToken);

        Assert.Equal("USB456", Assert.IsType<RootStatus>(response.Data).Serial);
    }

    private static FakeBridgeClient Bridge()
    {
        var device = new RexDevice(
            "USB123", "device", false, "Samsung", "SM-G998B", "Samsung Galaxy S21 Ultra");
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true, false, false, false, false,
                "adb.exe", "scrcpy.exe", new[] { device })
        };
        bridge.Devices.Add(device);
        return bridge;
    }
}
