using System.Windows.Automation;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The Profiles group through real clicks: saving the settings you have now with only the groups
/// you keep, applying a profile, the presets saying what they change, a card's menu, deleting with
/// a question first, and every rule and option saving.
/// </summary>
public sealed partial class AppUiTests
{
    private static ProfileStore ProfilesIn(TestPackage package) => ProfileStore.For(package.Paths);

    /// <summary>Opens a profile card's menu with its own button and picks an item from it.</summary>
    private static void PickFromProfileMenu(AppProcess app, int number, string item)
    {
        app.Ui.Invoke("profile-menu-" + number);
        var items = app.Ui.OpenMenuItems();
        var wanted = items.FirstOrDefault(i => i.Current.Name == item)
            ?? throw new InvalidOperationException($"No '{item}' in the menu: " + string.Join(" | ", items.Select(i => i.Current.Name)));
        ((InvokePattern)wanted.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    private static async Task<AppProcess> OpenProfilesAsync(TestPackage package)
    {
        var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupProfiles");
        return app;
    }

    [Fact(Timeout = 120_000)]
    public async Task Profiles_SaveWhatYouKeepAndApplyItLater()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Mirror.MaxFps = 30;
            c.Sound.Volume = 0.5;
        });
        using var app = await OpenProfilesAsync(package);
        Assert.Equal("Now: your own settings", app.Ui.Read("ProfilesNow", e => e.Name));

        // The form ticks the groups that differ from how the app ships; Sound is left out by hand.
        app.Ui.Invoke("ProfilesSave");
        await app.WaitUntilAsync(() => app.Ui.Exists("ProfileName"), Soon, "the save form");
        Assert.True(app.Ui.IsOn("profile-group-Mirror"));
        Assert.True(app.Ui.IsOn("profile-group-Sound"));
        Assert.False(app.Ui.IsOn("profile-group-Zoom"));
        app.Ui.Toggle("profile-group-Sound", on: false);
        app.Ui.SetText("ProfileName", "bad/name");
        await app.WaitUntilAsync(() => !app.Ui.Read("ProfilesSaveConfirm", e => e.IsEnabled), Soon, "a bad name to be refused");
        Assert.StartsWith("Use letters", app.Ui.Read("ProfileNameProblem", e => e.Name), StringComparison.Ordinal);
        app.Ui.SetText("ProfileName", "Smooth");
        app.Ui.Invoke("ProfilesSaveConfirm");
        await app.WaitUntilAsync(() => ProfilesIn(package).Load("Smooth") is not null, Soon, "the profile to be saved");
        Assert.Equal(["Mirror.MaxFps"], ProfilesIn(package).Load("Smooth")!.Settings.Keys);
        await app.SaveScreenshotAsync("ui-profiles-saved.png");

        // Changed since, then applied: its own setting comes back and the sound stays as it is now.
        package.EditConfig(c =>
        {
            c.Mirror.MaxFps = 60;
            c.Sound.Volume = 0.8;
        });
        await app.WaitForStatusAsync(s => s["configReloads"]!.GetValue<int>() > 0, Soon, "the change to arrive");
        app.Ui.Invoke("profile-apply-1");
        var applied = await app.WaitForStatusAsync(s => s["profiles"]!["manual"]!.GetValue<string>() == "Smooth", Soon, "the profile to apply");
        var config = ConfigFile.Load(package.Paths.Config);
        Assert.Equal(30, config.Mirror.MaxFps);
        Assert.Equal(0.8, config.Sound.Volume);
        // Back to the frame rate the mirror started with, so nothing waits for a restart.
        Assert.False(applied["restartRequired"]!.GetValue<bool>());
        Assert.Equal("Now: Smooth", app.Ui.Read("ProfilesNow", e => e.Name));

