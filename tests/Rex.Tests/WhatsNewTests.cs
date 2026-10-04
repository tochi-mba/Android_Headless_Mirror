using Rex.Core;
using Rex.Tests.Support;
using Rex.Mirror.Services;
using Rex.Mirror.Views;

namespace Rex.Tests;

/// <summary>When the window says what is new, and where its Help buttons lead.</summary>
public sealed class WhatsNewTests
{
    [Theory]
    [InlineData("2.3.2", "2.3.3", true, true)]
    [InlineData("2.3.3", "2.3.3", true, false)]
    [InlineData("2.4.0", "2.3.3", true, false)]
    [InlineData("", "2.3.3", true, false)]
    [InlineData(null, "2.3.3", true, false)]
    [InlineData("not a version", "2.3.3", true, false)]
    [InlineData("2.3.2", "garbled", true, false)]
    [InlineData("2.3.2", "2.3.3", false, false)]
    [InlineData("2.3.9", "2.10.0", true, true)]
    public void WhatsNewIsOfferedOnlyAfterAnUpdate(string? lastRun, string current, bool enabled, bool offered) =>
        Assert.Equal(offered, WhatsNew.ShouldOffer(lastRun, current, enabled));

    [Fact]
    public void UpdateOnboardingContainsOnlyFeatureReleasesThePersonMissed()
    {
        Assert.Equal(["Profiles for the way you use the mirror"],
            WhatsNew.FeaturesBetween("2.8.0", "2.9.0").Select(feature => feature.Title));
        Assert.Equal(["Every app, by name", "Send files straight to the phone", "A second screen for one app", "Profiles for the way you use the mirror"],
            WhatsNew.FeaturesBetween("2.5.0", "2.9.0").Select(feature => feature.Title));
        Assert.Empty(WhatsNew.FeaturesBetween("2.3.3", "2.3.4"));
        Assert.Empty(WhatsNew.FeaturesBetween(null, "2.9.0"));
        Assert.Empty(WhatsNew.FeaturesBetween("3.0.0", "2.9.0"));
        Assert.All(WhatsNew.Features, feature =>
        {
            Assert.True(Version.TryParse(feature.Version, out _));
            Assert.False(string.IsNullOrWhiteSpace(feature.Title));
            Assert.False(string.IsNullOrWhiteSpace(feature.Body));
            Assert.False(string.IsNullOrWhiteSpace(feature.Where));
            Assert.False(string.IsNullOrWhiteSpace(feature.Target));
        });
    }

    [Fact]
    public void EveryReleaseThatAddedSomethingHasItsCard()
    {
        // From the first release with a card on, a version whose notes add anything must teach it
        // after an update, or the people who update would never hear of it.
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, "CHANGELOG.md"));
        var adding = new List<string>();
        string? version = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                version = line[3..].Split(' ')[0];
            }
            else if (line == "### Added" && version is not null && Version.Parse(version) >= Version.Parse(WhatsNew.Features[0].Version))
            {
                adding.Add(version);
            }
        }

        Assert.NotEmpty(adding);
        Assert.All(adding, v => Assert.Contains(WhatsNew.Features, feature => feature.Version == v));
        Assert.Equal(WhatsNew.Features.OrderBy(f => Version.Parse(f.Version)).Select(f => f.Version), WhatsNew.Features.Select(f => f.Version));
    }

    [Fact]
    public void EveryHelpButtonLeadsToItsOwnPage()
    {
        Assert.Equal(SiteLinks.Guide, InfoPanel.HelpLink("guide").Url);
        Assert.Equal(SiteLinks.SettingsPage, InfoPanel.HelpLink("settings").Url);
        Assert.Equal(SiteLinks.ShortcutsPage, InfoPanel.HelpLink("shortcuts").Url);
        Assert.Equal(SiteLinks.ChangelogFor(CommandRouter.AppVersion), InfoPanel.HelpLink("changelog").Url);
        Assert.Equal(SiteLinks.NewIssue(CommandRouter.AppVersion), InfoPanel.HelpLink("report").Url);
        Assert.All(new[] { "guide", "settings", "shortcuts", "changelog", "report" }, tag => Assert.False(string.IsNullOrWhiteSpace(InfoPanel.HelpLink(tag).What)));
    }

    [Fact]
    public void OnlyTheAppsOwnSecureAddressesAreOpened()
    {
        var log = new RexLog(Path.Combine(Path.GetTempPath(), "rex-tests-" + Guid.NewGuid().ToString("N"), "mirror.log"), new LoggingSettings { Enabled = false });
        Assert.Throws<ArgumentException>(() => UrlOpener.Open("http://example.com", log));
        Assert.Throws<ArgumentException>(() => UrlOpener.Open("file:///C:/Windows/System32/calc.exe", log));
    }
}
