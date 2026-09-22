namespace Rex.Core;

/// <summary>
/// Locates the package root (the folder that holds config.json and REX.bat) and
/// derives every runtime path from it. The executables may live in tools/rex or
/// in a build output folder several levels deep, so discovery walks upwards.
/// </summary>
public sealed class AppPaths
{
    public const string RootEnvironmentVariable = "REX_ROOT";

    public string Root { get; }

    public string Config => Path.Combine(Root, "config.json");
    public string State => Path.Combine(Root, "state.json");
    public string Logs => Path.Combine(Root, "logs");
    public string LogFile => Path.Combine(Logs, "mirror.log");
    public string Captures => Path.Combine(Root, "captures");
    public string Tools => Path.Combine(Root, "tools");
    public string ScrcpyTools => Path.Combine(Tools, "scrcpy");
    public string RexTools => Path.Combine(Tools, "rex");
    public string Launcher => Path.Combine(Root, "REX.bat");

    private AppPaths(string root) => Root = root;

    public static AppPaths Discover()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return FromRoot(overrideRoot);
        }

        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath),
            Directory.GetCurrentDirectory(),
        };

        foreach (var start in candidates)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var current = new DirectoryInfo(Path.GetFullPath(start));
            for (var depth = 0; depth < 8 && current is not null; depth++, current = current.Parent)
            {
                if (LooksLikeRoot(current.FullName))
                {
                    return new AppPaths(current.FullName);
                }
            }
        }

        throw new InvalidOperationException(
            "Could not find the Android Headless Mirror folder (the one containing config.json and REX.bat). " +
            $"Run from inside that folder or set {RootEnvironmentVariable}.");
    }

    public static AppPaths FromRoot(string root)
    {
        var full = Path.GetFullPath(root);
        if (!LooksLikeRoot(full))
        {
            throw new InvalidOperationException($"'{full}' is not an Android Headless Mirror folder.");
        }

        return new AppPaths(full);
    }

    /// <summary>Resolves a relative path inside the package and rejects escapes.</summary>
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

    private static bool LooksLikeRoot(string path) =>
        File.Exists(Path.Combine(path, "config.json")) &&
        File.Exists(Path.Combine(path, "REX.bat"));
}