        // Its menu says what it changes, in the Settings tab's own words.
        PickFromProfileMenu(app, 1, "Show what it changes");
        await app.WaitUntilAsync(() => app.Ui.Exists("profile-changes-1"), Soon, "what it changes");
        Assert.Equal("Frame rate: 30", app.Ui.Read("profile-changes-1", e => e.Name));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Profiles_PresetsSayWhatTheyChangeBeforeAnythingHappens()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenProfilesAsync(package);
        Assert.True(app.Ui.Exists("ProfilesEmpty"));

        app.Ui.SelectComboItem("ProfilesPreset", "Gaming");
        await app.WaitUntilAsync(() => app.Ui.Read("ProfilesPresetChanges", e => e.Name).Contains("Frame rate: 120", StringComparison.Ordinal), Soon, "the preset's changes");
        Assert.Equal(new RexConfig().Mirror.MaxFps, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);

        app.Ui.Invoke("ProfilesPresetSave");
        await app.WaitUntilAsync(() => ProfilesIn(package).Load("Gaming") is not null, Soon, "the preset saved as a profile");
        Assert.False(app.Ui.Exists("ProfilesEmpty"));
        app.Ui.Invoke("ProfilesPresetSave");
        await app.WaitUntilAsync(() => ProfilesIn(package).Load("Gaming (mine)") is not null, Soon, "a second copy under its own name");

        app.Ui.Invoke("ProfilesPresetApply");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Mirror.MaxFps == 120, Soon, "the preset to apply");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Profiles_DeletingAsksFirst()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        ProfilesIn(package).Save(new Profile("Films", new Dictionary<string, System.Text.Json.Nodes.JsonNode> { ["Mirror.MaxSize"] = 0 }, DateTimeOffset.UnixEpoch));
        using var app = await OpenProfilesAsync(package);

        PickFromProfileMenu(app, 1, "Delete…");
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", e => e.Name) == "Delete", Soon, "the question");
        app.Ui.Invoke("ConfirmCancel");
        await app.WaitUntilAsync(() => !app.Ui.Exists("ConfirmAccept"), Soon, "the question to go");
        Assert.NotNull(ProfilesIn(package).Load("Films"));

        PickFromProfileMenu(app, 1, "Delete…");
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", e => e.Name) == "Delete", Soon, "the question again");
        app.Ui.Invoke("ConfirmAccept");
        await app.WaitUntilAsync(() => ProfilesIn(package).Load("Films") is null, Soon, "the profile to go");
        await app.WaitUntilAsync(() => app.Ui.Exists("ProfilesEmpty"), Soon, "the empty list");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Settings_ProfilesRulesAndOptionsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenProfilesAsync(package);

        app.Ui.SelectComboItem("ProfilesFullscreen", "Quiet (preset)");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Profiles.WhenFullscreen == "Quiet", Soon, "the fullscreen rule");
        app.Ui.SelectComboItem("ProfilesAfter", "Restart the mirror at once");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Profiles.AfterApplying == "restart", Soon, "restarting at once");

        foreach (var (id, read) in new (string, Func<ProfilesSettings, bool>)[]
                 {
                     ("ProfilesPerPhone", p => p.PerPhone), ("ProfilesPutBack", p => p.PutBack), ("ProfilesAnnounce", p => p.Announce),
                     ("ProfilesKeys", p => p.Keys), ("ProfilesInTray", p => p.InTray),
                 })
        {
            Assert.True(app.Ui.IsOn(id), id);
            app.Ui.Toggle(id, on: false);
            await app.WaitUntilAsync(() => !read(ConfigFile.Load(package.Paths.Config).Profiles), Soon, id + " to save");
        }

        // A PC with no battery (no power file here) cannot have a battery rule, and says so.
        Assert.False(app.Ui.Read("ProfilesBattery", e => e.IsEnabled));
        Assert.Equal("This PC has no battery.", app.Ui.Read("ProfilesBattery", e => e.HelpText));
        await app.QuitAsync();
    }
}
