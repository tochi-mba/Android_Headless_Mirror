using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void FromCandidate_AcceptsValidPackageRoot()
    {
        using var package = new TempPackage();

        var paths = AppPaths.FromCandidate(package.Root);

        Assert.Equal(Path.GetFullPath(package.Root), paths.Root);
        Assert.Equal(Path.Combine(package.Root, "config.json"), paths.Config);
        Assert.Equal(Path.Combine(package.Root, "RexBridge.ps1"), paths.Bridge);
        Assert.Equal(Path.Combine(package.Root, "captures"), paths.Captures);
        Assert.Equal(Path.Combine(package.Root, "display-verification.json"), paths.DisplayVerification);
    }

    [Fact]
    public void FromCandidate_RejectsRandomDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rex-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => AppPaths.FromCandidate(path));
            Assert.Contains("not an Android Headless Mirror package root", ex.Message);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public void Discover_HonorsExplicitEnvironmentOverride()
    {
        using var package = new TempPackage();
        var previous = Environment.GetEnvironmentVariable("REX_AHM_ROOT");

        try
        {
            Environment.SetEnvironmentVariable("REX_AHM_ROOT", package.Root);

            var paths = AppPaths.Discover();

            Assert.Equal(Path.GetFullPath(package.Root), paths.Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable("REX_AHM_ROOT", previous);
        }
    }

    [Fact]
    public void AllImportantPathsStayInsideRoot()
    {
        using var package = new TempPackage();
        var root = Path.GetFullPath(package.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var path in new[]
        {
            package.Paths.Config,
            package.Paths.Setup,
            package.Paths.Stop,
            package.Paths.Diagnostics,
            package.Paths.ControlCenter,
            package.Paths.Bridge,
            package.Paths.State,
            package.Paths.Logs,
            package.Paths.Captures,
            package.Paths.DisplayVerification,
        })
        {
            Assert.StartsWith(root, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }
    }
}
