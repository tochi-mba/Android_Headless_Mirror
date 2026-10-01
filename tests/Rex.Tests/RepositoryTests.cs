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
        var html = File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "shortcuts.html"));
        foreach (var shortcut in Shortcuts.All)
        {
            Assert.True(
                html.Contains(">" + shortcut.Gesture + "<", StringComparison.Ordinal),
                $"The website does not list '{shortcut.Gesture}', which the app answers to.");
        }
    }

    [Fact]
    public void NoControlIsLeftLookingLikeStockWindows()
    {
        // Every kind of control the app puts on screen has a style of the app's own. The phone
        // picker once came up as a light, square Windows menu in the middle of a dark window,
        // because nothing styled menus; this catches the next control that nobody styled.
        var theme = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        var styled = Regex.Matches(theme, "TargetType=\"([A-Za-z]+)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var source = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal)) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .Aggregate(string.Empty, (all, text) => all + text);
        string[] stock =
        [
            "Button", "CheckBox", "ComboBox", "ComboBoxItem", "Expander", "ScrollBar", "Slider", "TextBox", "ToolTip",
            "ContextMenu", "MenuItem", "ProgressBar", "GridSplitter", "ListBox", "ListBoxItem", "PasswordBox", "Menu",
            "TabControl", "ListView", "DataGrid", "TreeView", "Calendar", "DatePicker", "RichTextBox", "StatusBar", "ToolBar",
        ];
        foreach (var control in stock)
        {
            var used = Regex.IsMatch(source, $"<{control}[\\s>]") || Regex.IsMatch(source, $"new {control}\\b");
            var styledHere = styled.Contains(control) ||
                (control == "GridSplitter" && theme.Contains("TargetType=\"GridSplitter\"", StringComparison.Ordinal)) ||
                (control is "ListBox" or "ListBoxItem" && source.Contains("<Style TargetType=\"ListBoxItem\">", StringComparison.Ordinal));
            Assert.True(!used || styledHere, $"{control} is used but has no style of the app's own, so it will look like stock Windows.");
        }

        // The splitter's style is keyed, so it has to be asked for where the splitter is.
        Assert.Contains("Style=\"{StaticResource PanelSplitter}\"", File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "MainWindow.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void ScrcpysLetterboxIsTheWindowsInk()
    {
        // Anything scrcpy paints around the picture must be the colour of the mirror area itself,
        // or a letterboxed moment shows as bars.
        var theme = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        var ink = Regex.Match(theme, "<Color x:Key=\"InkColor\">(#[0-9A-Fa-f]{6})</Color>").Groups[1].Value;
        Assert.Equal(ink.ToUpperInvariant(), ScrcpyArguments.LetterboxColour.ToUpperInvariant());
    }

    [Fact]
    public void TheTrayMenuUsesThePalette()
    {
        var theme = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Mirror", "Theme.xaml"));
        string Colour(string key) => Regex.Match(theme, $"<Color x:Key=\"{key}\">#([0-9A-Fa-f]{{6}})</Color>").Groups[1].Value.ToUpperInvariant();
        string Hex(System.Drawing.Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
        Assert.Equal(Colour("PanelColor"), Hex(Rex.Mirror.Services.TrayMenuRenderer.Panel));
        Assert.Equal(Colour("LineColor"), Hex(Rex.Mirror.Services.TrayMenuRenderer.Line));
        Assert.Equal(Colour("TextColor"), Hex(Rex.Mirror.Services.TrayMenuRenderer.Text));
        Assert.Equal(Colour("MutedColor"), Hex(Rex.Mirror.Services.TrayMenuRenderer.Muted));
        Assert.Equal(Colour("SignalColor"), Hex(Rex.Mirror.Services.TrayMenuRenderer.Signal));
        Assert.Equal(Regex.Match(theme, "<SolidColorBrush x:Key=\"Hover\" Color=\"#([0-9A-Fa-f]{6})\"").Groups[1].Value.ToUpperInvariant(), Hex(Rex.Mirror.Services.TrayMenuRenderer.Hover));
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
        var pages = Directory.EnumerateFiles(docs, "*.html").Select(Path.GetFileName).Where(name => !name!.StartsWith('_')).ToArray();
        Assert.Contains("index.html", pages);
        var ids = pages.ToDictionary(page => page!, page => Regex.Matches(File.ReadAllText(Path.Combine(docs, page!)), " id=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToArray());

        foreach (var page in pages)
        {
            var html = File.ReadAllText(Path.Combine(docs, page!));
            Assert.True(ids[page!].Length == ids[page!].Distinct(StringComparer.Ordinal).Count(), $"docs/{page} uses an id twice");

            foreach (var link in Regex.Matches(html, "(?:href|src)=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Where(a => !a.Contains("://", StringComparison.Ordinal) && !a.StartsWith("mailto:", StringComparison.Ordinal)))
            {
                var parts = link.Split('#', 2);
                var target = parts[0] switch { "" => page!, "./" => "index.html", var file => file.Split('?')[0] };
                Assert.True(File.Exists(Path.Combine(docs, target)), $"docs/{page} links to {link}, which is missing");
                if (parts.Length == 2 && ids.TryGetValue(target, out var targetIds))
                {
                    Assert.True(targetIds.Contains(parts[1]), $"docs/{page} links to {link}, but {target} has no such id");
                }
            }

            Assert.DoesNotMatch("(?i)<[^>]+\\son[a-z]+=\"", html);
        }
    }
}
