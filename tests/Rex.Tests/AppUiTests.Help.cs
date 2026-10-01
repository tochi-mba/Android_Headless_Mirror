using Rex.Core;
using Rex.Mirror.Services;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The Info tab's Help buttons and the note after an update, through UI Automation.</summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 75_000)]
    public async Task Info_HelpButtonsOpenTheRightPages()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabInfo");

        var expected = new (string Button, string Url)[]
        {
            ("HelpGuide", SiteLinks.Guide),
            ("HelpSettings", SiteLinks.SettingsPage),
            ("HelpShortcuts", SiteLinks.ShortcutsPage),
            ("HelpWhatsNew", SiteLinks.ChangelogFor(CommandRouter.AppVersion)),
            ("HelpReport", SiteLinks.NewIssue(CommandRouter.AppVersion)),
        };
        foreach (var (button, url) in expected)
        {
            app.Ui.Invoke(button);
            await app.WaitUntilAsync(() => package.OpenedPages().Contains(url), Soon, $"{button} to open {url}");
        }

        Assert.Equal(expected.Select(e => e.Url), package.OpenedPages());
        await app.SaveScreenshotAsync("ui-info-help.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task WhatsNew_IsOfferedOnceAfterAnUpdate()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        var state = new StateStore(package.Paths.State);
        // The lock-screen question outranks the note; answer it first, as someone who has used the app has.
        state.SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        state.SetUi(state.Ui with { LastRunVersion = "1.0.0" });

        using (var first = new AppProcess(package))
        {
            await first.WaitForPhaseAsync("mirroring", Startup);
            await first.WaitUntilAsync(() => first.Ui.Exists("NoticeWhatsNew"), Soon, "the note about the update");
            Assert.Equal($"Updated to {CommandRouter.AppVersion}.", first.Ui.Read("NoticeTitle", e => e.Name));
            await first.SaveScreenshotAsync("ui-whats-new.png");
            first.Ui.Invoke("NoticeWhatsNew");
            await first.WaitUntilAsync(() => package.OpenedPages().Contains(SiteLinks.ChangelogFor(CommandRouter.AppVersion)), Soon, "this version's changes to open");
            await first.WaitUntilAsync(() => !first.Ui.Exists("NoticeWhatsNew"), Soon, "the note to go");
            Assert.Equal(CommandRouter.AppVersion, new StateStore(package.Paths.State).Ui.LastRunVersion);
            await first.QuitAsync();
        }

        // Once per version: the next start says nothing.
        using (var second = new AppProcess(package))
        {
            await second.WaitForPhaseAsync("mirroring", Startup);
            Assert.False(second.Ui.Exists("NoticeWhatsNew"));
            await second.QuitAsync();
        }

        // Switched off, an update passes quietly and is still remembered.
        state.SetUi(state.Ui with { LastRunVersion = "1.0.0" });
        var config = ConfigFile.Load(package.Paths.Config);
        config.App.ShowWhatsNew = false;
        ConfigFile.Save(package.Paths.Config, config);
        using var third = new AppProcess(package);
        await third.WaitForPhaseAsync("mirroring", Startup);
        Assert.False(third.Ui.Exists("NoticeWhatsNew"));
        await third.WaitUntilAsync(() => new StateStore(package.Paths.State).Ui.LastRunVersion == CommandRouter.AppVersion, Soon, "this version to be remembered");
        await third.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_SayingWhatIsNewCanBeSwitchedOff()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupStartup");
        Assert.True(app.Ui.IsOn("ShowWhatsNew"));
        app.Ui.Toggle("ShowWhatsNew", on: false);
        await app.WaitUntilAsync(() => !ConfigFile.Load(package.Paths.Config).App.ShowWhatsNew, Soon, "the choice to be saved");
        await app.QuitAsync();
    }
}
