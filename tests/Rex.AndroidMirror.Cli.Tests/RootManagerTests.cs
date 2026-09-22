using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RootManagerTests
{
    [Fact]
    public async Task PassiveProbe_DoesNotInvokeSu()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");

        var result = await Manager(package, shell).ProbePassiveAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.SuDetected, result.State);
        Assert.True(result.SuVisible);
        Assert.DoesNotContain(
            shell.Calls,
            call => call.RootMode == RootExecutionMode.Su);
        Assert.DoesNotContain(
            shell.Calls,
            call => call.Command.Id == "root.request");
    }

    [Fact]
    public async Task PassiveProbe_NoVisibleSu_IsUnavailable()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "");

        var result = await Manager(package, shell).ProbePassiveAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Unavailable, result.State);
        Assert.False(result.SuVisible);
    }

    [Fact]
    public async Task PassiveProbe_AdbdUidZero_IsReportedWithoutSu()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "", adbUid: 0);

        var result = await Manager(package, shell).ProbePassiveAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.AdbdRoot, result.State);
        Assert.Equal(0, result.EffectiveUid);
    }

    [Fact]
    public async Task PassiveProbe_UsesSameBootCachedVerification()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1", RootProvider.KernelSU));

        var result = await Manager(package, shell, store).ProbePassiveAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Granted, result.State);
        Assert.Equal(RootProvider.KernelSU, result.Provider);
        Assert.True(result.FromCachedVerification);
    }

    [Fact]
    public async Task PassiveProbe_IgnoresCacheFromPreviousBoot()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su", bootId: "boot-2");
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1", RootProvider.Magisk));

        var result = await Manager(package, shell, store).ProbePassiveAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.SuDetected, result.State);
        Assert.False(result.FromCachedVerification);
    }

    [Fact]
    public async Task Request_NoVisibleSu_DoesNotAttemptElevation()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "");

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Unavailable, result.State);
        Assert.DoesNotContain(shell.Calls, x => x.Command.Id == "root.request");
    }

    [Fact]
    public async Task Request_Timeout_BecomesAuthorizationPending()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        shell.Timeout("root.request");

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.AuthorizationPending, result.State);
        Assert.Contains("superuser prompt", result.Message);
    }

    [Fact]
    public async Task Request_Denial_IsReported()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        shell.Failure("root.request", "permission denied");

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Denied, result.State);
        Assert.Contains("permission denied", result.Message);
    }

    [Theory]
    [InlineData("MAGISKSU 30.1", RootProvider.Magisk)]
    [InlineData("KernelSU 1.0", RootProvider.KernelSU)]
    [InlineData("APatch 11000", RootProvider.APatch)]
    public async Task Request_IdentifiesKnownSuProvider(
        string version,
        RootProvider provider)
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        SeedGranted(shell, version);

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Granted, result.State);
        Assert.Equal(provider, result.Provider);
        Assert.Equal(0, result.EffectiveUid);
        Assert.False(result.FromCachedVerification);
        Assert.All(
            result.Capabilities,
            capability => Assert.Equal(
                CapabilityState.Verified,
                capability.State));
    }

    [Fact]
    public async Task Request_UnknownProvider_RemainsUsable()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        SeedGranted(shell, "SuperUser 1.0");
        shell.Failure("root.provider.test");
        shell.Failure("root.provider.test");
        shell.Failure("root.provider.test");
        shell.Failure("root.provider.command");

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Granted, result.State);
        Assert.Equal(RootProvider.Other, result.Provider);
    }

    [Fact]
    public async Task Request_NonZeroEffectiveUid_IsRestricted()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        SeedGranted(shell, "KernelSU", effectiveUid: 2000);

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.GrantedRestricted, result.State);
        Assert.Equal(2000, result.EffectiveUid);
    }

    [Fact]
    public async Task Request_MissingPrivateDataCapability_IsRestricted()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        SeedGranted(shell, "KernelSU", privateData: false);

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.GrantedRestricted, result.State);
        Assert.Contains(
            result.Capabilities,
            x => x.Id == RootCapabilityIds.PrivateAppData &&
                 x.State == CapabilityState.Unsupported);
    }

    [Fact]
    public async Task Request_PersistsVerificationForCurrentBoot()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        SeedGranted(shell, "MAGISKSU 30");
        var store = new RootStateStore(package.Paths.RootState);
        var manager = Manager(package, shell, store);

        await manager.RequestAsync("USB123", TestContext.Current.CancellationToken);

        var cached = store.Get("USB123", "boot-1");
        Assert.NotNull(cached);
        Assert.Equal(RootProvider.Magisk, cached.Provider);
    }

    [Fact]
    public async Task Request_AdbdRoot_UsesNormalShellForCapabilities()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "", adbUid: 0);
        SeedProfileAndCapabilities(shell);

        var result = await Manager(package, shell).RequestAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.AdbdRoot, result.State);
        Assert.DoesNotContain(
            shell.Calls,
            call => call.RootMode == RootExecutionMode.Su);
        Assert.Contains(
            shell.Calls,
            call => call.Command.Id == "root.cap.private-app-data" &&
                    call.RootMode == RootExecutionMode.AdbdRoot);
    }

    [Fact]
    public async Task ExecuteVerified_RequiresPriorVerification()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Manager(package, shell).ExecuteVerifiedAsync(
                "USB123",
                new PrivilegedCommand(
                    "root.test",
                    "id",
                    Array.Empty<string>()),
                TestContext.Current.CancellationToken));

        Assert.Contains("root request", ex.Message);
    }

    [Fact]
    public async Task ExecuteVerified_UsesCachedSuSession()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        shell.Success("root.test", "hello");
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1", RootProvider.Magisk));

        var result = await Manager(package, shell, store).ExecuteVerifiedAsync(
            "USB123",
            new PrivilegedCommand(
                "root.test",
                "echo",
                new[] { "hello" }),
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Contains(
            shell.Calls,
            x => x.Command.Id == "root.test" &&
                 x.RootMode == RootExecutionMode.Su);
    }

    [Fact]
    public async Task ExecuteVerified_TimeoutInvalidatesCachedSession()
    {
        using var package = new TempPackage();
        var shell = new FakeAndroidShellRunner();
        SeedPassive(shell, suPath: "/system/bin/su");
        shell.Timeout("root.test");
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1", RootProvider.Magisk));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Manager(package, shell, store).ExecuteVerifiedAsync(
                "USB123",
                new PrivilegedCommand(
                    "root.test",
                    "id",
                    Array.Empty<string>()),
                TestContext.Current.CancellationToken));

        Assert.Contains("authorization may have changed", ex.Message);
        Assert.Null(store.Get("USB123", "boot-1"));
    }

    [Fact]
    public async Task DisabledRoot_PerformsNoAdbProbe()
    {
        using var package = new TempPackage();
        package.Config.Set("Root.Enabled", "false");
        var shell = new FakeAndroidShellRunner();

        var result = await Manager(package, shell).ProbePassiveAsync("USB123", TestContext.Current.CancellationToken);

        Assert.Equal(RootAccessState.Unavailable, result.State);
        Assert.Empty(shell.Calls);
    }

    private static RootManager Manager(
        TempPackage package,
        FakeAndroidShellRunner shell,
        RootStateStore? store = null) =>
        new(
            package.Paths,
            new FakeProcessRunner(),
            Bridge(),
            package.Config,
            shell,
            store ?? new RootStateStore(package.Paths.RootState));

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

    private static void SeedPassive(
        FakeAndroidShellRunner shell,
        string suPath,
        int adbUid = 2000,
        string bootId = "boot-1")
    {
        shell.Success("root.probe.adb-uid", adbUid.ToString());
        shell.Success("root.probe.boot-id", bootId);
        shell.Success("root.probe.selinux", "Enforcing");
        shell.Success("root.probe.su-path", suPath);
    }

    private static void SeedGranted(
        FakeAndroidShellRunner shell,
        string providerVersion,
        int effectiveUid = 0,
        bool privateData = true)
    {
        shell.Success("root.request", effectiveUid.ToString());
        SeedProfileAndCapabilities(shell, privateData);
        shell.Success("root.provider.su-version", providerVersion);
    }

    private static void SeedProfileAndCapabilities(
        FakeAndroidShellRunner shell,
        bool privateData = true)
    {
        shell.Success("root.profile.gid", "0");
        shell.Success("root.profile.groups", "0 1000 2000");
        shell.Success(
            "root.profile.id",
            "uid=0(root) gid=0(root) groups=0(root),1000(system) context=u:r:su:s0");
        shell.Success(
            "root.profile.proc-status",
            "Name:\tsh\nCapPrm:\t000001ffffffffff\nCapEff:\t000001ffffffffff\nCapBnd:\t000001ffffffffff\n");

        if (privateData)
            shell.Success("root.cap.private-app-data");
        else
            shell.Failure("root.cap.private-app-data", "permission denied");

        shell.Success("root.cap.process-inspection");
        shell.Success("root.cap.kernel-logs");
        shell.Success("root.cap.system-files");
        shell.Success("root.cap.hardware");
        shell.Success("root.cap.network");
        shell.Success("root.cap.properties");
    }

    private static RootSessionCache Cache(
        string serial,
        string bootId,
        RootProvider provider) =>
        new(
            serial,
            bootId,
            RootAccessState.Granted,
            provider,
            provider.ToString(),
            new RootPrivilegeProfile(
                0,
                0,
                new[] { 0, 1000 },
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
                    "Private app data"),
                new RootCapability(
                    RootCapabilityIds.ProcessInspection,
                    CapabilityState.Verified,
                    PrivilegeRisk.ReadOnly,
                    "Processes")
            },
            DateTimeOffset.UtcNow);
}
