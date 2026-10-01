using Rex.Core;
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
