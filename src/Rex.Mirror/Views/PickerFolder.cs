using System.IO;

namespace Rex.Mirror.Views;

/// <summary>
/// Where a folder picker starts. Windows refuses to open a picker in a folder that is not there,
/// and the folder a setting names may not be yet: a fresh install has no recordings folder until
/// the first recording. Since these folders live inside the app's own, the missing one is simply
/// made first; one that cannot be gives way to a folder that is always there.
/// </summary>
public static class PickerFolder
{
    public static string Ready(string folder, string fallback)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return fallback;
        }
    }
}
