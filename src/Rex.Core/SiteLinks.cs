namespace Rex.Core;

/// <summary>
/// Every address the app and the command line send people to. Each page and anchor here exists in
/// docs/, and a test fails if one stops existing.
/// </summary>
public static class SiteLinks
{
    public const string Site = "https://tochi-mba.github.io/Android_Headless_Mirror/";
    public const string Repository = "https://github.com/tochi-mba/Android_Headless_Mirror";

    public const string Features = Site + "features.html";
    public const string Guide = Site + "guide.html";
    public const string ShortcutsPage = Site + "shortcuts.html";
    public const string SettingsPage = Site + "settings.html";
    public const string CommandLine = Site + "cli.html";
    public const string Help = Site + "help.html";
    public const string Changelog = Site + "changelog.html";

    /// <summary>One setting on the settings page; its row's id is its config path.</summary>
    public static string Setting(string configPath) => SettingsPage + "#" + configPath;

    /// <summary>A section of the help page, by its id.</summary>
    public static string HelpTopic(string topic) => Help + "#" + topic;

    /// <summary>One version's entry on the changelog page: 2.3.3 is #v2-3-3.</summary>
    public static string ChangelogFor(string version) => Changelog + "#" + ChangelogAnchor(version);

    public static string ChangelogAnchor(string version) => "v" + version.Replace('.', '-');

    /// <summary>A new GitHub issue with the version filled in and a pointer to the diagnostics.</summary>
    public static string NewIssue(string version) =>
        Repository + "/issues/new?body=" + Uri.EscapeDataString(
            $"Version: {version}\n\nWhat happened:\n\nWhat I expected:\n\n" +
            "Diagnostics (Info tab > Copy diagnostics, or `rex diagnostics`):\n");
}
