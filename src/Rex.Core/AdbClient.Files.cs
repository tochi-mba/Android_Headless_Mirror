using System.Globalization;

namespace Rex.Core;

public sealed record InstallFlags(bool Replace = true, bool AllowDowngrade = false, bool GrantPermissions = false, bool AllowTestApps = false);

/// <summary>Copying files to shared storage and installing APKs.</summary>
public sealed partial class AdbClient
{
    private static readonly TimeSpan MinimumFileTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Copies one file or folder. A large item gets roughly one minute per 100 MB, with a ten-minute floor.</summary>
    public Task<ProcessResult> PushAsync(string serial, string localPath, string remoteFolder, long size = 0, CancellationToken cancellationToken = default)
    {
        if (!TransferSettings.IsValidFolder(remoteFolder))
        {
            return Task.FromResult(new ProcessResult(-1, string.Empty, TransferSettings.WhyNotFolder(remoteFolder)!));
        }

        var minutes = Math.Max(MinimumFileTimeout.TotalMinutes, Math.Ceiling(Math.Max(0, size) / (100.0 * 1024 * 1024)));
        return RunAsync(["-s", serial, "push", localPath, remoteFolder], TimeSpan.FromMinutes(minutes), cancellationToken);
    }

    public async Task<AndroidResult> InstallAsync(string serial, string apkPath, InstallFlags flags, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(InstallArguments(serial, apkPath, flags), TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        return Answer(result);
    }

    public static IReadOnlyList<string> InstallArguments(string serial, string apkPath, InstallFlags flags)
    {
        var args = new List<string> { "-s", serial, "install" };
        if (flags.Replace) args.Add("-r");
        if (flags.AllowDowngrade) args.Add("-d");
        if (flags.GrantPermissions) args.Add("-g");
        if (flags.AllowTestApps) args.Add("-t");
        args.Add(apkPath);
        return args;
    }

    public async Task<long?> RemoteSizeAsync(string serial, string remotePath, CancellationToken cancellationToken = default)
    {
        var result = await ShellCommandAsync(serial, "stat -c %s " + ShellQuoting.Quote(remotePath), cancellationToken).ConfigureAwait(false);
        return result.Ok && long.TryParse(result.StdOut.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size >= 0 ? size : null;
    }

    public async Task<IReadOnlySet<string>> ListNamesAsync(string serial, string remoteFolder, CancellationToken cancellationToken = default)
    {
        if (!TransferSettings.IsValidFolder(remoteFolder))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var result = await ShellCommandAsync(serial, "ls -1 " + ShellQuoting.Quote(remoteFolder), cancellationToken).ConfigureAwait(false);
        return result.Ok
            ? result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public async Task<AndroidResult> DeleteFileAsync(string serial, string remotePath, CancellationToken cancellationToken = default) =>
        await DeleteEntryAsync(serial, remotePath, recursive: false, cancellationToken).ConfigureAwait(false);

    /// <summary>Removes a partial transfer. Recursive removal is only accepted inside shared storage.</summary>
    public async Task<AndroidResult> DeleteEntryAsync(string serial, string remotePath, bool recursive, CancellationToken cancellationToken = default)
    {
        var slash = remotePath.LastIndexOf('/');
        var folder = slash >= 0 ? remotePath[..(slash + 1)] : string.Empty;
        var name = slash >= 0 ? remotePath[(slash + 1)..] : string.Empty;
        if (!TransferSettings.IsValidFolder(folder) || string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return AndroidResult.Failure("The phone file path is not safe.");
        }

        var option = recursive ? "rm -rf -- " : "rm -f -- ";
        return AndroidResult.From(await ShellCommandAsync(serial, option + ShellQuoting.Quote(remotePath), cancellationToken).ConfigureAwait(false));
    }

    public async Task<AndroidResult> ScanMediaAsync(string serial, string remotePath, CancellationToken cancellationToken = default) =>
        AndroidResult.From(await ShellAsync(serial,
            ["am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", "file://" + remotePath], cancellationToken).ConfigureAwait(false));

    public async Task<AndroidResult> OpenFolderAsync(string serial, string remoteFolder, CancellationToken cancellationToken = default)
    {
        if (!TransferSettings.IsValidFolder(remoteFolder))
        {
            return AndroidResult.Failure(TransferSettings.WhyNotFolder(remoteFolder)!);
        }

        var result = await ShellAsync(serial,
            ["am", "start", "-a", "android.intent.action.VIEW", "-d", "content://com.android.externalstorage.documents/root/primary", "-t", "vnd.android.document/root"],
            cancellationToken).ConfigureAwait(false);
        return Answer(result);
    }

    public static string RemotePath(string folder, string name)
    {
        if (!TransferSettings.IsValidFolder(folder) || string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', '\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("The phone file path is not safe.");
        }

        return folder.TrimEnd('/') + "/" + name;
    }
}
