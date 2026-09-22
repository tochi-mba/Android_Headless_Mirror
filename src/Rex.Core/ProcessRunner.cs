using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Rex.Core;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut = false)
{
    public bool Ok => ExitCode == 0 && !TimedOut;

    /// <summary>Best human-readable failure text: stderr, else stdout, else the exit code.</summary>
    public string FailureText =>
        TimedOut ? "The command timed out." :
        !string.IsNullOrWhiteSpace(StdErr) ? StdErr.Trim() :
        !string.IsNullOrWhiteSpace(StdOut) ? StdOut.Trim() :
        $"exit code {ExitCode}";
}

public sealed record ProcessBytesResult(int ExitCode, byte[] StdOut, string StdErr, bool TimedOut = false)
{
    public bool Ok => ExitCode == 0 && !TimedOut;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<ProcessBytesResult> RunBytesAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a hidden process without capturing its output. Use it for commands that leave a
    /// daemon behind (adb start-server): a daemon inherits any pipe the child was given and
    /// would keep it open long after the child exits.
    /// </summary>
    Task<int> RunDetachedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs hidden child processes with captured output and a hard timeout that kills the process
/// tree. Output is collected as it arrives, so a grandchild that inherited the pipes (the ADB
/// server does) cannot stall the caller once the child itself has exited.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for trailing output after the child has exited.</summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromMilliseconds(300);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Create(fileName, arguments, capture: true);
        var exited = TrackExit(process);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(stdout, e.Data);
        process.ErrorDataReceived += (_, e) => Append(stderr, e.Data);

        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, $"Could not start {fileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = !await WaitAsync(process, exited, timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
        await DrainAsync(process).ConfigureAwait(false);
        return new ProcessResult(timedOut ? -1 : process.ExitCode, Text(stdout), Text(stderr), timedOut);
    }

    public async Task<ProcessBytesResult> RunBytesAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Create(fileName, arguments, capture: true);
        var exited = TrackExit(process);
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => Append(stderr, e.Data);

        if (!process.Start())
        {
            return new ProcessBytesResult(-1, [], $"Could not start {fileName}.");
        }

        process.BeginErrorReadLine();
        var buffer = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer, CancellationToken.None);

        var timedOut = !await WaitAsync(process, exited, timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
        await Task.WhenAny(copy, Task.Delay(DrainGrace, CancellationToken.None)).ConfigureAwait(false);
        await DrainAsync(process).ConfigureAwait(false);
        return new ProcessBytesResult(timedOut ? -1 : process.ExitCode, buffer.ToArray(), Text(stderr), timedOut);
    }

    public async Task<int> RunDetachedAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Create(fileName, arguments, capture: false);
        var exited = TrackExit(process);
        if (!process.Start())
        {
            return -1;
        }

        var timedOut = !await WaitAsync(process, exited, timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
        return timedOut ? -1 : process.ExitCode;
    }

    /// <summary>
    /// Marks this process's own standard handles non-inheritable. Console programs that pipe
    /// rex.exe would otherwise wait on the ADB server, which inherits every inheritable handle
    /// of whoever started it. Call once at startup, before any child process is created.
    /// </summary>
    public static void PreventStandardHandleInheritance()
    {
        foreach (var kind in new[] { StdInputHandle, StdOutputHandle, StdErrorHandle })
        {
            var handle = GetStdHandle(kind);
            if (handle != IntPtr.Zero && handle != InvalidHandleValue)
            {
                SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }

    private static Process Create(string fileName, IReadOnlyList<string> arguments, bool capture)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
            StandardOutputEncoding = capture ? Encoding.UTF8 : null,
            StandardErrorEncoding = capture ? Encoding.UTF8 : null,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = start, EnableRaisingEvents = true };
    }

    private static Task TrackExit(Process process)
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        return exited.Task;
    }

    private static void Append(StringBuilder builder, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (builder)
        {
            builder.Append(line).Append('\n');
        }
    }

    private static string Text(StringBuilder builder)
    {
        lock (builder)
        {
            return builder.ToString();
        }
    }

    /// <summary>Returns false when the timeout elapsed (the process tree is then killed).</summary>
    private static async Task<bool> WaitAsync(Process process, Task exited, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            await exited.WaitAsync(linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return false;
        }
    }

    /// <summary>Gives the async readers a moment to deliver trailing output; never waits for a leaked pipe.</summary>
    private static async Task DrainAsync(Process process)
    {
        using var grace = new CancellationTokenSource(DrainGrace);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A grandchild still holds the pipe; everything the child wrote has already been collected.
        }
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied or already terminating; nothing more to do.
        }
    }

    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint HandleFlagInherit = 1;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
