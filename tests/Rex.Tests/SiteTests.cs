using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rex.Cli;
using Rex.Core;
using Rex.Tests.Site;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The website and the documentation say what the app does, and cannot drift from it: the pages
/// made from the app must match it, and every link the app opens must exist. Run with
/// REX_WRITE_SITE=1 and the comparisons write the pages instead.
/// </summary>
public sealed partial class SiteTests
{
    private static readonly bool Writing = Environment.GetEnvironmentVariable("REX_WRITE_SITE") == "1";

    private static string Docs(string file) => Path.Combine(RepoPaths.Root, "docs", file);

    private static void AssertPageIsCurrent(string file)
    {
        var expected = SitePages.Expected(RepoPaths.Root, file);
        if (Writing)
        {
            File.WriteAllText(Docs(file), expected);
            return;
        }

        var committed = File.ReadAllText(Docs(file)).ReplaceLineEndings("\n");
        Assert.True(committed == expected,
            $"docs/{file} is out of date with the app. Run the site tests with REX_WRITE_SITE=1 to rewrite it, and commit the result.");
    }

    private static HashSet<string> Ids(string file) =>
        IdPattern().Matches(File.ReadAllText(Docs(file))).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("\\sid=\"([^\"]+)\"")]
    private static partial Regex IdPattern();

    [Fact]
    public void TheSettingsPageDescribesEverySettingTheAppHas()
    {
        AssertPageIsCurrent("settings.html");
        var ids = Ids("settings.html");
        var everyPath = SettingsCatalogue.Controls.Keys.Concat(SettingsCatalogue.NotYet).Concat(SettingsCatalogue.Elsewhere.Keys);
        Assert.All(everyPath, path => Assert.Contains(path, ids));
        Assert.All(SettingsReference.Read(RepoPaths.Root), group => Assert.Contains(SettingsPageWriter.GroupId(group.Name), ids));
    }

    [Fact]
    public void EveryControlTheSettingsPageDescribesHasItsWords()
    {
        var rows = SettingsReference.Read(RepoPaths.Root).SelectMany(group => group.Rows).ToArray();
        Assert.Equal(SettingsCatalogue.Controls.Count, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Label), $"{row.Path} has no label");
            Assert.NotEqual(row.Control, row.Label);
            if (row.Kind == "choice")
            {
                Assert.True(row.Choices.Count > 1, $"{row.Path} offers no choices");
                Assert.Contains(row.Choices, choice => choice.Label == SettingsReference.DefaultText(row));
            }

            if (row.Kind == "slider")
            {
                Assert.True(row.Minimum < row.Maximum, $"{row.Path} has no range");
            }
        });
    }

    /// <summary>Settings that only reach scrcpy while another setting is on, and the value that turns it on.</summary>
    private static readonly Dictionary<string, (string Path, string Value)> NeedsFirst = new(StringComparer.Ordinal)
    {
        ["Input.KeyRepeat"] = ("Mirror.CompatibilityKeyboard", "true"),
        ["Mirror.RecordFormat"] = ("Mirror.RecordOnStart", "true"),
        ["Mirror.RecordDirectory"] = ("Mirror.RecordOnStart", "true"),
    };

    /// <summary>A value to try for settings that take text or have no control to read choices from.</summary>
    private static readonly Dictionary<string, string[]> TryValues = new(StringComparer.Ordinal)
    {
        ["Session.StartApp"] = ["com.example.player"],
        ["Mirror.ExtraArgs"] = ["--no-mipmaps"],
        ["Mirror.AudioCodec"] = ["aac", "flac", "raw"],
        ["Mirror.AudioBufferMs"] = ["120"],
        ["Mirror.RecordDirectory"] = ["elsewhere"],
        ["Session.KeepActive"] = ["false"],
    };

    [Fact]
    public void EverySettingSaysWhenItAppliesAndItIsTrue()
    {
        var rows = SettingsReference.Read(RepoPaths.Root).SelectMany(group => group.Rows).ToDictionary(row => row.Path, StringComparer.Ordinal);
        var paths = SettingsCatalogue.Controls.Keys.Concat(SettingsCatalogue.NotYet);
        foreach (var path in paths)
        {
            var values = TryValues.TryGetValue(path, out var given) ? given
                : rows.TryGetValue(path, out var row) ? SettingsReference.OtherValues(row).ToArray()
                : SettingsReference.Default(path)?.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.True => ["false"],
                    System.Text.Json.JsonValueKind.False => ["true"],
                    System.Text.Json.JsonValueKind.Number => [(SettingsReference.Default(path)!.GetValue<double>() + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    _ => [],
                };
            (string, string)[] first = NeedsFirst.TryGetValue(path, out var needed) ? [needed] : [];
            var changesLaunch = values.Any(value => SettingsReference.ChangesLaunch(path, value, first));
            Assert.True(changesLaunch == SettingsCatalogue.AppliesAtNextStart(path),
                $"{path} says it applies {(SettingsCatalogue.AppliesAtNextStart(path) ? "at the next start" : "at once")}, but changing it {(changesLaunch ? "changes" : "does not change")} what scrcpy is started with.");
        }
    }

    [Fact]
    public void TheShortcutsPageListsEveryShortcut()
    {
        AssertPageIsCurrent("shortcuts.html");
        var ids = Ids("shortcuts.html");
        Assert.All(Shortcuts.All, shortcut => Assert.Contains("key-" + shortcut.Id, ids));
    }

    [Fact]
    public void TheCommandLinePageListsEveryCommand()
    {
        AssertPageIsCurrent("cli.html");
        var html = File.ReadAllText(Docs("cli.html"));
        Assert.All(CliReference.All, command => Assert.Contains(SiteHtml.Escape(command.Usage), html, StringComparison.Ordinal));
    }

    /// <summary>The command words a dispatcher's switch accepts, read from its source, less the aliases.</summary>
    private static string[] Dispatched(string file, string start, string end, Regex word, params string[] aliases)
    {
        var source = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Rex.Cli", file));
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var block = source[from..source.IndexOf(end, from, StringComparison.Ordinal)];
        return word.Matches(block).Select(m => m.Groups[1].Value).Except(aliases).Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex("case \"([a-z-]+)\":")]
    private static partial Regex HumanCase();

    [GeneratedRegex("\"([a-z-]+)\"(?: or \"[a-z-]+\")* =>")]
    private static partial Regex MachineCase();

    [Fact]
    public void TheReferenceHasExactlyTheCommandsRexAccepts()
    {
        var human = Dispatched("Commands.cs", "switch (command)", "default:", HumanCase(), "start", "--help", "-h");
        Assert.Equal(human, CliReference.HumanCommands.Order(StringComparer.Ordinal));

        var machine = Dispatched("MachineMode.cs", "return command switch", "_ => throw", MachineCase());
        Assert.Equal(machine, CliReference.MachineCommands.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheChangelogHasAnEntryForThisVersion()
    {
        AssertPageIsCurrent("changelog.html");
        var versions = ChangelogPageWriter.Versions(RepoPaths.Root);
        var props = File.ReadAllText(Path.Combine(RepoPaths.Root, "Directory.Build.props"));
        var current = Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal(current, versions[0]);
        var numbers = versions.Select(Version.Parse).ToArray();
        for (var i = 1; i < numbers.Length; i++)
        {
            Assert.True(numbers[i - 1] > numbers[i], $"{numbers[i - 1]} is listed above {numbers[i]}; newest comes first, once.");
        }

        var ids = Ids("changelog.html");
        Assert.All(versions, version => Assert.Contains(SiteLinks.ChangelogAnchor(version), ids));
    }

    [Fact]
    public void AgentsMdNamesEveryMachineCommand()
    {
        var agents = File.ReadAllText(Path.Combine(RepoPaths.Root, "AGENTS.md"));
        Assert.All(CliReference.MachineCommands, command => Assert.Contains($"`{command}", agents, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(Ctrl\+Alt\+(?:Shift\+)?[A-Za-z0-9]+|\bF\d{1,2}\b|Alt \+ [a-z]+)")]
    private static partial Regex KeyMention();

    [Fact]
    public void TheReadmeOnlyMentionsKeysTheAppHas()
    {
        var readme = File.ReadAllText(Path.Combine(RepoPaths.Root, "README.md"));
        var known = Shortcuts.All.Select(s => s.Gesture).ToHashSet(StringComparer.Ordinal);
        var unknown = KeyMention().Matches(readme).Select(m => m.Value).Where(key => !known.Contains(key)).Distinct().ToArray();
        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryLinkTheAppOpensExistsOnTheSite()
    {
        foreach (var url in new[] { SiteLinks.Features, SiteLinks.Guide, SiteLinks.ShortcutsPage, SiteLinks.SettingsPage, SiteLinks.CommandLine, SiteLinks.Help, SiteLinks.Changelog })
        {
            Assert.StartsWith(SiteLinks.Site, url, StringComparison.Ordinal);
            Assert.True(File.Exists(Docs(url[SiteLinks.Site.Length..])), $"{url} has no page in docs/");
        }

        var setting = SiteLinks.Setting("Mirror.MaxFps");
        Assert.Contains(setting[(setting.IndexOf('#', StringComparison.Ordinal) + 1)..], Ids("settings.html"));
        var version = ChangelogPageWriter.Versions(RepoPaths.Root)[0];
        Assert.EndsWith("#" + SiteLinks.ChangelogAnchor(version), SiteLinks.ChangelogFor(version), StringComparison.Ordinal);
        Assert.Contains(SiteLinks.ChangelogAnchor(version), Ids("changelog.html"));
        Assert.Contains("report", Ids("help.html"));
        Assert.Equal(SiteLinks.Help + "#report", SiteLinks.HelpTopic("report"));

        var issue = SiteLinks.NewIssue("2.3.3");
        Assert.StartsWith(SiteLinks.Repository + "/issues/new?body=", issue, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Version: 2.3.3"), issue, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkdownSubsetRendersWhatTheChangelogUses()
    {
        var html = MarkdownSubset.Render(
        [
            "## 1.2.3 - 2026-01-02",
            "",
            "### Added",
            "",
            "- One with `code` & <angle> and **bold**",
            "  that carries on",
            "  - nested [a link](https://example.com) and [a page](help.html)",
            "- Two",
            "",
            "A paragraph",
            "on two lines.",
        ], (level, text) => level == 2 ? "v1-2-3" : null);

        Assert.Equal(
            "<h2 id=\"v1-2-3\">1.2.3 - 2026-01-02</h2>\n<h3>Added</h3>\n" +
            "<ul>\n<li>One with <code>code</code> &amp; &lt;angle&gt; and <strong>bold</strong> that carries on\n" +
            "<ul>\n<li>nested <a href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">a link</a> and <a href=\"help.html\">a page</a></li>\n</ul>\n" +
            "</li>\n<li>Two</li>\n</ul>\n<p>A paragraph on two lines.</p>\n",
            html);
        Assert.Equal("&lt;b&gt;&amp;&quot;", SiteHtml.Escape("<b>&\""));
        Assert.Equal("hello-world-2", SiteHtml.Slug("  Hello, World! 2 "));
    }

    [Fact]
    public void HelpPrintsEveryCommandFromTheReference()
    {
        var help = CliReference.HelpText();
        Assert.All(CliReference.All.Where(c => c.Human), command => Assert.Contains(command.Usage, help, StringComparison.Ordinal));
        Assert.DoesNotContain(CliReference.All.Where(c => !c.Human), command => help.Contains(command.Usage, StringComparison.Ordinal));
        Assert.Contains(SiteLinks.CommandLine, help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CapabilitiesListTheReferencesMachineCommands()
    {
        using var package = new TestPackage();
        var result = await MachineMode.RunAsync(["capabilities"], new CliContext(package.Paths));
        var commands = JsonNode.Parse(result.Json)!["data"]!["commands"]!.AsArray().Select(x => x!.GetValue<string>());
        Assert.Equal(CliReference.MachineCommands, commands);
    }
}
