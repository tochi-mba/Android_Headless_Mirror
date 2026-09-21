namespace Rex.AndroidMirror.Cli;

public sealed class AppPaths
{
    public string Root { get; }

    public string Config => Path.Combine(Root, "config.json");
    public string Setup => Path.Combine(Root, "Setup.ps1");
    public string StartBatch => Path.Combine(Root, "START_NOW.bat");
    public string Stop => Path.Combine(Root, "Stop-PhoneMirror.ps1");
    public string Diagnostics => Path.Combine(Root, "Diagnostics.ps1");
    public string InstallAutostart => Path.Combine(Root, "Install-Autostart.ps1");
    public string RemoveAutostart => Path.Combine(Root, "Remove-Autostart.ps1");
    public string InstallRexShortcut => Path.Combine(Root, "Install-Rex-Shortcut.ps1");
    public string RemoveRexShortcut => Path.Combine(Root, "Remove-Rex-Shortcut.ps1");
    public string ResetLockChoices => Path.Combine(Root, "Reset-LockScreenChoices.ps1");
    public string ControlCenter => Path.Combine(Root, "ControlCenter.ps1");
    public string Bridge => Path.Combine(Root, "RexBridge.ps1");
    public string State => Path.Combine(Root, "state.json");
    public string StopFlag => Path.Combine(Root, "stop.flag");
    public string Logs => Path.Combine(Root, "logs");
    public string Captures => Path.Combine(Root, "captures");
    public string DisplayVerification => Path.Combine(Root, "display-verification.json");

    private AppPaths(string root)
    {
        Root = root;
    }

    public static AppPaths Discover()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("REX_AHM_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return FromCandidate(overrideRoot);
        }

        var candidates = new List<string?>
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath)
        };

        foreach (var start in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var current = new DirectoryInfo(Path.GetFullPath(start!));
            for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
            {
                if (LooksLikeRoot(current.FullName))
                {
                    return new AppPaths(current.FullName);
                }
            }
        }

        throw new InvalidOperationException(
            "Could not locate the Android Headless Mirror root. Run rex.exe from the repository/package folder or set REX_AHM_ROOT.");
    }

    public static AppPaths FromCandidate(string candidate)
    {
        var full = Path.GetFullPath(candidate);
        if (!LooksLikeRoot(full))
        {
            throw new InvalidOperationException(
                $"'{full}' is not an Android Headless Mirror package root.");
        }

        return new AppPaths(full);
    }

    private static bool LooksLikeRoot(string path)
    {
        return File.Exists(Path.Combine(path, "config.json")) &&
               File.Exists(Path.Combine(path, "Setup.ps1")) &&
               File.Exists(Path.Combine(path, "Start-PhoneMirror.ps1"));
    }
}
