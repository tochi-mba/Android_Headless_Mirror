using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RootPolicyTests
{
    [Fact]
    public void Defaults_AllowOnlyReadOnlyPrivilegedOperations()
    {
        using var package = new TempPackage();
        var policy = new RootPolicy(package.Config);

        Assert.True(policy.Enabled);
        Assert.True(policy.AllowReadOnly);
        Assert.False(policy.AllowReversible);
        Assert.False(policy.AllowSystemChanges);
        Assert.False(policy.AllowDeviceCritical);
        Assert.False(policy.RawShellEnabled);
        Assert.False(policy.RequestAutomatically);
    }

    [Theory]
    [InlineData(PrivilegeRisk.Reversible)]
    [InlineData(PrivilegeRisk.SystemChanging)]
    [InlineData(PrivilegeRisk.DeviceCritical)]
    public void EnsureAllowed_BlocksWriteRiskByDefault(PrivilegeRisk risk)
    {
        using var package = new TempPackage();
        var policy = new RootPolicy(package.Config);

        var ex = Assert.Throws<InvalidOperationException>(
            () => policy.EnsureAllowed(risk));

        Assert.Contains("policy blocks", ex.Message);
    }

    [Fact]
    public void EnsureAllowed_AllowsReadOnlyByDefault()
    {
        using var package = new TempPackage();
        new RootPolicy(package.Config).EnsureAllowed(PrivilegeRisk.ReadOnly);
    }

    [Fact]
    public void DisabledRoot_BlocksEvenReadOnly()
    {
        using var package = new TempPackage();
        package.Config.Set("Root.Enabled", "false");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new RootPolicy(package.Config)
                .EnsureAllowed(PrivilegeRisk.ReadOnly));

        Assert.Contains("disabled", ex.Message);
    }

    [Fact]
    public void NumericPolicyValues_AreClamped()
    {
        using var package = new TempPackage();
        package.Config.Set("Root.CommandTimeoutSeconds", "9999");
        package.Config.Set("Root.MaxOutputCharacters", "1");

        var policy = new RootPolicy(package.Config);

        Assert.Equal(120, policy.CommandTimeoutSeconds);
        Assert.Equal(4096, policy.MaxOutputCharacters);
    }
}
