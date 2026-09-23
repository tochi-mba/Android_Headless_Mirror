using System.Diagnostics;
using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// The ADB server is a grandchild that inherits the pipes of the adb client that spawned it.
/// cmd's "start /b" reproduces that shape without adb: the background ping keeps the pipe open
/// long after the command that produced the output has exited.
/// </summary>
public sealed class ProcessRunnerTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task RunAsync_ReturnsOnceTheChildExitsEvenIfAGrandchildHoldsThePipe()
    {
        var runner = new ProcessRunner();
        var watch = Stopwatch.StartNew();

        var result = await runner.RunAsync(Cmd, ["/c", "echo hello& echo oops 1>&2& start /b cmd /c ping -n 16 127.0.0.1 >nul"], TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.FailureText);
        Assert.Equal("hello", result.StdOut.Trim());
        Assert.Equal("oops", result.StdErr.Trim());
        // The grandchild lives ~15 s; returning well before that proves the pipe was not awaited.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_KillsTheProcessTreeOnTimeout()
    {
        var runner = new ProcessRunner();
        var watch = Stopwatch.StartNew();

        var result = await runner.RunAsync(Cmd, ["/c", "echo started& ping -n 60 127.0.0.1 >nul"], TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.False(result.Ok);
        Assert.Equal("The command timed out.", result.FailureText);
        Assert.Equal("started", result.StdOut.Trim());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task RunBytesAsync_CapturesBinaryOutputAndErrors()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunBytesAsync(Cmd, ["/c", "echo abc& echo bad 1>&2& exit 3"], TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(result.StdOut).Trim());
        Assert.Equal("bad", result.StdErr.Trim());
    }

    [Fact]
    public async Task RunDetachedAsync_ReportsTheExitCodeWithoutCapturing()
    {
        var runner = new ProcessRunner();

        Assert.Equal(0, await runner.RunDetachedAsync(Cmd, ["/c", "exit 0"], TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(7, await runner.RunDetachedAsync(Cmd, ["/c", "exit 7"], TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(-1, await runner.RunDetachedAsync(Cmd, ["/c", "ping -n 60 127.0.0.1 >nul"], TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PreventStandardHandleInheritance_IsIdempotent()
    {
        ProcessRunner.PreventStandardHandleInheritance();
        ProcessRunner.PreventStandardHandleInheritance();
    }
}
