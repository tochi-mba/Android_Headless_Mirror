using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Every control a user can reach in the window, driven through UI Automation and real input
/// against the fake phone: tabs, toggles, sliders, combos, buttons, the close button, Alt+wheel.
/// </summary>
[Collection("desktop")]
public sealed class AppUiTests
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(8);

    [Fact(Timeout = 75_000)]
    public async Task Tour_ShowsItselfOnceAndCanBeTakenAgain()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, showTour: true);
        using (var app = new AppProcess(package))
        {
            await app.WaitForPhaseAsync("mirroring", Startup);
            var started = await app.WaitForStatusAsync(s => s["tour"]!["visible"]!.GetValue<bool>(), Startup, "the tour");
            Assert.Equal(1, started["tour"]!["step"]!.GetValue<int>());
            Assert.True(started["tour"]!["steps"]!.GetValue<int>() >= 4);
            await app.SaveScreenshotAsync("ui-tour-first-step.png");

            app.Ui.Invoke("NextButton");
            await app.WaitForStatusAsync(s => s["tour"]!["step"]!.GetValue<int>() == 2, Soon, "the second step");
            app.Ui.Invoke("BackButton");
            await app.WaitForStatusAsync(s => s["tour"]!["step"]!.GetValue<int>() == 1, Soon, "the first step again");

            app.Ui.Invoke("SkipButton");
            await app.WaitForStatusAsync(s => !s["tour"]!["visible"]!.GetValue<bool>(), Soon, "the tour to close");
            await app.QuitAsync();
        }

        Assert.True(new StateStore(package.Paths.State).Ui.TourSeenVersion > 0);

        // Seen once is seen: it does not ambush the next launch, but Info can bring it back.
        using var again = new AppProcess(package);
        await again.WaitForPhaseAsync("mirroring", Startup);
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        var quiet = await again.SendAsync(new IpcRequest("status"));
        Assert.False(quiet.Data!["tour"]!["visible"]!.GetValue<bool>());

        again.Ui.Select("TabInfo");
        again.Ui.Invoke("TourButton");
        await again.WaitForStatusAsync(s => s["tour"]!["visible"]!.GetValue<bool>(), Soon, "the tour again");
        await again.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_SearchNarrowsToTheGroupThatMentionsIt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");

        app.Ui.SetText("SettingsFilter", "wash");
        await app.WaitUntilAsync(() => app.Ui.Exists("AmbientTintHue"), Soon, "the soft background group to stay");
        Assert.False(app.Ui.Exists("HudScale"), "A group that does not mention the search should be hidden.");
        await app.SaveScreenshotAsync("ui-settings-search.png");

        app.Ui.SetText("SettingsFilter", "nothing whatsoever");
        await app.WaitUntilAsync(() => !app.Ui.Exists("AmbientTintHue"), Soon, "every group to go");

        app.Ui.SetText("SettingsFilter", string.Empty);
        await app.WaitUntilAsync(() => app.Ui.Exists("AmbientTintHue"), Soon, "the groups to come back");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_ResetEverything_AsksInTheWindowAndCanBeCancelled()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.Toggle("AmbientEnabled", false);
        await app.WaitUntilAsync(() => !ConfigFile.Load(package.Paths.Config).Ambient.Enabled, Soon, "the change to save");

        app.Ui.ExpandGroup("GroupAdvanced");
        app.Ui.Invoke("ResetEverything");
        await app.WaitUntilAsync(() => app.Ui.Exists("ConfirmCancel"), Soon, "the question");
        await app.SaveScreenshotAsync("ui-confirm-sheet.png");

        // Cancelling changes nothing at all.
        app.Ui.Invoke("ConfirmCancel");
        await app.WaitUntilAsync(() => !app.Ui.Exists("ConfirmCancel"), Soon, "the question to close");
        Assert.False(ConfigFile.Load(package.Paths.Config).Ambient.Enabled);

        app.Ui.Invoke("ResetEverything");
        await app.WaitUntilAsync(() => app.Ui.Exists("ConfirmAccept"), Soon, "the question again");
        app.Ui.Invoke("ConfirmAccept");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Ambient.Enabled, Soon, "everything back to how it ships");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Sidebar_WidthIsRememberedAcrossRuns()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { SidebarWidth = 420 });

        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", Startup);
        Assert.Equal(420, status["sidebarWidth"]!.GetValue<double>());

        // Hiding the panel and bringing it back keeps the width, rather than snapping to default.
        app.Ui.Invoke("SidebarToggle");
        await app.WaitForStatusAsync(s => !s["sidebarVisible"]!.GetValue<bool>(), Soon, "the panel to go");
        app.Ui.Invoke("SidebarToggle");
        await app.WaitForStatusAsync(s => s["sidebarWidth"]!.GetValue<double>() == 420, Soon, "the width to come back");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Sidebar_TabsAndToggle_PersistAcrossRuns()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using (var app = new AppProcess(package))
        {
            await app.WaitForPhaseAsync("mirroring", Startup);
            app.Ui.Select("TabSettings");
            await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == "settings", Soon, "settings tab");
            await app.SaveScreenshotAsync("ui-settings-tab.png");
            app.Ui.Invoke("SidebarToggle");
            await app.WaitForStatusAsync(s => !s["sidebarVisible"]!.GetValue<bool>(), Soon, "sidebar hidden");
            await app.SaveScreenshotAsync("ui-sidebar-hidden.png");
            await app.QuitAsync();
        }

        var ui = new StateStore(package.Paths.State).Ui;
        Assert.False(ui.SidebarVisible);
        Assert.Equal("settings", ui.SidebarTab);

        using var again = new AppProcess(package);
        var status = await again.WaitForPhaseAsync("mirroring", Startup);
        Assert.False(status["sidebarVisible"]!.GetValue<bool>());
        Assert.Equal("settings", status["sidebarTab"]!.GetValue<string>());
        await again.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_SoftBackgroundControls_PreviewLiveAndSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        TestPackage.WritePreviewImage(Path.Combine(package.ToolsFolder, "preview.png"));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitForStatusAsync(s => s["ambientVisible"]!.GetValue<bool>(), Startup, "soft background");
        app.Ui.Select("TabSettings");

        app.Ui.Toggle("AmbientEnabled", false);
        await app.WaitForStatusAsync(s => !s["ambientVisible"]!.GetValue<bool>(), Soon, "soft background hidden");
        await app.WaitUntilAsync(() => !ConfigFile.Load(package.Paths.Config).Ambient.Enabled, Soon, "Ambient.Enabled saved");
        app.Ui.Toggle("AmbientEnabled", true);
        await app.WaitForStatusAsync(s => s["ambientVisible"]!.GetValue<bool>(), Soon, "soft background back");

        app.Ui.SetValue("AmbientOpacity", 0.8);
        app.Ui.SetValue("AmbientBlur", 0);
        app.Ui.SetValue("AmbientSize", 1.5);
        app.Ui.SetValue("AmbientOffsetX", -0.4);
        app.Ui.SetValue("AmbientEdgeFade", 0.3);
        app.Ui.SetValue("AmbientTintStrength", 0.5);
        app.Ui.SelectComboItem("AmbientPlacement", "Left of the phone");
        app.Ui.SelectComboItem("AmbientScaling", "Show the whole screen");
        app.Ui.Toggle("AmbientFlip", true);
        await app.WaitUntilAsync(() =>
        {
            var ambient = ConfigFile.Load(package.Paths.Config).Ambient;
            return ambient.Opacity == 0.8 && ambient.Blur == 0 && ambient.Size == 1.5 && ambient.OffsetX == -0.4 && ambient.EdgeFade == 0.3
                && ambient.TintStrength == 0.5 && ambient.Placement == "left" && ambient.Scaling == "fit" && ambient.FlipHorizontal;
        }, Soon, "every soft-background setting saved");
        await app.SaveScreenshotAsync("ui-soft-background-custom.png");

        app.Ui.InvokeNamed("Reset soft background");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Ambient == new AmbientSettings(), Soon, "soft background reset");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Settings_LaunchTimeChange_OffersRestartAndAppliesIt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.SelectComboItem("MaxFps", "30 fps");
        await app.WaitForStatusAsync(s => s["restartRequired"]!.GetValue<bool>(), Soon, "restart notice");
        await app.SaveScreenshotAsync("ui-restart-notice.png");

        app.Ui.InvokeNamed("Restart now");
        await app.WaitForPhaseAsync("waiting", Startup);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitForStatusAsync(s => !s["restartRequired"]!.GetValue<bool>(), Soon, "restart notice gone");
        var launches = package.ScrcpyLog().Where(l => l.StartsWith("args ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, launches.Length);
        Assert.Contains("--max-fps=30", launches[^1]);
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task TopBar_QuickActions_ReachThePhone()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        app.Ui.Invoke("QuickHome");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(l => l.EndsWith("input keyevent KEYCODE_HOME", StringComparison.Ordinal)), Soon, "home key");
        app.Ui.Invoke("QuickBack");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(l => l.EndsWith("input keyevent KEYCODE_BACK", StringComparison.Ordinal)), Soon, "back key");
        app.Ui.Invoke("QuickScreenshot");
        var shots = Path.Combine(package.Root, "captures", "screenshots");
        await app.WaitUntilAsync(() => Directory.Exists(shots) && Directory.GetFiles(shots, "*.png").Length == 1, Soon, "screenshot file");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task LockQuestion_AnswerIsRemembered()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForStatusAsync(s => s["lockQuestion"]!.GetValue<bool>(), Startup, "lock-type question");
        await app.SaveScreenshotAsync("ui-lock-question.png");
        app.Ui.InvokeNamed("Pattern");
        await app.WaitForStatusAsync(s => !s["lockQuestion"]!.GetValue<bool>(), Soon, "question answered");
        Assert.Equal(LockScreenModes.Pattern, new StateStore(package.Paths.State).GetDevice("FAKE123")!.LockScreenMode);
        Assert.True(app.Ui.Exists("PatternToggle"), "the Controls tab shows the pattern-guide section for pattern phones");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Controls_StopAndStartMirror()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Invoke("SessionButton");
        await app.WaitForPhaseAsync("stopped", Startup);
        await app.SaveScreenshotAsync("ui-stopped.png");
        app.Ui.Invoke("EmptyPrimary");
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task PhonePanel_ShowsTheCatalogueAndWritesEveryKindOfSetting()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabPhone");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(l => l.Contains("settings list system", StringComparison.Ordinal)), Soon, "phone settings read");
        await app.SaveScreenshotAsync("ui-phone-tab.png");

        // A toggle, a choice and a slider, each written the moment it changes.
        app.Ui.ExpandGroup("PhoneGroup Input & gestures");
        app.Ui.Toggle("show-touches", true);
        await app.WaitUntilAsync(() => package.AdbCalls().Any(l => l.Contains("settings put system show_touches 1", StringComparison.Ordinal)), Soon, "show taps written");

        app.Ui.SelectComboItem("screen-timeout", "5 minutes");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(l => l.Contains("settings put system screen_off_timeout 300000", StringComparison.Ordinal)), Soon, "timeout written");

        app.Ui.SetValue("brightness", 200);
        app.Ui.Invoke("Reset show-touches");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(l => l.Contains("settings delete system show_touches", StringComparison.Ordinal)), Soon, "reset to the phone default");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task PhonePanel_SearchNarrowsToOneGroup()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabPhone");
        await app.WaitUntilAsync(() => app.Ui.Exists("brightness"), Soon, "the catalogue rows");

        app.Ui.ExpandGroup("PhoneGroup Input & gestures");
        app.Ui.SetText("SettingsSearch", "brightness");
        await app.WaitUntilAsync(() => !app.Ui.Exists("show-touches") && app.Ui.Exists("brightness"), Soon, "the search to narrow the list");
        await app.SaveScreenshotAsync("ui-phone-search.png");

        app.Ui.SetText("SettingsSearch", "zzz-nothing");
        await app.WaitUntilAsync(() => !app.Ui.Exists("brightness"), Soon, "no matches");
        app.Ui.SetText("SettingsSearch", "");
        await app.WaitUntilAsync(() => app.Ui.Exists("brightness"), Soon, "the full list again");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Advanced_FiltersEveryAndroidKey()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabPhone");
        app.Ui.ExpandGroup("GroupRaw");
        app.Ui.SetText("Search", "font");
        await app.WaitUntilAsync(() => app.Ui.CountChildren("Rows") == 1, Soon, "one filtered row");
        await app.SaveScreenshotAsync("ui-advanced-filter.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task CloseButton_HidesToTray_AndShowReturns()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.CloseWindow();
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>() && s["mirroring"]!.GetValue<bool>(), Soon, "hidden to tray, still mirroring");
        Assert.True((await app.SendAsync(new IpcRequest("show"))).Ok);
        await app.WaitForStatusAsync(s => s["windowVisible"]!.GetValue<bool>(), Soon, "window shown again");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task FirstRun_SkipForNow_HidesTheGuide()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        using var app = new AppProcess(package);
        await app.WaitForStatusAsync(s => s["onboarding"]!.GetValue<bool>(), Startup, "first-run guide");
        app.Ui.Invoke("SkipButton");
        await app.WaitForStatusAsync(s => !s["onboarding"]!.GetValue<bool>(), Soon, "guide dismissed");
        Assert.True(new StateStore(package.Paths.State).Ui.SetupDismissed);
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task AltWheel_ZoomsAndShowsTheNavigator()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.AltWheelOverMirrorAsync(3);
        var status = await app.WaitForStatusAsync(s => s["zoom"]!.GetValue<double>() > 1.0, Soon, "zoom via Alt+wheel");
        Assert.True(status["navigatorVisible"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("ui-alt-wheel-zoom.png");

        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupControls");
        app.Ui.SelectComboItem("NavigatorCorner", "Top left");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Zoom.NavigatorCorner == "top-left", Soon, "navigator corner saved");
        await app.SaveScreenshotAsync("ui-navigator-top-left.png");

        await app.AltWheelOverMirrorAsync(-6);
        await app.WaitForStatusAsync(s => s["zoom"]!.GetValue<double>() == 1.0 && !s["navigatorVisible"]!.GetValue<bool>(), Soon, "zoomed back out");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Hud_ButtonsPositionAndHoverZoneFollowTheSettings()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupHud");
        app.Ui.SelectComboItem("HudPosition", "Bottom right");
        app.Ui.Toggle("hud-button screenshot", false);
        app.Ui.Toggle("hud-button mute", true);
        await app.WaitUntilAsync(() =>
        {
            var hud = ConfigFile.Load(package.Paths.Config).Hud;
            return hud.Position == "bottom-right" && !hud.Buttons.Contains("screenshot") && hud.Buttons.Contains("mute");
        }, Soon, "the HUD settings to save");

        await app.PressKeyAsync(0x7A); // F11
        await app.WaitForStatusAsync(s => s["fullscreen"]!.GetValue<bool>(), Soon, "fullscreen");

        // The bar now lives in the bottom-right corner, so that is where hovering brings it back.
        app.MovePointerToCenter();
        await app.WaitForStatusAsync(s => !s["hudVisible"]!.GetValue<bool>(), TimeSpan.FromSeconds(10), "the HUD to hide");
        app.MovePointerToTop();
        await Task.Delay(700, TestContext.Current.CancellationToken);
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["hudVisible"]!.GetValue<bool>());

        app.MovePointerToCorner();
        var shown = await app.WaitForStatusAsync(s => s["hudVisible"]!.GetValue<bool>(), Soon, "the HUD in its new corner");
        Assert.True(shown["hudVisible"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("ui-hud-bottom-right.png");
        Assert.False(app.Ui.Exists("hud-screenshot"));
        Assert.True(app.Ui.Exists("hud-mute"));

        await app.PressKeyAsync(0x1B);
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task Info_ListsToolsAndVersion()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabInfo");
        Assert.NotNull(app.Ui.FindByName("v9.9"));
        Assert.NotNull(app.Ui.FindByName(package.Root));
        await app.SaveScreenshotAsync("ui-info-tab.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task CliConfigSet_AppliesWhileTheAppRuns()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        var output = await app.RunCliAsync("--json", "config", "set", "Mirror.MaxFps", "30");
        Assert.True(JsonNode.Parse(output)!["ok"]!.GetValue<bool>(), output);
        await app.WaitForStatusAsync(s => s["restartRequired"]!.GetValue<bool>(), Soon, "the running app to notice the CLI edit");

        var status = await app.RunCliAsync("--json", "status");
        Assert.True(JsonNode.Parse(status)!["data"]!["appRunning"]!.GetValue<bool>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 75_000)]
    public async Task WindowPlacement_IsRemembered()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        double scale;
        using (var app = new AppProcess(package))
        {
            await app.WaitForPhaseAsync("mirroring", Startup);
            scale = app.DpiScale();
            app.MoveWindow((int)(120 * scale), (int)(90 * scale), (int)(1000 * scale), (int)(700 * scale));
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await app.QuitAsync();
        }

        // A physical-pixel move lands on whole pixels, so the saved DIPs can differ by up to one.
        var ui = new StateStore(package.Paths.State).Ui;
        Assert.Equal(120, ui.Left, 1.0);
        Assert.Equal(90, ui.Top, 1.0);
        Assert.Equal(1000, ui.Width, 1.0);
        Assert.Equal(700, ui.Height, 1.0);
    }
}
