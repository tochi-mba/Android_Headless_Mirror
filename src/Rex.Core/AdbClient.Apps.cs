using System.Globalization;

namespace Rex.Core;

/// <summary>
/// Opening, closing and looking after the phone's apps. Every package is checked by
/// <see cref="PackageName"/> and passed to Android as one argument, never inside a command line.
/// </summary>
public sealed partial class AdbClient
{
    private const string Launcher = "android.intent.category.LAUNCHER";

    /// <summary>The activity the phone's launcher opens for an app, or null when it has none.</summary>
    public async Task<string?> ResolveLauncherAsync(string serial, string package, CancellationToken cancellationToken = default)
    {
        if (!PackageName.IsValid(package))
        {
            return null;
        }

        var result = await ShellAsync(serial, ["cmd", "package", "resolve-activity", "--brief", "-c", Launcher, package], cancellationToken).ConfigureAwait(false);
        var last = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return result.Ok && PackageName.IsValidComponent(last) ? last : null;
    }

    /// <summary>
    /// Opens an app on the phone, or on one of its displays. Fresh closes it first so it starts from
    /// the beginning. An app whose launcher activity cannot be found is opened the way the
    /// launcher itself would, on the phone's own screen only.
    /// </summary>
    public async Task<AndroidResult> LaunchAppAsync(string serial, string package, bool fresh, int? displayId = null, CancellationToken cancellationToken = default)
    {
        if (!PackageName.IsValid(package))
        {
            return AndroidResult.Failure($"\"{package}\" is not an app's package name.");
        }

        var component = await ResolveLauncherAsync(serial, package, cancellationToken).ConfigureAwait(false);
        if (component is null)
        {
            if (displayId is not null)
            {
                return AndroidResult.Failure("This app cannot be opened on another screen.");
            }

            if (fresh)
            {
                await ForceStopAsync(serial, package, cancellationToken).ConfigureAwait(false);
            }

            return Answer(await ShellAsync(serial, ["monkey", "-p", package, "-c", Launcher, "1"], cancellationToken).ConfigureAwait(false));
        }

        var args = new List<string> { "am", "start" };
        if (fresh)
        {
            args.Add("-S");
        }

        if (displayId is { } display)
        {
            args.AddRange(["--display", display.ToString(CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-n", component]);
        return Answer(await ShellAsync(serial, args, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Closes an app completely, as Force stop does in its settings.</summary>
    public Task<AndroidResult> ForceStopAsync(string serial, string package, CancellationToken cancellationToken = default) =>
        Checked(serial, package, ["am", "force-stop", package], cancellationToken);

    /// <summary>Opens the app's own page in the phone's settings.</summary>
    public Task<AndroidResult> OpenAppInfoAsync(string serial, string package, CancellationToken cancellationToken = default) =>
        Checked(serial, package, ["am", "start", "-a", "android.settings.APPLICATION_DETAILS_SETTINGS", "-d", "package:" + package], cancellationToken);

    /// <summary>Deletes everything the app has stored, as Clear storage does.</summary>
    public Task<AndroidResult> ClearAppDataAsync(string serial, string package, CancellationToken cancellationToken = default) =>
        Checked(serial, package, ["pm", "clear", package], cancellationToken);

    /// <summary>Uninstalls an app the person installed. Apps that came with the phone are never sent here.</summary>
    public Task<AndroidResult> UninstallAppAsync(string serial, string package, CancellationToken cancellationToken = default) =>
        Checked(serial, package, ["pm", "uninstall", package], cancellationToken, TimeSpan.FromSeconds(60));

    private async Task<AndroidResult> Checked(string serial, string package, IReadOnlyList<string> args, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (!PackageName.IsValid(package))
        {
            return AndroidResult.Failure($"\"{package}\" is not an app's package name.");
        }

        return Answer(await ShellAsync(serial, args, cancellationToken, timeout).ConfigureAwait(false));
    }

    /// <summary>
    /// am and pm often exit 0 after failing, and say so in their output instead: "Error: ...",
    /// "Failure [...]" or "Failed". Those are failures, in the tool's own words.
    /// </summary>
    internal static AndroidResult Answer(ProcessResult result)
    {
        if (!result.Ok)
        {
            return AndroidResult.Failure(result.FailureText);
        }

        var said = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var failed = said.FirstOrDefault(l => l.StartsWith("Error", StringComparison.Ordinal) || l.StartsWith("Failure", StringComparison.Ordinal) ||
            l.StartsWith("Failed", StringComparison.Ordinal) || l.Contains("No activities found", StringComparison.Ordinal));
        return failed is null ? AndroidResult.Success(result.StdOut.Trim()) : AndroidResult.Failure(failed);
    }
}
