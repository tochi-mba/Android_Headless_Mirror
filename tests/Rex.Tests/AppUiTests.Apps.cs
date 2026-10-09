using System.Text.Json.Nodes;
using System.Windows.Automation;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The Apps tab through the window: finding and opening an app, favourites as tiles and keys,
/// system and hidden apps, each app's menu, the states of the tab, and its settings.
/// </summary>
public sealed partial class AppUiTests
{
    private const byte KeyShift = 0x10, KeyCtrl = 0x11, KeyAlt = 0x12, KeyF10 = 0x79;

    private static JsonNode AppsOf(JsonObject status) => status["apps"]!;

    /// <summary>The app mirrored and the phone's apps read, as they are once a phone connects.</summary>
    private static async Task<AppProcess> StartWithAppsAsync(TestPackage package)
    {
        var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitForStatusAsync(s => AppsOf(s)["count"]!.GetValue<int>() == 14, Startup, "the phone's apps");
        return app;
    }

    private static bool Opened(TestPackage package, string app) =>
        package.AdbCalls().Any(c => c.Contains($"am start -n {app}/.MainActivity", StringComparison.Ordinal));

    /// <summary>Opens an app's menu the way the keyboard does, and picks one of its items.</summary>
    private static async Task PickFromMenuAsync(AppProcess app, string row, string item)
    {
        app.Ui.Focus(row);
        await app.PressChordAsync(KeyShift, KeyF10);
        var items = app.Ui.OpenMenuItems();
        var wanted = items.FirstOrDefault(i => i.Current.Name == item)
            ?? throw new InvalidOperationException($"No '{item}' in the menu: " + string.Join(" | ", items.Select(i => i.Current.Name)));
        ((InvokePattern)wanted.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    [Fact(Timeout = 120_000)]
    public async Task Apps_TabOpensSearchesAndOpensAnApp()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        await app.FocusAsync();

        await app.PressCtrlAltKeyAsync((byte)'2');
        await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == "apps", Soon, "the Apps tab");
        Assert.True(app.Ui.Exists("app-yours-com.example.one"));
        Assert.False(app.Ui.Exists("app-system-com.android.settings"));
        Assert.StartsWith("14 apps · read", app.Ui.Read("AppsInfo", i => i.Name), StringComparison.Ordinal);
        await app.SaveScreenshotAsync("ui-apps-tab.png");

        app.Ui.SetText("AppsSearch", "spot");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-matches-com.spotify.music") && !app.Ui.Exists("app-matches-com.example.one"), Soon, "only Spotify to match");
        app.Ui.Invoke("app-matches-com.spotify.music");
        await app.WaitUntilAsync(() => Opened(package, "com.spotify.music"), Soon, "Spotify to open on the phone");
        await app.WaitForStatusAsync(s => AppsOf(s)["recent"]!.AsArray().Any(r => r!["package"]!.GetValue<string>() == "com.spotify.music"), Soon, "Spotify among the recent apps");

        app.Ui.SetText("AppsSearch", "nothing like this");
        await app.WaitUntilAsync(() => app.Ui.Read("AppsState", i => i.Name).StartsWith("No app is called", StringComparison.Ordinal), Soon, "the empty search");
        Assert.True(app.Ui.Exists("AppsSearchSystem"));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Apps_StarredAppsBecomeTilesAndKeys()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        app.Ui.Select("TabControls");
        Assert.True(app.Ui.Exists("FavouritesHint"));

        app.Ui.Select("TabApps");
        app.Ui.Toggle("app-star-yours-com.example.one", true);
        await app.WaitForStatusAsync(s => AppsOf(s)["favourites"]!.AsArray().Count == 1, Soon, "a favourite");
        Assert.True(app.Ui.Exists("app-favourites-com.example.one"));

        app.Ui.Select("TabControls");
        await app.WaitUntilAsync(() => app.Ui.Exists("favourite-tile-1"), Soon, "the favourite's tile");
        Assert.Equal("Example One", app.Ui.Read("favourite-tile-1", i => i.Name));
        await app.SaveScreenshotAsync("ui-controls-favourites.png");

        await app.FocusAsync();
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyShift, (byte)'1');
        await app.WaitUntilAsync(() => Opened(package, "com.example.one"), Soon, "the first favourite to open");

