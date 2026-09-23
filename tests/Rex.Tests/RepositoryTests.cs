using Rex.Core;
using System.Text.RegularExpressions;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Hygiene rules for the repository itself.</summary>
public sealed class RepositoryTests
{
    private static readonly string[] SourceGlobs = ["*.cs", "*.xaml", "*.ps1", "*.bat", "*.yml", "*.js", "*.css", "*.html", "*.md", "*.json"];
    /// <summary>Generated or third-party folders (all git-ignored) that are not part of the source tree.</summary>
    private static readonly string[] Skip = new[] { "bin", "obj", "node_modules", "tools", "artifacts", "dist", "test-results", "playwright-report", ".git" }
        .Select(name => Path.DirectorySeparatorChar + name + Path.DirectorySeparatorChar)
        .ToArray();

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
    public void OnlyTheLauncherIconAndInstallerScriptsExist()
    {
        var scripts = Directory.EnumerateFiles(RepoPaths.Root, "*.*", SearchOption.AllDirectories)
            .Where(path => !Skip.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Where(path => Path.GetExtension(path) is ".ps1" or ".bat" or ".vbs" or ".py")
            .Select(path => Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/'))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["REX.bat", "assets/make-icon.ps1", "installer/build.ps1", "installer/prepare-upgrade.ps1"], scripts);
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
        Assert.Contains("--shortcut-mod=rctrl", agents);
        Assert.Contains("protocolVersion", agents);
        Assert.Contains("REX.bat agent capabilities", agents);
    }

    [Fact]
    public void Installer_IsVersionedAndKeepsAStableUpgradeIdentity()
    {
        var installer = File.ReadAllText(Path.Combine(RepoPaths.Root, "installer", "AndroidHeadlessMirror.iss"));
        var build = File.ReadAllText(Path.Combine(RepoPaths.Root, "installer", "build.ps1"));
        var release = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "release.yml"));

        Assert.Contains("AppId={{6C1E7A2B-5D3F-4E8A-9B0C-7A2D4F6E8B10}", installer);
        Assert.Contains("OutputBaseFilename=AndroidHeadlessMirror-Setup\n", installer.ReplaceLineEndings("\n"));
        Assert.Contains("function PrepareToInstall", installer);
        Assert.Contains("ignoreversion recursesubdirs", installer);
        Assert.Contains("AndroidHeadlessMirror-Setup.exe", build);
        Assert.Contains("dist/AndroidHeadlessMirror-Setup.exe", release);
    }

    [Fact]
    public void ContinuousDelivery_PublishesEachMainVersionOnce()
    {
        var ci = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));
        var release = File.ReadAllText(Path.Combine(RepoPaths.Root, ".github", "workflows", "release.yml"));

        Assert.Contains("if: github.event_name == 'push' && github.ref == 'refs/heads/main'", ci);
        Assert.Contains("$props.Project.PropertyGroup.Version", ci);
        // Every suite that can block a release is a gate on it, the desktop UI automation included.
        Assert.Contains("needs: [windows, desktop-e2e, desktop-ui, pages]", ci);
        Assert.Contains("--filter-class Rex.Tests.AppUiTests", ci);
        Assert.False(File.Exists(Path.Combine(RepoPaths.Root, "URGENT.md")),
            "URGENT.md tracked a release exception that no longer exists.");
        Assert.Contains("gh release view \"$tag\"", ci);
        Assert.Contains("gh release create \"$tag\"", ci);
        Assert.Contains("--target \"$GITHUB_SHA\"", ci);
        Assert.Contains("sha256sum --check", ci);
        Assert.DoesNotContain("gh release upload", ci);
        Assert.DoesNotContain("--clobber", ci);
        Assert.DoesNotContain("--clobber", release);
        Assert.Contains("Published releases are immutable", release);
    }

    [Fact]
    public void TheSiteAndTheAppAgreeOnEveryShortcut()
    {
        var html = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "index.html"));
        foreach (var shortcut in Shortcuts.All)
        {
            Assert.True(
                html.Contains(">" + shortcut.Gesture + "<", StringComparison.Ordinal),
                $"The website does not list '{shortcut.Gesture}', which the app answers to.");
        }
    }

    [Fact]
    public void EveryPaletteKeyHasAHighContrastAnswer()
    {
        var theme = Keys(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        var contrast = Keys(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "HighContrast.xaml"));

        // Anything the theme paints with must have a system colour to fall back to, or High
        // Contrast keeps that one hardcoded colour and the window stops making sense.
        Assert.Equal(theme, contrast);

        static SortedSet<string> Keys(string path) =>
            [.. Regex.Matches(File.ReadAllText(path), "<SolidColorBrush x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void TheSiteHasAPageForAddressesThatDoNotExist()
    {
        var notFound = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "404.html"));
        Assert.Contains("Android Headless Mirror", notFound, StringComparison.Ordinal);
        Assert.Contains("styles.css", notFound, StringComparison.Ordinal);
        Assert.Contains("noindex", notFound, StringComparison.Ordinal);

        var html = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "index.html"));
        Assert.Contains("og:image", html, StringComparison.Ordinal);
        Assert.Contains("summary_large_image", html, StringComparison.Ordinal);
        Assert.Contains("SoftwareApplication", html, StringComparison.Ordinal);
        Assert.Contains("FAQPage", html, StringComparison.Ordinal);
        Assert.True(new FileInfo(Path.Combine(RepoPaths.Root, "docs", "og.png")).Length > 1000, "The share image must be a real picture.");
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
