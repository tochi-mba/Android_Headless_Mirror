using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The Controls tab as the person wants it, through the Settings tab: a section hidden and moved,
/// a tile taken out and another added, names off and four to a row, and all of it put back, with
/// the Controls tab following each change.
/// </summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 150_000)]
    public async Task ControlsTab_HideMoveAndChangeTilesThenPutItBack()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupControlsTab");
        ControlsSettings Saved() => ConfigFile.Load(package.Paths.Config).Controls;

        app.Ui.Toggle("section-show-view", on: false);
        await app.WaitUntilAsync(() => !Saved().Sections.Contains("view"), Soon, "the View section to be hidden");
        app.Ui.Invoke("section-up-copies");
        await app.WaitUntilAsync(() => Saved().Sections.IndexOf("copies") == Saved().Sections.IndexOf("zoom") - 1, Soon, "Copies to move above Zoom");

        app.Ui.Invoke("phone-tiles-remove-mute");
        await app.WaitUntilAsync(() => !Saved().PhoneTiles.Contains("mute"), Soon, "Mute to come out of the Phone grid");
        app.Ui.SelectComboItem("phone-tiles-add-choice", "Fit the window");
        app.Ui.Invoke("phone-tiles-add");
        await app.WaitUntilAsync(() => Saved().PhoneTiles.LastOrDefault() == "fit-window", Soon, "Fit the window to join the Phone grid");
        app.Ui.Toggle("ControlsTileLabels", on: false);
        app.Ui.SetValue("ControlsColumns", 4);
        await app.WaitUntilAsync(() => Saved() is { TileLabels: false, Columns: 4 }, Soon, "names off and four to a row");
        await app.SaveScreenshotAsync("ui-settings-controls-tab.png");

        // The Controls tab follows: no View grid, no Mute, a Fit the window tile.
        app.Ui.Select("TabControls");
        await app.WaitUntilAsync(() => app.Ui.Exists("tile-fit-window"), Soon, "the new tile in the Controls tab");
        Assert.False(app.Ui.Exists("tile-mute"));
        Assert.False(app.Ui.Exists("tile-screenshot"));
        await app.SaveScreenshotAsync("ui-controls-tab-yours.png");

        app.Ui.Select("TabSettings");
        app.Ui.Invoke("ControlsPutBack");
        await app.WaitUntilAsync(() => Saved().PhoneTiles.SequenceEqual(ControlsSettings.DefaultPhoneTiles) && Saved().Sections.Contains("view"), Soon,
            "the Controls tab to be put back");
        await app.QuitAsync();
    }

    /// <summary>Each section's small settings button opens the Settings tab at that feature's own group.</summary>
    [Fact(Timeout = 75_000)]
    public async Task ControlsTab_EachSectionLinksToItsSettings()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabControls");

        app.Ui.Invoke("section-settings-copies");
        await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == "settings", Soon, "the Settings tab");
        await app.WaitUntilAsync(() => app.Ui.IsExpanded("GroupCopies"), Soon, "the Copies group to open");
        Assert.False(app.Ui.Read("CopiesMost", i => i.IsOffscreen));

        // Back on Controls, the keyboard section reaches its own group the same way. The pattern
        // section is not tried: it only shows once a phone is known to have a pattern lock.
        app.Ui.Select("TabControls");
        app.Ui.Invoke("section-settings-keyboard");
        await app.WaitUntilAsync(() => app.Ui.IsExpanded("GroupInput"), Soon, "the input group to open");
        await app.QuitAsync();
    }
}
