using System.Text.RegularExpressions;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Hygiene rules for the repository itself.</summary>
public sealed class RepositoryTests
{
    private static readonly string[] SourceGlobs = ["*.cs", "*.xaml", "*.ps1", "*.bat", "*.yml", "*.js", "*.css", "*.html", "*.md", "*.json"];
    private static readonly string[] Skip = [$"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"];

    private static IEnumerable<string> SourceFiles() =>
        SourceGlobs.SelectMany(glob => Directory.EnumerateFiles(RepoPaths.Root, glob, SearchOption.AllDirectories))
            .Where(path => !Skip.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !path.EndsWith("package-lock.json", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void NoSourceFileExceedsOneThousandLines()
    {
        var offenders = SourceFiles()
            .Select(path => (path, lines: File.ReadLines(path).Count()))
            .Where(x => x.lines > 1000)
            .Select(x => $"{Path.GetRelativePath(RepoPaths.Root, x.path)} ({x.lines})")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoHardcodedUserPaths()
    {
        var offenders = SourceFiles()
            .Where(path => !Path.GetFileName(path).Equals("RepositoryTests.cs", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"[A-Z]:\\Users\\|/home/\w+", RegexOptions.IgnoreCase))
            .Select(path => Path.GetRelativePath(RepoPaths.Root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void OnlyTheLauncherAndBootstrapAreScripts()
    {
        var scripts = Directory.EnumerateFiles(RepoPaths.Root, "*.*", SearchOption.AllDirectories)
            .Where(path => !Skip.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Where(path => Path.GetExtension(path) is ".ps1" or ".bat" or ".vbs" or ".py")
            .Select(path => Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Bootstrap-Rex.ps1", "REX.bat", "assets/make-icon.ps1"], scripts);
    }

    [Fact]
    public void RuntimeFilesAreIgnored()
    {
        var ignore = File.ReadAllText(Path.Combine(RepoPaths.Root, ".gitignore"));
        foreach (var entry in new[] { "tools/", "state.json", "logs/", "captures/", "config.json.rex-backup", "bin/", "obj/", "node_modules/" })
        {
            Assert.Contains(entry, ignore);
        }
    }

    [Fact]
    public void CommittedConfigIsTheCurrentSchemaWithDefaults()
    {
        using var package = new TestPackage();
        var expected = File.ReadAllText(package.Paths.Config).ReplaceLineEndings();
        var committed = File.ReadAllText(Path.Combine(RepoPaths.Root, "config.json")).ReplaceLineEndings();
        Assert.Equal(expected, committed);
    }

    [Fact]
    public void ScrcpyLaunchContractIsDocumentedForAgents()
    {
        var agents = File.ReadAllText(Path.Combine(RepoPaths.Root, "AGENTS.md"));
        Assert.Contains("--shortcut-mod=rctrl+ralt", agents);
        Assert.Contains("protocolVersion", agents);
        Assert.Contains("REX.bat agent capabilities", agents);
    }

    [Fact]
    public void PagesSiteHasNoBrokenLocalAnchorsOrAssets()
    {
        var docs = Path.Combine(RepoPaths.Root, "docs");
        var html = File.ReadAllText(Path.Combine(docs, "index.html"));

        var ids = Regex.Matches(html, "id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(ids.Count, Regex.Matches(html, "id=\"([^\"]+)\"").Count);

        var anchors = Regex.Matches(html, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.All(anchors, a => Assert.Contains(a, ids));

        var assets = Regex.Matches(html, "(?:href|src)=\"([^\"#]+)\"").Select(m => m.Groups[1].Value)
            .Where(a => !a.Contains("://", StringComparison.Ordinal))
            .ToArray();
        Assert.All(assets, a => Assert.True(File.Exists(Path.Combine(docs, a.Split('?')[0])), $"docs/{a} is referenced but missing"));

        Assert.DoesNotMatch("(?i)on(click|load|error)=", html);
    }
}
