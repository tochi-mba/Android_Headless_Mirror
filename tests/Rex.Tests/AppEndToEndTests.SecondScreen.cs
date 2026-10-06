using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The second screen across processes: its own session with a display of its own, keys going to the
/// view that has them, instead of the phone, switching app on the same display, copies waiting,
/// closing and coming back with the mirror, an Android too old for it, and the command line.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static readonly TimeSpan ScreenTimeout = TimeSpan.FromSeconds(30);

    private static JsonNode ScreenOf(JsonObject status) => status["secondScreen"]!;

    private static IpcRequest Screen(string verb, string app = "", string placement = "", string size = "") =>
        new("screen", new Dictionary<string, string> { ["verb"] = verb, ["app"] = app, ["placement"] = placement, ["size"] = size });

    /// <summary>The app mirroring with its apps read, so a second screen can be opened by an app's name.</summary>
    private static async Task<AppProcess> StartForScreenAsync(TestPackage package)
    {
        var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitForAppsAsync(app);
        return app;
    }

    private static async Task<JsonObject> OpenScreenAsync(AppProcess app, string name = "Example One", string placement = "", string size = "")
    {
        var reply = await app.SendAsync(Screen("open", name, placement, size));
        Assert.True(reply.Ok, reply.Error);
        return await app.WaitForStatusAsync(s => ScreenOf(s)["state"]!.GetValue<string>() == "showing", ScreenTimeout, "the second screen");
    }

    private static string[] ScreenLines(TestPackage package) =>
        package.ScrcpyLog().Where(l => l.StartsWith("new-display", StringComparison.Ordinal)).ToArray();

    [Fact(Timeout = 180_000)]
    public async Task SecondScreen_OpensBesideThePhoneWithItsOwnSession()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartForScreenAsync(package);

        var status = await OpenScreenAsync(app);
        var screen = ScreenOf(status);
        Assert.Equal("com.example.one", screen["app"]!.GetValue<string>());
        Assert.Equal(7, screen["displayId"]!.GetValue<int>());
        Assert.True(screen["follows"]!.GetValue<bool>());
        Assert.Equal("Example One on a second screen beside the phone", screen["words"]!.GetValue<string>());
        Assert.Contains("Second screen · Example One", screen["captions"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.True(status["mirroring"]!.GetValue<bool>());

        // The names say themselves, then step aside from the top of each picture.
        await app.WaitForStatusAsync(s => !ScreenOf(s)["captionsShowing"]!.GetValue<bool>(), ScreenTimeout, "the views' names to step aside");
        Assert.Contains("Second screen · Example One", ScreenOf((await app.SendAsync(new IpcRequest("status"))).Data!.AsObject())["captions"]!.AsArray().Select(c => c!.GetValue<string>()));

        var line = Assert.Single(ScreenLines(package));
        Assert.Contains("flex=True start-app=com.example.one", line, StringComparison.Ordinal);
        var args = package.ScrcpyLog().Last(l => l.StartsWith("args ", StringComparison.Ordinal) && l.Contains("--new-display", StringComparison.Ordinal));
        foreach (var expected in new[] { "--port=27190", "--no-cleanup", "--no-audio", "--no-power-on", "--display-ime-policy=local", "--no-vd-destroy-content", "--flex-display" })
        {
            Assert.Contains(expected, args, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--turn-screen-off", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--window-width", args, StringComparison.Ordinal);
        await app.SaveScreenshotAsync("second-screen-beside.png");

        // Closing moves the app back to the phone, as set, and says so.
        Assert.True((await app.SendAsync(Screen("close"))).Ok);
        var closed = await app.WaitForStatusAsync(s => ScreenOf(s)["state"]!.GetValue<string>() == "off", ScreenTimeout, "the second screen to close");
        Assert.Equal(0, ScreenOf(closed)["processId"]!.GetValue<int>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task SecondScreen_KeysGoToTheViewThatHasThem()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartForScreenAsync(package);
        await OpenScreenAsync(app);

        // Showing comes a moment before the view has its place in the window.
        var status = await app.WaitForStatusAsync(s => ScreenOf(s)["rect"] is not null, ScreenTimeout, "the second screen's place in the window");
        var rect = ScreenOf(status)["rect"]!;
        await app.ClickAtAsync(rect["left"]!.GetValue<int>() + rect["width"]!.GetValue<int>() / 2, rect["top"]!.GetValue<int>() + rect["height"]!.GetValue<int>() / 2);
        await app.WaitForStatusAsync(s => ScreenOf(s)["active"]!.GetValue<bool>(), ScreenTimeout, "the second screen to have the keyboard");
        await app.WaitForStatusAsync(s => ScreenOf(s)["outlined"]!.GetValue<bool>(), ScreenTimeout, "the outline round the view with the keyboard");

        // Home on the second screen is its own session's shortcut, played on its display; not the phone's key.
        await app.PressCtrlAltKeyAsync((byte)'H');
        await app.WaitUntilAsync(() => package.ScrcpyLog().Any(l => l.StartsWith("key vk=72 ", StringComparison.Ordinal) && l.EndsWith("at=27190", StringComparison.Ordinal)),
            ScreenTimeout, "Home sent to the second screen");
        Assert.DoesNotContain(package.AdbCalls(), c => c.Contains("KEYCODE_HOME", StringComparison.Ordinal));

        // Turning the phone means nothing for a display of its own.
        Assert.False((await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "rotate-device" }))).Ok);

        // The phone's view takes the keyboard back with a click, and Home is the phone's again.
        var phone = status["copies"]!["views"]![0]!;
        await app.ClickAtAsync(phone["x"]!.GetValue<int>() + 40, phone["y"]!.GetValue<int>() + phone["height"]!.GetValue<int>() / 2);
        await app.WaitForStatusAsync(s => !ScreenOf(s)["active"]!.GetValue<bool>(), ScreenTimeout, "the phone to have the keyboard");
        await app.PressCtrlAltKeyAsync((byte)'H');
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("KEYCODE_HOME", StringComparison.Ordinal)), ScreenTimeout, "Home on the phone");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task SecondScreen_InsteadOfThePhoneWithAFixedSize()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartForScreenAsync(package);
        var mirror = (await app.SendAsync(new IpcRequest("status"))).Data!["mirrorProcessId"]!.GetValue<int>();

        var status = await OpenScreenAsync(app, placement: "instead", size: "1080p");
        Assert.Equal("Example One on a second screen instead of the phone", ScreenOf(status)["words"]!.GetValue<string>());
        Assert.False(ScreenOf(status)["follows"]!.GetValue<bool>());
        Assert.Contains("new-display 1920x1080 flex=False", Assert.Single(ScreenLines(package)), StringComparison.Ordinal);
        // The phone's own session keeps running behind it, with its sound.
        Assert.Equal(mirror, status["mirrorProcessId"]!.GetValue<int>());
        Assert.Equal("instead", ConfigFile.Load(package.Paths.Config).SecondScreen.Placement);
        await app.SaveScreenshotAsync("second-screen-instead.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task SecondScreen_SwitchingAppKeepsTheDisplay()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartForScreenAsync(package);
        await OpenScreenAsync(app);

        Assert.True((await app.SendAsync(Screen("app", "Example Two"))).Ok);
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am start --display 7 -n com.example.two/.MainActivity", StringComparison.Ordinal)), ScreenTimeout, "the app on display 7");
        var status = await app.WaitForStatusAsync(s => ScreenOf(s)["app"]!.GetValue<string>() == "com.example.two", ScreenTimeout, "the new app");
        Assert.Single(ScreenLines(package));
        Assert.Equal("com.example.two", new StateStore(package.Paths.State).GetDevice("FAKE123")!.SecondScreenApp);
        Assert.Equal("showing", ScreenOf(status)["state"]!.GetValue<string>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 240_000)]
    public async Task SecondScreen_CopiesWaitAndTheScreenComesBackWithTheMirror()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondScreen.ReopenOnStart = true);
        using var app = await StartForScreenAsync(package);
        await app.ActionAsync("copy-add");
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "a copy");

        await OpenScreenAsync(app);
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 0, ScreenTimeout, "the copy to wait");

        // The mirror starting again brings the second screen back with its app.
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitUntilAsync(() => ScreenLines(package).Length == 2, ScreenTimeout, "the second screen opened again");
        await app.WaitForStatusAsync(s => ScreenOf(s)["state"]!.GetValue<string>() == "showing", ScreenTimeout, "the second screen back");

        Assert.True((await app.SendAsync(Screen("close"))).Ok);
        await app.WaitForStatusAsync(s => CopiesRunning(s) == 1, StartupTimeout, "the copy back");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task SecondScreen_NotOfferedBeforeAndroid10()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new
        {
            Devices = new[] { new { Serial = "FAKE123", State = "device", Product = "fake", Model = "Old Phone" } },
            Properties = new Dictionary<string, string> { ["ro.product.model"] = "Old Phone", ["ro.build.version.release"] = "9", ["ro.build.version.sdk"] = "28" },
        });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // Until the phone is mirrored the reason is that; then it is the phone's age.
        var status = await app.WaitForStatusAsync(s => ScreenOf(s)["reason"]?.GetValue<string>().Contains("Android", StringComparison.Ordinal) == true, StartupTimeout, "the reason");
        Assert.Equal("A second screen needs Android 10 or later; this phone has Android 9.", ScreenOf(status)["reason"]!.GetValue<string>());
        var refused = await app.SendAsync(Screen("open", "com.example.one"));
        Assert.False(refused.Ok);
        Assert.Contains("Android 10", refused.Error, StringComparison.Ordinal);
        Assert.Empty(ScreenLines(package));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task SecondScreen_TheCommandLineOpensAndClosesIt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = await StartForScreenAsync(package);

        Assert.Contains("Example One on a second screen", await app.RunCliAsync("screen", "open", "Example", "One", "--size", "720p"), StringComparison.Ordinal);
        Assert.Contains("new-display 1280x720 flex=False", Assert.Single(ScreenLines(package)), StringComparison.Ordinal);
        Assert.Contains("on a second screen", await app.RunCliAsync("screen"), StringComparison.Ordinal);
        Assert.Contains("Off", await app.RunCliAsync("screen", "close"), StringComparison.Ordinal);
        Assert.Contains("A size is", await app.RunCliAsync("--json", "screen", "open", "Example", "One", "--size", "huge"), StringComparison.Ordinal);
        await app.QuitAsync();
    }
}
