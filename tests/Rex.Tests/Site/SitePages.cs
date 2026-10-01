namespace Rex.Tests.Site;

/// <summary>
/// The four pages of docs/ that are made from the app: what each should contain now. The tests
/// compare the committed pages with this, and rewrite them when REX_WRITE_SITE=1 is set.
/// </summary>
internal static class SitePages
{
    public static IReadOnlyList<(string File, string Key, Func<string, string> Middle)> Generated { get; } =
    [
        ("settings.html", SettingsPageWriter.Key, SettingsPageWriter.Write),
        ("shortcuts.html", ShortcutsPageWriter.Key, _ => ShortcutsPageWriter.Write()),
        ("cli.html", CliPageWriter.Key, _ => CliPageWriter.Write()),
        ("changelog.html", ChangelogPageWriter.Key, ChangelogPageWriter.Write),
    ];

    /// <summary>The page as it should be: the committed file with its generated part made again.</summary>
    public static string Expected(string repoRoot, string file)
    {
        var (_, key, middle) = Generated.Single(page => page.File == file);
        var committed = File.ReadAllText(Path.Combine(repoRoot, "docs", file)).ReplaceLineEndings("\n");
        return SiteHtml.Replace(committed, key, middle(repoRoot));
    }

    public static void WriteAll(string repoRoot)
    {
        foreach (var (file, _, _) in Generated)
        {
            File.WriteAllText(Path.Combine(repoRoot, "docs", file), Expected(repoRoot, file));
        }
    }
}
