using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RootStateStoreTests
{
    [Fact]
    public void MissingState_ReturnsNull()
    {
        using var package = new TempPackage();
        var store = new RootStateStore(package.Paths.RootState);

        Assert.Null(store.Get("USB123", "boot-1"));
    }

    [Fact]
    public void State_IsScopedToSerialAndBootId()
    {
        using var package = new TempPackage();
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1"));

        Assert.NotNull(store.Get("USB123", "boot-1"));
        Assert.Null(store.Get("USB123", "boot-2"));
        Assert.Null(store.Get("OTHER", "boot-1"));
    }

    [Fact]
    public void State_RoundTripsProviderAndCapabilities()
    {
        using var package = new TempPackage();
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1"));

        var reloaded = new RootStateStore(package.Paths.RootState)
            .Get("USB123", "boot-1");

        Assert.NotNull(reloaded);
        Assert.Equal(RootProvider.Magisk, reloaded.Provider);
        Assert.Equal(RootAccessState.Granted, reloaded.State);
        Assert.Single(reloaded.Capabilities);
        Assert.Equal(
            CapabilityState.Verified,
            reloaded.Capabilities[0].State);
    }

    [Fact]
    public void Remove_ClearsOnlyRequestedSerial()
    {
        using var package = new TempPackage();
        var store = new RootStateStore(package.Paths.RootState);
        store.Set(Cache("USB123", "boot-1"));
        store.Set(Cache("USB456", "boot-2"));

        store.Remove("USB123");

        Assert.Null(store.Get("USB123", "boot-1"));
        Assert.NotNull(store.Get("USB456", "boot-2"));
    }

    [Fact]
    public void InvalidJson_IsReportedClearly()
    {
        using var package = new TempPackage();
        File.WriteAllText(package.Paths.RootState, "{ broken");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new RootStateStore(package.Paths.RootState)
                .Get("USB123", "boot-1"));

        Assert.Contains("root-state.json is not valid JSON", ex.Message);
    }

    private static RootSessionCache Cache(string serial, string bootId) =>
        new(
            serial,
            bootId,
            RootAccessState.Granted,
            RootProvider.Magisk,
            "30.0",
            new RootPrivilegeProfile(
                0,
                0,
                new[] { 0 },
                "u:r:su:s0",
                "ffffffff",
                "ffffffff",
                "ffffffff"),
            new[]
            {
                new RootCapability(
                    RootCapabilityIds.PrivateAppData,
                    CapabilityState.Verified,
                    PrivilegeRisk.ReadOnly,
                    "Private app data")
            },
            DateTimeOffset.UtcNow);
}
