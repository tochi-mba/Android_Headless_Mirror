namespace Rex.Core;

/// <summary>Something on this PC to send: a file, or a folder with its total size and file count.</summary>
public sealed record LocalEntry(string Path, string Name, bool IsFolder, long Size, int Files = 1)
{
    /// <summary>What a path on this PC is, read from the disk; null when it is not there.</summary>
    public static LocalEntry? Read(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new LocalEntry(path, System.IO.Path.GetFileName(path), false, new FileInfo(path).Length);
            }

            if (!Directory.Exists(path))
            {
                return null;
            }

            // A junction can lead outside the folder or back into it. adb follows the directory
            // tree it is given, so count the same ordinary files and leave reparse points alone.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            var files = Directory.EnumerateFiles(path, "*", options).Select(f => new FileInfo(f)).ToArray();
            var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
            return new LocalEntry(path, name, true, files.Sum(f => f.Length), files.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

public enum TransferKind
{
    Push,
    Install,
    Refuse,
}

/// <summary>What will happen to one dropped thing: copied to a folder, installed, or refused with the reason.</summary>
public sealed record TransferItem(LocalEntry Entry, TransferKind Kind, string Target, string? Why = null);

/// <summary>
/// Decides what each dropped thing becomes. An .apk is installed when that is asked for; a folder is
/// sent whole or refused; photos, videos and music go to their own folders when sorting is on.
/// </summary>
public static class TransferPlan
{
    public static readonly IReadOnlySet<string> Pictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif", ".bmp", ".dng" };

    public static readonly IReadOnlySet<string> Movies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".mov", ".webm", ".avi", ".3gp", ".m4v" };

    public static readonly IReadOnlySet<string> Music = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".flac", ".wav", ".ogg", ".opus", ".aac", ".wma" };

    public const string FolderRefused = "Folders are not sent, as set in Settings.";

    public static bool IsApk(string name) => Path.GetExtension(name).Equals(".apk", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<TransferItem> Plan(IEnumerable<LocalEntry> entries, TransferSettings settings) =>
        entries.Select(entry =>
            entry.IsFolder && settings.Folders == "refuse" ? new TransferItem(entry, TransferKind.Refuse, string.Empty, FolderRefused)
            : !entry.IsFolder && settings.InstallApks && IsApk(entry.Name) ? new TransferItem(entry, TransferKind.Install, string.Empty)
            : new TransferItem(entry, TransferKind.Push, FolderFor(entry, settings)))
        .ToArray();

    /// <summary>The phone folder an entry goes to.</summary>
    public static string FolderFor(LocalEntry entry, TransferSettings settings)
    {
        if (!settings.SortMedia || entry.IsFolder)
        {
            return settings.Folder;
        }

        var extension = Path.GetExtension(entry.Name);
        var media = Pictures.Contains(extension) ? "Pictures/" : Movies.Contains(extension) ? "Movies/" : Music.Contains(extension) ? "Music/" : null;
        return media is null ? settings.Folder : TransferSettings.RootOf(settings.Folder) + media;
    }

    /// <summary>The words for what a drop would do, shown while it is over the window.</summary>
    public static string Describe(IReadOnlyList<TransferItem> items, string phone)
    {
        var sent = items.Where(i => i.Kind != TransferKind.Refuse).ToArray();
        if (sent.Length == 0)
        {
            return items.FirstOrDefault()?.Why ?? "Nothing to send.";
        }

        if (sent.All(i => i.Kind == TransferKind.Install))
        {
            return sent.Length == 1 ? $"Drop to install {sent[0].Entry.Name} on {phone}" : $"Drop to install {sent.Length} apps on {phone}";
        }

        if (sent.Length == 1 && sent[0].Entry.IsFolder)
        {
            var files = sent[0].Entry.Files == 1 ? "1 file" : $"{sent[0].Entry.Files} files";
            return $"Drop to send this folder ({files}) to {phone} · {FolderName(sent[0].Target)}";
        }

        var what = sent.Length == 1 ? sent[0].Entry.Name : $"{sent.Length} files";
        var folders = sent.Where(i => i.Kind == TransferKind.Push).Select(i => i.Target).Distinct().ToArray();
        return $"Drop to send {what} to {phone}" + (folders.Length == 1 ? " · " + FolderName(folders[0]) : string.Empty);
    }

    /// <summary>The last part of a phone folder: "/sdcard/Download/" is "Download".</summary>
    public static string FolderName(string folder) =>
        folder.TrimEnd('/').Split('/').LastOrDefault(p => p.Length > 0) ?? folder;
}
