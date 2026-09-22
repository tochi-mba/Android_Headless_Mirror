using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class DeviceCapabilityRegistryTests
{
    [Fact]
    public async Task Probe_ReportsAdbAndVisibleSu()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        shell.Success("root.probe.adb-uid", "2000");
        shell.Success("root.probe.boot-id", "boot-1");
        shell.Success("root.probe.selinux", "Enforcing");
        shell.Success("root.probe.su-path", "/system/bin/su");

        var bridge = Bridge();
        var manager = new RootManager(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            shell,
            new RootStateStore(package.Paths.RootState));

        var result = await new DeviceCapabilityRegistry(bridge, manager)
            .ProbeAsync("USB123");

        Assert.Equal(CapabilityState.Verified, result.Adb);
        Assert.Equal(
            CapabilityState.Reported,
            result.Capabilities["device.root"]);
        Assert.Equal(RootAccessState.SuDetected, result.Root!.State);
    }

    [Fact]
    public async Task Probe_CachedVerifiedRootExpandsCapabilityRegistry()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        shell.Success("root.probe.adb-uid", "2000");
        shell.Success("root.probe.boot-id", "boot-1");
        shell.Success("root.probe.selinux", "Enforcing");
        shell.Success("root.probe.su-path", "/system/bin/su");

        var store = new RootStateStore(package.Paths.RootState);
        store.Set(new RootSessionCache(
            "USB123",
            "boot-1",
            RootAccessState.Granted,
            RootProvider.Magisk,
            "30",
            new RootPrivilegeProfile(
                0, 0, new[] { 0 }, "u:r:su:s0",
                "ffff", "ffff", "ffff"),
            new[]
            {
                new RootCapability(
                    RootCapabilityIds.PrivateAppData,
                    CapabilityState.Verified,
                    PrivilegeRisk.ReadOnly,
                    "Private app data")
            },
            DateTimeOffset.UtcNow));

        var bridge = Bridge();
        var manager = new RootManager(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            shell,
            store);

        var result = await new DeviceCapabilityRegistry(bridge, manager)
            .ProbeAsync("USB123");

        Assert.Equal(
            CapabilityState.Verified,
            result.Capabilities["device.root"]);
        Assert.Equal(
            CapabilityState.Verified,
            result.Capabilities[RootCapabilityIds.PrivateAppData]);
    }

    [Fact]
    public async Task Probe_NoAuthorizedDevice_LeavesAdbUnknown()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = new RexStatus(
                true, false, false, false, false,
                "adb.exe", "scrcpy.exe", Array.Empty<RexDevice>())
        };
        var manager = new RootManager(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            new FakeAndroidShellRunner(),
            new RootStateStore(package.Paths.RootState));

        var result = await new DeviceCapabilityRegistry(bridge, manager)
            .ProbeAsync();

        Assert.Equal(CapabilityState.Unknown, result.Adb);
        Assert.Null(result.Root);
    }

    [Fact]
    public async Task Probe_UnknownRequestedSerial_DoesNotInventRootCapability()
    {
        using var package = new TempPackage();
        var bridge = Bridge();
        var manager = new RootManager(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            new FakeAndroidShellRunner(),
            new RootStateStore(package.Paths.RootState));

        var result = await new DeviceCapabilityRegistry(bridge, manager)
            .ProbeAsync("MISSING");

        Assert.Equal(CapabilityState.Unknown, result.Capabilities["device.root"]);
        Assert.Null(result.Root);
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
