using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace Rex.Core;

/// <summary>Start-with-Windows via the per-user Run registry key. No admin rights, no scheduled tasks.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "AndroidHeadlessMirror";
    public const string BackgroundArgument = "--background";

    private static readonly string[] LegacyStartupShortcuts =
    [
        "Android Headless Mirror.lnk",
        "S21 Headless Mirror.lnk",
    ];

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static string? CurrentCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) as string;
    }

    public static void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");
        key.SetValue(ValueName, $"\"{executablePath}\" {BackgroundArgument}");
        RemoveLegacyShortcuts();
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        RemoveLegacyShortcuts();
    }

    /// <summary>Older versions used a Startup-folder shortcut; clean it up so two copies never race.</summary>
    public static void RemoveLegacyShortcuts()
    {
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startup))
        {
            return;
        }

        foreach (var name in LegacyStartupShortcuts)
        {
            var path = Path.Combine(startup, name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

/// <summary>Creates and removes the "REX" desktop shortcut without WScript.</summary>
public static class DesktopShortcut
{
    public const string FileName = "REX.lnk";

    public static string DefaultPath()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
        {
            throw new InvalidOperationException("Could not resolve the Desktop folder.");
        }

        return Path.Combine(desktop, FileName);
    }

    public static bool Exists(string? shortcutPath = null) => File.Exists(shortcutPath ?? DefaultPath());

    public static string Create(string targetExecutable, string workingDirectory, string? shortcutPath = null)
    {
        var path = shortcutPath ?? DefaultPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetExecutable);
        link.SetWorkingDirectory(workingDirectory);
        link.SetDescription("Android Headless Mirror by REX Technologies");
        link.SetIconLocation(targetExecutable, 0);
        ((IPersistFile)link).Save(path, true);
        return path;
    }

    public static bool Remove(string? shortcutPath = null)
    {
        var path = shortcutPath ?? DefaultPath();
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
