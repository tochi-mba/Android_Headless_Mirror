using System.Runtime.InteropServices;
using System.IO;

namespace Rex.Mirror.Services.Files;

/// <summary>The optional File Explorer “Send to › Android phone” shortcut.</summary>
public static class SendToMenu
{
    public const string Name = "Android phone (Android Headless Mirror).lnk";
    public const string FolderVariable = "REX_SENDTO_DIR";

    public static string Folder => Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } test
        ? test
        : Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

    public static string Path => System.IO.Path.Combine(Folder, Name);

    public static void Apply(bool enabled, string executable)
    {
        if (!enabled)
        {
            if (File.Exists(Path)) File.Delete(Path);
            return;
        }

        Directory.CreateDirectory(Folder);
        var type = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(type)!;
            dynamic scripting = shell;
            shortcut = scripting.CreateShortcut(Path);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.Arguments = "--send";
            link.WorkingDirectory = System.IO.Path.GetDirectoryName(executable) ?? string.Empty;
            link.Description = "Send files to an Android phone with Android Headless Mirror";
            link.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
