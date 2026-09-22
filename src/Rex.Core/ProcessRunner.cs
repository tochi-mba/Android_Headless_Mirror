using System.Diagnostics;
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
}

/// <summary>Runs hidden child processes with captured output and a hard timeout that kills the process tree.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Create(fileName, arguments);
        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, $"Could not start {fileName}.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        var timedOut = !await WaitAsync(process, timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false),
            timedOut);
    }

    public async Task<ProcessBytesResult> RunBytesAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Create(fileName, arguments);
        if (!process.Start())
        {
            return new ProcessBytesResult(-1, [], $"Could not start {fileName}.");
        }

        var buffer = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer, CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        var timedOut = !await WaitAsync(process, timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
        await copy.ConfigureAwait(false);
        return new ProcessBytesResult(
            timedOut ? -1 : process.ExitCode,
            buffer.ToArray(),
            await stderr.ConfigureAwait(false),
            timedOut);
    }

    private static Process Create(string fileName, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = start };
    }

    /// <summary>Returns false when the timeout elapsed (the process is then killed).</summary>
    private static async Task<bool> WaitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
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
}
