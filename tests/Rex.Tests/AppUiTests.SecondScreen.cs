using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The second screen through the window: choosing its app in the Apps tab, the Controls section, the splitter, and its settings.</summary>
public sealed partial class AppUiTests
{
    private static JsonNode ScreenOf(JsonObject status) => status["secondScreen"]!;

    [Fact(Timeout = 150_000)]
    public async Task SecondScreen_ChoosingAnAppFromTheAppsTab()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        app.Ui.Select("TabControls");
        Assert.Equal("Off", app.Ui.Read("ScreenStatus", i => i.Name));

        // Choosing can be cancelled, and nothing opens.
        app.Ui.Invoke("ScreenOpen");
        await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == "apps", Soon, "the Apps tab");
        // A border is not in UI Automation's tree; its Cancel button is, while the banner shows.
        await app.WaitUntilAsync(() => app.Ui.Exists("ChoosingCancel"), Soon, "the banner");
        await app.SaveScreenshotAsync("ui-second-screen-choosing.png");
        app.Ui.Invoke("ChoosingCancel");
        await app.WaitUntilAsync(() => !app.Ui.Exists("ChoosingCancel"), Soon, "the banner to go");

        // A click on an app while choosing opens it there.
        app.Ui.Select("TabControls");
        app.Ui.Invoke("ScreenOpen");
        await app.WaitUntilAsync(() => app.Ui.Exists("app-yours-com.example.two"), Soon, "the list");
        app.Ui.Invoke("app-yours-com.example.two");
        await app.WaitForStatusAsync(s => ScreenOf(s)["state"]!.GetValue<string>() == "showing", Startup, "the second screen");
        Assert.DoesNotContain(package.AdbCalls(), c => c.Contains("am start -n com.example.two/.MainActivity", StringComparison.Ordinal));

        app.Ui.Select("TabControls");
        await app.WaitUntilAsync(() => app.Ui.Read("ScreenStatus", i => i.Name) == "Example Two on a second screen beside the phone", Soon, "the section to say so");
        await app.SaveScreenshotAsync("ui-second-screen.png");

        // Instead of the phone, from the section, and closed again.
        app.Ui.Select("ScreenInstead");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).SecondScreen.Placement == "instead", Soon, "instead of the phone");
        app.Ui.Invoke("ScreenClose");
        await app.WaitForStatusAsync(s => ScreenOf(s)["state"]!.GetValue<string>() == "off", Soon, "the second screen to close");
        await app.QuitAsync();
    }

    [Fact(Timeout = 150_000)]
    public async Task SecondScreen_TheSplitterMovesWithTheKeysAndIsRemembered()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartWithAppsAsync(package);
        Assert.True((await app.SendAsync(new IpcRequest("screen", new Dictionary<string, string> { ["verb"] = "open", ["app"] = "Example One" }))).Ok);
        await app.WaitForStatusAsync(s => ScreenOf(s)["state"]!.GetValue<string>() == "showing", Startup, "the second screen");
        await app.WaitUntilAsync(() => app.Ui.Exists("ViewSplitter") && !app.Ui.Read("ViewSplitter", i => i.IsOffscreen), Soon, "the splitter");

        await app.FocusAsync();
        app.Ui.Focus("ViewSplitter");
        await app.PressChordAsync(0x27);
        await app.PressChordAsync(0x27);
        await app.WaitUntilAsync(() => new StateStore(package.Paths.State).Ui.ViewSplit > 0, Soon, "the split remembered");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Settings_SecondScreenRowsSave()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupScreen");

        // Your own size waits for "Your own", and upright for a fixed size.
        Assert.False(app.Ui.Read("ScreenCustomWidth", i => i.IsEnabled));
        Assert.False(app.Ui.Read("ScreenPortrait", i => i.IsEnabled));
        app.Ui.SelectComboItem("ScreenSize", "Your own");
        await app.WaitUntilAsync(() => app.Ui.Read("ScreenCustomWidth", i => i.IsEnabled), Soon, "your own size to open");
        app.Ui.SetText("ScreenCustomWidth", "1600");
        app.Ui.SetText("ScreenCustomHeight", "900");
        app.Ui.Focus("ScreenDecorations");

        app.Ui.SelectComboItem("ScreenPlacement", "Instead of the phone");
        app.Ui.SelectComboItem("ScreenKeyboard", "Never: type on this PC");
        app.Ui.SelectComboItem("ScreenMaxSize", "1280 px");
        app.Ui.SelectComboItem("ViewsArrangement", "One above the other");
        app.Ui.SelectComboItem("ViewsOutline", "Always");
        app.Ui.SelectComboItem("ViewsCaptions", "Never");
        app.Ui.Toggle("ScreenDecorations", false);
        app.Ui.Toggle("ScreenKeepApps", false);
        app.Ui.Toggle("ScreenReopen", true);
        app.Ui.SetValue("ScreenDpi", 240);
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config) is
        {
            SecondScreen: { Size: "custom", CustomWidth: 1600, CustomHeight: 900, Placement: "instead", Keyboard: "never", MaxSize: 1280, Decorations: false, KeepAppsOnClose: false, ReopenOnStart: true, Dpi: 240 },
            Views: { Arrangement: "stack", Outline: "always", Captions: "never" },
        }, Soon, "every row saved");

        // Beside-only rows wait while it is instead of the phone.
        Assert.False(app.Ui.Read("ScreenMinWidth", i => i.IsEnabled));
        Assert.False(app.Ui.Read("ViewsSplitter", i => i.IsEnabled));

        app.Ui.Select("TabApps");
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupApps");
        app.Ui.SelectComboItem("AppsOpenOn", "Ask each time");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Apps.OpenOn == "ask", Soon, "where apps open");
        await app.QuitAsync();
    }
}
