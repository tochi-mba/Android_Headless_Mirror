namespace Rex.Core;

/// <summary>What reading a phone's apps gave: the apps, or why there are none.</summary>
public sealed record AppsRead(bool Ok, IReadOnlyList<PhoneApp> Apps, string Error)
{
    public static AppsRead Failed(string error) => new(false, [], error);
}

/// <summary>
/// Reads a phone's apps with scrcpy itself (<c>scrcpy --list-apps</c>), which knows each app's own
/// name without any extra tool on the phone. It runs with <c>--no-cleanup</c>, so ending it can
/// never put back phone settings a running mirror changed.
/// </summary>
public static class AppLister
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static IReadOnlyList<string> Arguments(string serial) => ["--serial=" + serial, "--list-apps", "--no-cleanup"];

    public static async Task<AppsRead> ReadAsync(IProcessRunner runner, string scrcpyPath, string serial, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(scrcpyPath, Arguments(serial), Timeout, cancellationToken).ConfigureAwait(false);
        // scrcpy's server writes the list through its log, which has gone to either stream over the years.
        var apps = AppList.Parse((result.StdOut + "\n" + result.StdErr).Split('\n'));
        if (apps.Count > 0)
        {
            return new AppsRead(true, apps, string.Empty);
        }

        return AppsRead.Failed(
            result.TimedOut ? "The phone took too long to list its apps." :
            result.Ok ? "The phone listed no apps." :
            Why(result));
    }

    /// <summary>scrcpy's last error line without its prefix, else whatever it said.</summary>
    private static string Why(ProcessResult result)
    {
        var error = result.StdErr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.Contains("ERROR:", StringComparison.Ordinal));
        return error is null ? result.FailureText : error[(error.IndexOf("ERROR:", StringComparison.Ordinal) + 6)..].Trim();
    }
}
