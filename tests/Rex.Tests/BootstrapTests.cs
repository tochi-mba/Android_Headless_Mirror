using System.Diagnostics;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class BootstrapTests
{
    [Theory]
    [InlineData("fail", false)]
    [InlineData("incomplete", false)]
    [InlineData("complete", true)]
    [InlineData("locked", false)]
    public async Task SourceBuildPreservesInstalledAppUntilReplacementIsComplete(string mode, bool succeeds)
    {
        using var package = new TestPackage();
        var script = Path.Combine(package.Root, "Bootstrap-Rex.ps1");
        File.Copy(Path.Combine(RepoPaths.Root, "Bootstrap-Rex.ps1"), script);
        var installed = Path.Combine(package.Root, "tools", "rex");
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "RexMirror.exe"), "original app");
        File.WriteAllText(Path.Combine(installed, "rex.exe"), "original cli");
        using var locked = mode == "locked"
            ? new FileStream(Path.Combine(installed, "RexMirror.exe"), FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;

        var fakeBin = Path.Combine(package.Root, "fake-bin");
        Directory.CreateDirectory(fakeBin);
        File.WriteAllText(Path.Combine(fakeBin, "dotnet.cmd"), """
            @echo off
            if "%REX_BOOTSTRAP_TEST_MODE%"=="fail" exit /b 1
            :args
            if "%~1"=="" exit /b 2
            if "%~1"=="-o" goto output
            shift
            goto args
            :output
            shift
            if not exist "%~1" mkdir "%~1"
            echo replacement app>"%~1\RexMirror.exe"
            if "%REX_BOOTSTRAP_TEST_MODE%"=="complete" echo replacement cli>"%~1\rex.exe"
            exit /b 0
            """);

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = package.Root,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Source" })
            start.ArgumentList.Add(arg);
        start.Environment["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["REX_BOOTSTRAP_TEST_MODE"] = mode == "locked" ? "complete" : mode;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var output = await stdout + await stderr;
        Assert.True((process.ExitCode == 0) == succeeds, output);
        Assert.Equal(succeeds ? "replacement app" : "original app", File.ReadAllText(Path.Combine(installed, "RexMirror.exe")).Trim());
        Assert.Equal(succeeds ? "replacement cli" : "original cli", File.ReadAllText(Path.Combine(installed, "rex.exe")).Trim());
        Assert.Empty(Directory.GetDirectories(Path.Combine(package.Root, "tools"), "rex-staging-*"));
        if (succeeds)
        {
            var backup = Assert.Single(Directory.GetDirectories(Path.Combine(package.Root, "tools"), "rex-previous-*"));
            Assert.Equal("original app", File.ReadAllText(Path.Combine(backup, "RexMirror.exe")));
        }
    }
}
