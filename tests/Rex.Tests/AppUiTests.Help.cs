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
            // An update with things to learn opens its own onboarding: every feature since 1.0.0.
            await first.WaitUntilAsync(() => first.Ui.Exists("UpdateOnboarding"), Soon, "the update-only onboarding");
            Assert.Equal("What changed since 1.0.0", first.Ui.Read("UpdateTitle", e => e.Name));
            Assert.All(WhatsNew.Features, feature => Assert.True(first.Ui.ExistsNamed(feature.Title), feature.Title));
            Assert.False(first.Ui.Exists("NoticeWhatsNew"));
            await first.SaveScreenshotAsync("ui-update-onboarding.png");
            first.Ui.Invoke("UpdateFullNotes");
            await first.WaitUntilAsync(() => package.OpenedPages().Contains(SiteLinks.ChangelogFor(CommandRouter.AppVersion)), Soon, "this version's changes to open");
            await first.WaitUntilAsync(() => !first.Ui.Exists("UpdateOnboarding"), Soon, "the update-only onboarding to go");
            Assert.Equal(CommandRouter.AppVersion, new StateStore(package.Paths.State).Ui.LastRunVersion);
            await first.QuitAsync();
        }

        // Once per version: the next start says nothing.
        using (var second = new AppProcess(package))
        {
            await second.WaitForPhaseAsync("mirroring", Startup);
            Assert.False(second.Ui.Exists("NoticeWhatsNew"));
            Assert.False(second.Ui.Exists("UpdateOnboarding"));
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
        Assert.False(third.Ui.Exists("UpdateOnboarding"));
        await third.WaitUntilAsync(() => new StateStore(package.Paths.State).Ui.LastRunVersion == CommandRouter.AppVersion, Soon, "this version to be remembered");
        await third.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task WhatsNew_ToursOnlyWhatThePersonDidNotHaveYet()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        var state = new StateStore(package.Paths.State);
        state.SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        state.SetUi(state.Ui with { LastRunVersion = "2.8.0" });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        // Coming from 2.8.0, only profiles are new: the sheet has that one card and nothing else.
        await app.WaitUntilAsync(() => app.Ui.Exists("UpdateOnboarding"), Soon, "the update-only onboarding");
        Assert.Equal($"New in {CommandRouter.AppVersion}", app.Ui.Read("UpdateTitle", e => e.Name));
        await app.WaitForStatusAsync(s => s["updateOnboarding"]!.GetValue<bool>() && !s["pictureShown"]!.GetValue<bool>(), Soon, "the sheet over the mirror");
        Assert.True(app.Ui.ExistsNamed("Profiles for the way you use the mirror"));
        Assert.False(app.Ui.ExistsNamed("A second screen for one app"));

        // Showing it around is a tour of that one feature, in place, and the update counts as seen.
        app.Ui.Invoke("UpdateTour");
        var touring = await app.WaitForStatusAsync(s => s["tour"]!["visible"]!.GetValue<bool>(), Soon, "the update's tour");
        Assert.Equal(1, touring["tour"]!["steps"]!.GetValue<int>());
        Assert.False(touring["updateOnboarding"]!.GetValue<bool>());
        Assert.Equal("Profiles for the way you use the mirror", app.Ui.Read("StepTitle", e => e.Name));
        Assert.Contains("Ctrl+Alt+F1 to F9", app.Ui.Read("StepBody", e => e.Name), StringComparison.Ordinal);
        await app.SaveScreenshotAsync("ui-update-tour.png");
        app.Ui.Invoke("NextButton");
        await app.WaitForStatusAsync(s => !s["tour"]!["visible"]!.GetValue<bool>() && s["pictureShown"]!.GetValue<bool>(), Soon, "the tour to end and the mirror to come back");
        Assert.Equal(CommandRouter.AppVersion, new StateStore(package.Paths.State).Ui.LastRunVersion);
        await app.QuitAsync();
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
