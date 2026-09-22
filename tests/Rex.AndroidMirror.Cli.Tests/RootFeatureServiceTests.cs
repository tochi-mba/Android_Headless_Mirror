using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RootFeatureServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("data/user/0")]
    public void ValidatePath_RequiresAbsolutePath(string path)
    {
        Assert.Throws<ArgumentException>(
            () => RootFeatureService.ValidatePath(path, false));
    }

    [Theory]
    [InlineData("/dev/block")]
    [InlineData("/dev/block/by-name/userdata")]
    [InlineData("/dev/mem")]
    [InlineData("/dev/kmem")]
    [InlineData("/proc/kcore")]
    public void ValidatePath_BlocksDeviceCriticalReads(string path)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => RootFeatureService.ValidatePath(path, false));

        Assert.Contains("Root v1 blocks", ex.Message);
    }

    [Theory]
    [InlineData("/data/user/0/com.example")]
    [InlineData("/proc/123/status")]
    [InlineData("/sys/class/thermal")]
    [InlineData("/system/build.prop")]
    public void ValidatePath_AllowsNormalAbsoluteInspectionPaths(string path)
    {
        Assert.Equal(
            path,
            RootFeatureService.ValidatePath(path, false));
    }

    [Fact]
    public void ValidatePath_RejectsControlCharacters()
    {
        Assert.Throws<ArgumentException>(
            () => RootFeatureService.ValidatePath("/data/hello\nworld", false));
    }

    [Theory]
    [InlineData("com.example.app")]
    [InlineData("org.example_2.test")]
    [InlineData("com.netflix.mediaclient")]
    public void ValidatePackage_AcceptsAndroidPackageNames(string packageName)
    {
        Assert.Equal(
            packageName,
            RootFeatureService.ValidatePackage(packageName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("netflix")]
    [InlineData("com.example;reboot")]
    [InlineData("../com.example")]
    [InlineData("com.example/app")]
    public void ValidatePackage_RejectsUnsafePackageNames(string packageName)
    {
        Assert.Throws<ArgumentException>(
            () => RootFeatureService.ValidatePackage(packageName));
    }

    [Fact]
    public async Task Diagnostics_CollectsBoundedReadOnlySections()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        foreach (var id in new[]
        {
            "root.diagnostics.identity",
            "root.diagnostics.selinux",
            "root.diagnostics.kernel",
            "root.diagnostics.mounts",
            "root.diagnostics.memory",
            "root.diagnostics.cmdline",
            "root.diagnostics.release",
            "root.diagnostics.patch"
        })
        {
            shell.Success(id, id + "-value");
        }

        var result = await Features(package, shell).DiagnosticsAsync("USB123");

        Assert.Equal("root.diagnostics", result.Operation);
        Assert.Equal(PrivilegeRisk.ReadOnly, result.Risk);
        Assert.Equal(8, result.Sections.Count);
        Assert.Equal(
            "root.diagnostics.kernel-value",
            result.Sections["kernel"]);
        Assert.All(
            shell.Calls.Where(x => x.Command.Id.StartsWith("root.diagnostics", StringComparison.Ordinal)),
            call => Assert.Equal(PrivilegeRisk.ReadOnly, call.Command.Risk));
    }

    [Fact]
    public async Task ListFiles_UsesStructuredLsArguments()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.files.list", "listing");

        var result = await Features(package, shell).ListFilesAsync(
            "USB123",
            "/data/user/0/com.example");

        Assert.Equal("listing", result.Sections["listing"]);
        var call = Assert.Single(
            shell.Calls,
            x => x.Command.Id == "root.files.list");
        Assert.Equal("ls", call.Command.Executable);
        Assert.Equal(
            new[] { "-laZ", "/data/user/0/com.example" },
            call.Command.Arguments);
    }

    [Fact]
    public async Task ReadFile_IsBoundedTo256KiB()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.files.read", "hello");

        await Features(package, shell).ReadFileAsync(
            "USB123",
            "/data/user/0/com.example/file.txt");

        var call = Assert.Single(
            shell.Calls,
            x => x.Command.Id == "root.files.read");
        Assert.Equal("head", call.Command.Executable);
        Assert.Equal(262144, call.Command.MaxOutputCharacters);
        Assert.Equal("-c", call.Command.Arguments[0]);
        Assert.Equal("262144", call.Command.Arguments[1]);
    }

    [Fact]
    public async Task StatFile_UsesReadOnlyStat()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.files.stat", "file|12|600|0|0|regular file");

        var result = await Features(package, shell).StatFileAsync(
            "USB123",
            "/data/system/packages.xml");

        Assert.Contains("regular file", result.Sections["stat"]);
    }

    [Fact]
    public async Task Processes_UsesFullProcessListing()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.processes", "PID NAME");

        var result = await Features(package, shell).ProcessesAsync("USB123");

        Assert.Equal("PID NAME", result.Sections["processes"]);
        Assert.Contains(
            shell.Calls,
            x => x.Command.Id == "root.processes" &&
                 x.Command.Executable == "ps");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Process_RejectsInvalidPid(int pid)
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Features(package, shell).ProcessAsync("USB123", pid));
    }

    [Fact]
    public async Task Process_CollectsProcStatusCmdlineAndLimits()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.process.status", "Name: app");
        shell.Success("root.process.cmdline", "com.example");
        shell.Success("root.process.limits", "Max open files");

        var result = await Features(package, shell).ProcessAsync("USB123", 1234);

        Assert.Equal(3, result.Sections.Count);
        Assert.Contains("Name: app", result.Sections["status"]);
        Assert.Contains(
            shell.Calls,
            x => x.Command.Arguments.Contains("/proc/1234/status"));
    }

    [Fact]
    public async Task App_InspectsPackageAndPrivateDataWithoutWrites()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.apps.apk", "package:/data/app/base.apk");
        shell.Success("root.apps.package", "Package [com.example]");
        shell.Success("root.apps.data", "drwx------");
        shell.Success("root.apps.size", "128 /data/user/0/com.example");

        var result = await Features(package, shell).AppAsync(
            "USB123",
            "com.example");

        Assert.Equal(4, result.Sections.Count);
        Assert.Contains("base.apk", result.Sections["apk"]);
        Assert.All(
            shell.Calls.Where(x => x.Command.Id.StartsWith("root.apps.", StringComparison.Ordinal)),
            x => Assert.Equal(PrivilegeRisk.ReadOnly, x.Command.Risk));
    }

    [Fact]
    public async Task Hardware_CollectsKernelBackedTelemetry()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.hardware.cpu", "processor");
        shell.Success("root.hardware.memory", "MemTotal");
        shell.Success("root.hardware.uptime", "123");
        shell.Success("root.hardware.thermal", "thermal_zone0");
        shell.Success("root.hardware.power", "battery");

        var result = await Features(package, shell).HardwareAsync("USB123");

        Assert.Equal(5, result.Sections.Count);
        Assert.Contains("thermal_zone0", result.Sections["thermalZones"]);
    }

    [Fact]
    public async Task Network_IsInspectionOnly()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.network.interfaces", "wlan0");
        shell.Success("root.network.routes", "default");
        shell.Success("root.network.tcp", "tcp");
        shell.Success("root.network.tcp6", "tcp6");

        var result = await Features(package, shell).NetworkAsync("USB123");

        Assert.Equal(4, result.Sections.Count);
        Assert.DoesNotContain(
            shell.Calls,
            x => x.Command.Risk != PrivilegeRisk.ReadOnly);
    }

    [Fact]
    public async Task KernelLogs_UsesBoundedTail()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.logs.kernel", "kernel line");

        var result = await Features(package, shell).KernelLogsAsync("USB123");

        Assert.Contains("kernel line", result.Sections["kernel"]);
        var call = Assert.Single(shell.Calls, x => x.Command.Id == "root.logs.kernel");
        Assert.Equal(262144, call.Command.MaxOutputCharacters);
        Assert.Contains("tail -n 500", call.Command.Arguments[1]);
    }

    [Fact]
    public async Task Properties_IsReadOnly()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Success("root.properties", "[ro.product.model]: [Galaxy]");

        var result = await Features(package, shell).PropertiesAsync("USB123");

        Assert.Contains("Galaxy", result.Sections["properties"]);
    }

    [Fact]
    public async Task CommandFailure_IsRepresentedAsUnavailableSection()
    {
        using var package = new TempPackage();
        var shell = VerifiedShell();
        shell.Failure("root.processes", "permission denied");

        var result = await Features(package, shell).ProcessesAsync("USB123");

        Assert.Contains("[unavailable]", result.Sections["processes"]);
        Assert.Contains("permission denied", result.Sections["processes"]);
    }

    [Fact]
    public async Task ReadOnlyPolicyDisabled_BlocksFeatureBeforeRootCommand()
    {
        using var package = new TempPackage();
        package.Config.Set("Root.AllowReadOnly", "false");
        var shell = VerifiedShell();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Features(package, shell).ProcessesAsync("USB123"));

        Assert.Contains("policy blocks", ex.Message);
        Assert.DoesNotContain(shell.Calls, x => x.Command.Id == "root.processes");
    }

    private static RootFeatureService Features(
        TempPackage package,
        FakeAndroidShellRunner shell)
    {
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(new RootSessionCache(
            "USB123",
            "boot-1",
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
            DateTimeOffset.UtcNow));

        var manager = new RootManager(
            package.Paths,
            new FakeProcessRunner(),
            Bridge(),
            package.Config,
            shell,
            store);

        return new RootFeatureService(manager, package.Config);
    }

    private static FakeAndroidShellRunner VerifiedShell()
    {
        var shell = new FakeAndroidShellRunner();
        shell.Success("root.probe.adb-uid", "2000");
        shell.Success("root.probe.boot-id", "boot-1");
        shell.Success("root.probe.selinux", "Enforcing");
        shell.Success("root.probe.su-path", "/system/bin/su");
        return shell;
    }

    private static FakeBridgeClient Bridge()
    {
        var device = new RexDevice(
            "USB123",
            "device",
            false,
            "Samsung",
            "SM-G998B",
            "Samsung Galaxy S21 Ultra");

        return new FakeBridgeClient
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
    }
}
