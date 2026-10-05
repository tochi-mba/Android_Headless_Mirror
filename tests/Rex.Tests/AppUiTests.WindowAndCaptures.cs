using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The window your way in the Settings tab: the bars come and go, the side panel grows, where the
/// window opens, fitting it, asking before quitting, how captures are named, and the rest of the
/// new rows, each saved and each followed at once.
/// </summary>
public sealed partial class AppUiTests
{
    [Fact(Timeout = 150_000)]
    public async Task Settings_TheWindowYourWay()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupWindow");
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);
        static JsonObjectView Window(System.Text.Json.Nodes.JsonObject s) => new(s["window"]!.AsObject());

        app.Ui.Toggle("ShowTopBar", on: false);
        await app.WaitForStatusAsync(s => !Window(s).Bool("topBar"), Soon, "the top bar to go");
        Assert.False(app.Ui.Exists("DeviceChip"));
        // Without the top bar its phone buttons have nowhere to show, and say so by being off.
        Assert.False(app.Ui.Read("quick-button home", e => e.IsEnabled));

        app.Ui.Toggle("ShowStatusBar", on: false);
        await app.WaitForStatusAsync(s => !Window(s).Bool("statusBar"), Soon, "the status bar to go");
        Assert.False(app.Ui.Read("ShowHints", e => e.IsEnabled));

        app.Ui.SetValue("PanelScale", 1.25);
        await app.WaitForStatusAsync(s => Window(s).Number("panelScale") == 1.25, Soon, "the side panel to grow");
        await app.WaitUntilAsync(() => Saved().App.PanelScale == 1.25, Soon, "the panel's size to save");
        await app.SaveScreenshotAsync("ui-settings-window.png");

        app.Ui.Toggle("RememberPlacement", on: false);
        app.Ui.Toggle("FitWindowOnStart", on: true);
        app.Ui.Toggle("ConfirmQuit", on: true);
        app.Ui.Toggle("ShowTopBar", on: true);
        app.Ui.Toggle("ShowStatusBar", on: true);
        await app.WaitUntilAsync(() => Saved().App is { RememberPlacement: false, FitWindowOnStart: true, ConfirmQuit: true, ShowTopBar: true, ShowStatusBar: true },
            Soon, "the window's choices to save");
        await app.WaitForStatusAsync(s => Window(s).Bool("topBar") && Window(s).Bool("statusBar"), Soon, "both bars to come back");
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task Settings_CapturesZoomGuideAndLogRowsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupCaptures", "GroupZoom", "GroupDisplay", "GroupLockScreen", "GroupStartup", "GroupConnection");
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);

        // A name that cannot name files is refused in words, and nothing is saved.
        app.Ui.SetText("CaptureNames", "shot:{n}");
        LeaveTheSettingsTab(app);
        await app.WaitUntilAsync(() => app.Ui.Exists("CaptureNamesError"), Soon, "the name to be refused");
        Assert.Equal(CaptureName.Default, Saved().App.CaptureNames);
        app.Ui.SetText("CaptureNames", "{phone}-{n}");
        LeaveTheSettingsTab(app);
        await app.WaitUntilAsync(() => Saved().App.CaptureNames == "{phone}-{n}", Soon, "the name to save");
        Assert.EndsWith("-1.png", app.Ui.Read("CaptureNamesExample", e => e.Name), StringComparison.Ordinal);

        app.Ui.Toggle("ScreenshotFlash", on: true);
        app.Ui.Toggle("OpenScreenshots", on: true);
        app.Ui.Toggle("NotifyMirrorStops", on: true);
        app.Ui.Toggle("LoggingVerbose", on: true);
        app.Ui.SelectComboItem("AmbientWhenZoomed", "Hide it");
        app.Ui.SetValue("ZoomKeyStep", 0.5);
        app.Ui.SetValue("PatternDotSize", 1.3);
        await app.WaitUntilAsync(() => Saved() is
        {
            App: { ScreenshotFlash: true, OpenScreenshots: true, NotifyMirrorStops: true },
            Logging.Verbose: true,
            Ambient.WhenZoomed: "hide",
            Zoom.KeyStep: 0.5,
            PatternGuide.DotSize: 1.3,
        }, Soon, "every new row to save");

        // With the log written in full, the next phone command reaches it.
        await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "home" }));
        await app.WaitUntilAsync(() => app.ReadLog().Contains("Ran adb -s FAKE123 shell input (where the screen was touched is left out) · exit 0", StringComparison.Ordinal),
            Soon, "the command in the log");
        Assert.DoesNotContain(app.ReadLog().Split('\n'), line => line.Contains("Ran ", StringComparison.Ordinal) && line.Contains("KEYCODE_HOME", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task Settings_BackdropFloatingControlsAndUpdateRowsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await OpenSettingsAsync(package, "GroupWindow", "GroupHud", "GroupStartup");
        RexConfig Saved() => ConfigFile.Load(package.Paths.Config);

        app.Ui.SelectComboItem("Backdrop", "Black, for an OLED screen");
        await app.WaitForStatusAsync(s => s["window"]!["backdrop"]!.GetValue<string>() == "black", Soon, "the mirror area to turn black");
        // scrcpy's own edges follow at the next start, so a restart is offered.
        await app.WaitForStatusAsync(s => s["restartRequired"]!.GetValue<bool>(), Soon, "the restart offer");

        app.Ui.Toggle("HudAutoHide", on: false);
        await app.WaitUntilAsync(() => !app.Ui.Read("HudDelay", e => e.IsEnabled), Soon, "the delay to wait for hiding");
        app.Ui.Toggle("HudShowInWindow", on: true);
        app.Ui.Toggle("CheckForUpdates", on: true);
        await app.WaitUntilAsync(() => Saved() is { Mirror.Backdrop: "black", Hud: { AutoHide: false, ShowInWindow: true }, App.CheckForUpdates: true },
            Soon, "the new rows to save");
        await app.WaitForStatusAsync(s => s["hudVisible"]!.GetValue<bool>(), Soon, "the floating controls in the window");
        await app.SaveScreenshotAsync("ui-floating-controls-in-window.png");
        await app.QuitAsync();
    }

    /// <summary>A small reader over a status object's fields.</summary>
    private readonly record struct JsonObjectView(System.Text.Json.Nodes.JsonObject Node)
    {
        public bool Bool(string name) => Node[name]!.GetValue<bool>();

        public double Number(string name) => Node[name]!.GetValue<double>();
    }
}
