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
