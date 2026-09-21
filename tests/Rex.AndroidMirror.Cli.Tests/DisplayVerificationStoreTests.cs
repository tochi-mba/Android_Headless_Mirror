using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class DisplayVerificationStoreTests
{
    [Fact]
    public void MissingFile_ReturnsUnknownVerification()
    {
        using var package = new TempPackage();
        var store = new DisplayVerificationStore(package.Paths.DisplayVerification);

        var result = store.Get(DisplayTransportIds.WindowsMiracast);

        Assert.Equal(VerificationOutcome.Unknown, result.NormalPlayback);
        Assert.Equal(VerificationOutcome.Unknown, result.ProtectedPlayback);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public void Set_PersistsNormalAndProtectedIndependently()
    {
        using var package = new TempPackage();
        var store = new DisplayVerificationStore(package.Paths.DisplayVerification);

        store.Set(DisplayTransportIds.WindowsMiracast, "normal", VerificationOutcome.Passed);
        store.Set(DisplayTransportIds.WindowsMiracast, "protected", VerificationOutcome.Failed);

        var reloaded = new DisplayVerificationStore(package.Paths.DisplayVerification)
            .Get(DisplayTransportIds.WindowsMiracast);

        Assert.Equal(VerificationOutcome.Passed, reloaded.NormalPlayback);
        Assert.Equal(VerificationOutcome.Failed, reloaded.ProtectedPlayback);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public void Set_PreservesNote()
    {
        using var package = new TempPackage();
        var store = new DisplayVerificationStore(package.Paths.DisplayVerification);

        store.Set(
            DisplayTransportIds.WindowsMiracast,
            "protected",
            VerificationOutcome.Passed,
            "S21 + laptop manual test");

        Assert.Equal(
            "S21 + laptop manual test",
            store.Get(DisplayTransportIds.WindowsMiracast).Note);
    }

    [Fact]
    public void Set_RejectsUnknownTarget()
    {
        using var package = new TempPackage();
        var store = new DisplayVerificationStore(package.Paths.DisplayVerification);

        Assert.Throws<ArgumentException>(() =>
            store.Set(
                DisplayTransportIds.WindowsMiracast,
                "latency",
                VerificationOutcome.Passed));
    }

    [Fact]
    public void InvalidJson_IsReportedClearly()
    {
        using var package = new TempPackage();
        File.WriteAllText(package.Paths.DisplayVerification, "{ definitely-not-json");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DisplayVerificationStore(package.Paths.DisplayVerification)
                .Get(DisplayTransportIds.WindowsMiracast));

        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Clear_RemovesPersistedState()
    {
        using var package = new TempPackage();
        var store = new DisplayVerificationStore(package.Paths.DisplayVerification);
        store.Set(
            DisplayTransportIds.WindowsMiracast,
            "protected",
            VerificationOutcome.Passed);

        store.Clear(DisplayTransportIds.WindowsMiracast);

        var result = store.Get(DisplayTransportIds.WindowsMiracast);
        Assert.Equal(VerificationOutcome.Unknown, result.ProtectedPlayback);
    }
}
