using System.Text;
using System.Text.RegularExpressions;

namespace Rex.AndroidMirror.Cli;

public interface IAndroidShellRunner
{
    Task<AndroidCommandResult> RunAsync(
        string adbPath,
        string serial,
        PrivilegedCommand command,
        RootExecutionMode? rootMode = null,
        CancellationToken cancellationToken = default);
}

public static partial class AndroidShellQuoting
{
    [GeneratedRegex(@"^[A-Za-z0-9_@%+=:,./-]+$")]
    private static partial Regex SafeArgumentRegex();

    public static string Quote(string value)
    {
        if (value.Length == 0)
            return "''";

        if (SafeArgumentRegex().IsMatch(value))
            return value;

        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    public static string BuildCommand(string executable, IEnumerable<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("Executable is required.", nameof(executable));

        var pieces = new List<string> { Quote(executable) };
        pieces.AddRange(arguments.Select(Quote));
        return string.Join(' ', pieces);
    }
}

public sealed class AndroidShellRunner : IAndroidShellRunner
{
    private readonly IProcessRunner _runner;
    private readonly string _workingDirectory;
    private readonly RootPolicy _policy;

    public AndroidShellRunner(
        IProcessRunner runner,
        string workingDirectory,
        RootPolicy policy)
    {
        _runner = runner;
        _workingDirectory = workingDirectory;
        _policy = policy;
    }

    public async Task<AndroidCommandResult> RunAsync(
        string adbPath,
        string serial,
        PrivilegedCommand command,
        RootExecutionMode? rootMode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adbPath))
            throw new InvalidOperationException("adb.exe is unavailable. Run REX setup first.");

        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Android serial is required.", nameof(serial));

        var timeout = command.Timeout ??
            TimeSpan.FromSeconds(_policy.CommandTimeoutSeconds);
        var maxOutput = command.MaxOutputCharacters ??
            _policy.MaxOutputCharacters;

        var remoteCommand = AndroidShellQuoting.BuildCommand(
            command.Executable,
            command.Arguments);

        var args = new List<string> { "-s", serial, "shell" };
        if (rootMode == RootExecutionMode.Su)
        {
            args.Add("su");
            args.Add("-c");
            args.Add(remoteCommand);
        }
        else
        {
            args.Add("sh");
            args.Add("-c");
            args.Add(remoteCommand);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var result = await _runner.RunAsync(
                adbPath,
                args,
                _workingDirectory,
                captureOutput: true,
                timeoutCts.Token);

            var (stdout, outTruncated) = Truncate(result.StdOut, maxOutput);
            var (stderr, errTruncated) = Truncate(result.StdErr, maxOutput);

            return new AndroidCommandResult(
                result.ExitCode,
                stdout,
                stderr,
                TimedOut: false,
                Truncated: outTruncated || errTruncated);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutCts.IsCancellationRequested)
        {
            return new AndroidCommandResult(
                -1,
                string.Empty,
                $"Command '{command.Id}' timed out after {timeout.TotalSeconds:0.#} seconds.",
                TimedOut: true,
                Truncated: false);
        }
    }

    private static (string Value, bool Truncated) Truncate(string value, int max)
    {
        if (value.Length <= max)
            return (value, false);

        return (value[..max] + Environment.NewLine + "[REX output truncated]", true);
    }
}
