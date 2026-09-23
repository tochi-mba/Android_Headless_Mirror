namespace Rex.Core;

/// <summary>
/// Where the app keeps its files. There are two layouts:
/// <list type="bullet">
/// <item><b>Checkout</b>: the repository folder (config.json next to REX.bat) is the root, and the
/// executables live in tools/rex or a build output folder below it. Discovery walks upwards.</item>
/// <item><b>Installed</b>: the installer puts the executables under Program Files and the data
/// root is <c>%LocalAppData%\REX\Android Headless Mirror</c>, created on first run.</item>
/// </list>
/// <c>REX_ROOT</c> overrides both.
/// </summary>
public sealed class AppPaths
{
    public const string RootEnvironmentVariable = "REX_ROOT";
    public const string ProductFolderName = "Android Headless Mirror";

    public string Root { get; }

    public string Config => Path.Combine(Root, "config.json");
    public string State => Path.Combine(Root, "state.json");
    public string Logs => Path.Combine(Root, "logs");
    public string LogFile => Path.Combine(Logs, "mirror.log");
    public string Captures => Path.Combine(Root, "captures");
    public string Tools => Path.Combine(Root, "tools");

    /// <summary>scrcpy versions installed or updated by the app itself.</summary>
    public string ScrcpyTools => Path.Combine(Tools, "scrcpy");

    /// <summary>The scrcpy the installer ships next to the executables.</summary>
    public static string BundledScrcpyTools => Path.Combine(AppContext.BaseDirectory, "scrcpy");

    private AppPaths(string root) => Root = root;

    public static AppPaths Discover()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return FromRoot(overrideRoot);
        }

        // Where the executable lives decides the layout; the working directory never does, so an
        // installed rex.exe run from inside a checkout still uses the installed data root.
        var candidates = new[] { AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) };

        foreach (var start in candidates)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var current = new DirectoryInfo(Path.GetFullPath(start));
            for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
            {
                if (LooksLikeCheckout(current.FullName))
                {
                    return new AppPaths(current.FullName);
                }
            }
        }

        return CreateDataRoot(DefaultDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    }

    /// <summary>An existing root that already holds config.json (a checkout or a prepared folder).</summary>
    public static AppPaths FromRoot(string root)
    {
        var full = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(full, "config.json")))
        {
            throw new InvalidOperationException($"'{full}' is not an Android Headless Mirror folder (no config.json).");
        }

        return new AppPaths(full);
    }

    /// <summary>Creates the folder and a default config.json when they do not exist yet.</summary>
    public static AppPaths CreateDataRoot(string directory)
    {
        var full = Path.GetFullPath(directory);
        Directory.CreateDirectory(full);
        var config = Path.Combine(full, "config.json");
        if (!File.Exists(config))
        {
            ConfigFile.Save(config, new RexConfig());
            File.Delete(ConfigFile.BackupPath(config));
        }

        return new AppPaths(full);
    }

    public static string DefaultDataRoot(string localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Windows did not report a Local AppData folder.");
        }

        return Path.Combine(localAppData, "REX", ProductFolderName);
    }

    /// <summary>Resolves a relative path inside the root and rejects escapes.</summary>
    public string Inside(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new ArgumentException("A relative path is required.", nameof(relative));
        }

        var combined = Path.GetFullPath(Path.Combine(Root, relative));
        var rootWithSeparator = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{relative}' points outside the Android Headless Mirror folder.");
        }

        return combined;
    }

    public string ScreenshotFolder(string configuredPath) =>
        Path.IsPathFullyQualified(configuredPath) ? Path.GetFullPath(configuredPath) : Inside(configuredPath);

    private static bool LooksLikeCheckout(string path) =>
        File.Exists(Path.Combine(path, "config.json")) &&
        File.Exists(Path.Combine(path, "REX.bat"));
}
