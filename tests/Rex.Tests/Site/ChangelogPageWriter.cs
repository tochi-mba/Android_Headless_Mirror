using System.Text.RegularExpressions;
using Rex.Core;

namespace Rex.Tests.Site;

/// <summary>Writes the middle of docs/changelog.html from CHANGELOG.md.</summary>
internal static partial class ChangelogPageWriter
{
    public const string Key = "changelog";

    /// <summary>A release heading: "## 2.3.3 - 2026-10-01".</summary>
    [GeneratedRegex(@"^(\d+\.\d+\.\d+)(?:\s+-\s+(\d{4}-\d{2}-\d{2}))?")]
    public static partial Regex Release();

    /// <summary>The versions in CHANGELOG.md, in the order they are written (newest first).</summary>
    public static IReadOnlyList<string> Versions(string repoRoot) =>
        File.ReadLines(Path.Combine(repoRoot, "CHANGELOG.md"))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => Release().Match(line[3..]))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToArray();

    public static string Write(string repoRoot)
    {
        // The page has its own title and introduction; the file's own heading and lead-in stay in the file.
        var lines = File.ReadLines(Path.Combine(repoRoot, "CHANGELOG.md"))
            .SkipWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));
        return MarkdownSubset.Render(lines, (level, text) =>
            level == 2 && Release().Match(text) is { Success: true } match ? SiteLinks.ChangelogAnchor(match.Groups[1].Value) : null).TrimEnd();
    }
}