        app.Ui.Select("TabApps");
        app.Ui.Toggle("app-star-favourites-com.example.one", false);
        await app.WaitForStatusAsync(s => AppsOf(s)["favourites"]!.AsArray().Count == 0, Soon, "no favourite");
        app.Ui.Select("TabControls");
        await app.WaitUntilAsync(() => !app.Ui.Exists("favourite-tile-1"), Soon, "the tile to go");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Apps_SystemAppsFollowTheSetting()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        app.Ui.Select("TabApps");

        app.Ui.SetText("AppsSearch", "settings");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-matches-com.android.settings"), Soon, "a search to find a system app");
        Assert.Equal("Settings, system app", app.Ui.Read("app-matches-com.android.settings", i => i.Name));

        app.Ui.SetText("AppsSearch", string.Empty);
        new ConfigStore(package.Paths.Config).Set("Apps.ShowSystem", "true");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-system-com.android.settings"), Soon, "the system apps' group");
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task Apps_RowMenuDoesTheChores()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        app.Ui.Select("TabApps");
        await app.FocusAsync();

        await PickFromMenuAsync(app, "app-yours-com.example.one", "Close the app");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am force-stop com.example.one", StringComparison.Ordinal)), Soon, "the app to close");

        await PickFromMenuAsync(app, "app-yours-com.example.one", "App info on the phone");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("APPLICATION_DETAILS_SETTINGS -d package:com.example.one", StringComparison.Ordinal)), Soon, "its info page");

        // Uninstalling asks first; Cancel sends nothing.
        await PickFromMenuAsync(app, "app-yours-com.example.two", "Uninstall…");
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", i => i.Name) == "Uninstall", Soon, "the question");
        await app.SaveScreenshotAsync("ui-apps-uninstall.png");
        app.Ui.Invoke("ConfirmCancel");
        await app.FocusAsync();
        await PickFromMenuAsync(app, "app-yours-com.example.two", "Uninstall…");
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", i => i.Name) == "Uninstall", Soon, "the question again");
        app.Ui.Invoke("ConfirmAccept");
        await app.WaitUntilAsync(() => package.AdbCalls().Count(c => c.Contains("pm uninstall com.example.two", StringComparison.Ordinal)) == 1, Soon, "the uninstall");

        // An app that came with the phone is never offered for uninstalling.
        app.Ui.SetText("AppsSearch", "settings");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-matches-com.android.settings"), Soon, "the system app");
        // The confirm sheet and the list read again after uninstalling can leave another window in front.
        await app.FocusAsync();
        app.Ui.Focus("app-matches-com.android.settings");
        await app.PressChordAsync(KeyShift, KeyF10);
        Assert.DoesNotContain(app.Ui.OpenMenuItems(), i => i.Current.Name == "Uninstall…");
        await app.PressChordAsync(0x1B);
        Assert.DoesNotContain(package.AdbCalls(), c => c.Contains("pm uninstall com.android.settings", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Apps_HiddenAppsCanBeShownAgain()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        app.Ui.Select("TabApps");
        await app.FocusAsync();

        await PickFromMenuAsync(app, "app-yours-com.example.two", "Hide from this list");
        await app.WaitUntilAsync(() => !app.Ui.Exists("app-yours-com.example.two"), Soon, "the app to hide");
        Assert.Equal(["com.example.two"], ConfigFile.Load(package.Paths.Config).Apps.Hidden);

        app.Ui.Toggle("AppsShowHiddenToggle", true);
        await app.WaitUntilAsync(() => app.Ui.Exists("app-hidden-com.example.two"), Soon, "the hidden apps");

        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupApps");
        app.Ui.Invoke("AppsShowHidden");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Apps.Hidden.Count == 0, Soon, "every app shown again");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Apps_AFailedReadSaysWhyAndTriesAgain()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.FailListAppsMarker, string.Empty);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitForStatusAsync(s => AppsOf(s)["error"] is not null, Startup, "the read to fail");
        app.Ui.Select("TabApps");
        await app.WaitUntilAsync(() => app.Ui.Read("AppsState", i => i.Name) == "Could not read the phone's apps: Could not find any ADB device", Soon, "the reason");
        await app.SaveScreenshotAsync("ui-apps-failed.png");

        File.Delete(package.FailListAppsMarker);
        app.Ui.Invoke("AppsTryAgain");
        await app.WaitForStatusAsync(s => AppsOf(s)["count"]!.GetValue<int>() == 14 && AppsOf(s)["error"] is null, Startup, "the apps");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-yours-com.example.one"), Soon, "the list");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Apps_ShowAsTilesWhenAsked()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Apps.Layout = "grid");
        using var app = await StartWithAppsAsync(package);
        app.Ui.Select("TabApps");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-yours-com.example.one"), Soon, "the tiles");
        Assert.False(app.Ui.Exists("app-star-yours-com.example.one"));

        // A row of tiles lines up at the top, whatever the length of a name in it.
        var top = app.Ui.Read("app-yours-com.example.longname", i => i.BoundingRectangle.Top);
        Assert.Equal(top, app.Ui.Read("app-yours-com.example.bank", i => i.BoundingRectangle.Top));

        // The two apps called Notes say their packages; an app with a name of its own does not.
        Assert.True(app.Ui.ExistsNamed("com.example.notes"));
        Assert.True(app.Ui.ExistsNamed("org.other.notes"));
        Assert.False(app.Ui.ExistsNamed("com.example.bank"));
        await app.SaveScreenshotAsync("ui-apps-grid.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Settings_AppsRowsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupApps");

        foreach (var id in new[] { "AppsShowSystem", "AppsInHud", "AppsOpenFresh", "AppsCloseOnStop" })
        {
            app.Ui.Toggle(id, true);
        }

        foreach (var id in new[] { "AppsShowPackages", "AppsShowRecent", "AppsOnControls", "AppsKeys", "AppsReadOnConnect" })
        {
            app.Ui.Toggle(id, false);
        }

        app.Ui.SelectComboItem("AppsSortBy", "Opened most first");
        app.Ui.SelectComboItem("AppsLayout", "Tiles");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Apps is
        {
            ShowSystem: true, FavouritesInHud: true, OpenFresh: true, CloseWhenMirrorStops: true,
            ShowPackages: false, ShowRecent: false, FavouritesOnControls: false, FavouriteKeys: false, ReadOnConnect: false,
            SortBy: "most-used", Layout: "grid",
        }, Soon, "every Apps row saved");

        // The counts follow their switches.
        Assert.False(app.Ui.Read("AppsRecentCount", i => i.IsEnabled));
        Assert.False(app.Ui.Read("AppsOnControlsMost", i => i.IsEnabled));
        app.Ui.Toggle("AppsShowRecent", true);
        app.Ui.Toggle("AppsOnControls", true);
        app.Ui.SetValue("AppsRecentCount", 7);
        app.Ui.SetValue("AppsOnControlsMost", 9);
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Apps is { RecentCount: 7, FavouritesOnControlsMost: 9 }, Soon, "the counts saved");
        Assert.False(app.Ui.Read("AppsShowHidden", i => i.IsEnabled));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Tabs_KeysMatchTheirPlaces()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.FocusAsync();
        foreach (var (key, tab) in new[] { ('2', "apps"), ('3', "phone"), ('4', "settings"), ('5', "info"), ('1', "controls") })
        {
            await app.PressCtrlAltKeyAsync((byte)key);
            await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == tab, Soon, $"the {tab} tab");
            await app.FocusAsync();
        }

        await app.QuitAsync();
    }
}
