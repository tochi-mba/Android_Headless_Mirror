using System.Diagnostics;
using System.Globalization;

namespace Rex.Core;

/// <summary>
/// Writes every command it runs into the log while <c>Logging.Verbose</c> is on: what ran, how it
/// ended and how long it took, for a bug report. Taps and swipes are left out, so a pattern drawn
/// on the lock screen never reaches the log; everything else about a command is kept.
/// </summary>
public sealed class LoggedProcessRunner(IProcessRunner inner, Action<string> log, Func<bool> verbose) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!verbose())
        {
            return await inner.RunAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        }

        var watch = Stopwatch.StartNew();
        var result = await inner.RunAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        log(Line(fileName, arguments, result.ExitCode, result.TimedOut, watch.Elapsed));
        return result;
    }

    public async Task<ProcessBytesResult> RunBytesAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!verbose())
        {
            return await inner.RunBytesAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        }

        var watch = Stopwatch.StartNew();
        var result = await inner.RunBytesAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        log(Line(fileName, arguments, result.ExitCode, result.TimedOut, watch.Elapsed));
        return result;
    }

    public async Task<int> RunDetachedAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!verbose())
        {
            return await inner.RunDetachedAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        }

        var watch = Stopwatch.StartNew();
        var exit = await inner.RunDetachedAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        log(Line(fileName, arguments, exit, timedOut: false, watch.Elapsed));
        return exit;
    }

    private static string Line(string fileName, IReadOnlyList<string> arguments, int exit, bool timedOut, TimeSpan took) =>
        $"Ran {Describe(fileName, arguments)} · {(timedOut ? "timed out" : "exit " + exit.ToString(CultureInfo.InvariantCulture))} · " +
        $"{took.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";

    /// <summary>
    /// A command as the log shows it: the program's name and its arguments, quoted where they hold
    /// a space. From an <c>input</c> command on, the arguments are left out: they would say where
    /// the screen was touched.
    /// </summary>
    public static string Describe(string fileName, IReadOnlyList<string> arguments)
    {
        var words = new List<string> { Path.GetFileNameWithoutExtension(fileName) };
        foreach (var argument in arguments)
        {
            if (argument == "input")
            {
                words.Add("input (where the screen was touched is left out)");
                break;
            }

            words.Add(argument.Length == 0 || argument.Contains(' ') ? "\"" + argument + "\"" : argument);
        }

        return string.Join(' ', words);
    }
}
