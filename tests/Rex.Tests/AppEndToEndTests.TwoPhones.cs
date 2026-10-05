using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Two different phones across processes: asking about the second one and showing it in a session
/// of its own, the keys and actions following the phone in use, it leaving and coming back, one
/// phone on USB and Wi-Fi shown once, swapping which is the main phone, never for one phone, and
/// the command line.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static readonly TimeSpan PhonesTimeout = TimeSpan.FromSeconds(30);

    private static JsonNode PhonesOf(JsonObject status) => status["phones"]!;

    private static string? BesideState(JsonObject status) => PhonesOf(status)["beside"]?["state"]?.GetValue<string>();

    private static IpcRequest PhonesIpc(string verb, string serial = "") =>
        new("phones", new Dictionary<string, string> { ["verb"] = verb, ["serial"] = serial });

    private static IpcRequest Action(string name) => new("action", new Dictionary<string, string> { ["name"] = name });

    /// <summary>
    /// The Galaxy on USB and a Pixel 7 of another shape; both already have a lock-screen answer, so
    /// the notice bar is free for the question about the second one.
    /// </summary>
    private static void WriteTwoPhones(TestPackage package, params object[] more)
    {
        var devices = new List<object>
        {
            new { Serial = "FAKE123", State = "device", Model = "SM-G998B" },
            new
            {
                Serial = "FAKE456",
                State = "device",
                Model = "Pixel_7",
                Properties = new Dictionary<string, string>
                {
                    ["ro.product.manufacturer"] = "Google",
                    ["ro.product.model"] = "Pixel 7",
                    ["ro.product.marketname"] = "Pixel 7",
                    ["ro.build.version.release"] = "14",
                    ["ro.build.version.sdk"] = "34",
                },
                DisplayWidth = 1440,
                DisplayHeight = 3120,
            },
        };
        devices.AddRange(more);
        package.WriteScenario(new
        {
            Devices = devices,
            Properties = new Dictionary<string, string>
            {
                ["ro.product.manufacturer"] = "Samsung",
                ["ro.product.model"] = "SM-G998B",
                ["ro.product.marketname"] = "Galaxy S21 Ultra",
                ["ro.build.version.release"] = "15",
                ["ro.build.version.sdk"] = "35",
            },
        });
        var state = new StateStore(package.Paths.State);
        foreach (var serial in new[] { "FAKE123", "FAKE456", "192.168.1.5:5555" })
        {
            state.SetLockScreenMode(serial, LockScreenModes.None);
        }
    }

    private static Task<JsonObject> WaitBesideAsync(AppProcess app, string state, string? why = null) =>
        app.WaitForStatusAsync(s => BesideState(s) == state, PhonesTimeout, why ?? $"the phone beside to be {state}");

    [Fact]
    public async Task TwoPhones_TheSecondIsAskedAboutThenShownInASessionOfItsOwn()
    {
        using var package = new TestPackage(withFakeTools: true);
        WriteTwoPhones(package);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        await app.WaitUntilAsync(() => app.Ui.Exists("NoticeBeside"), PhonesTimeout, "the question about the second phone");
        Assert.Equal("Pixel 7 is connected too.", app.Ui.Read("NoticeTitle", e => e.Name));
        await app.SaveScreenshotAsync("two-phones-question.png");
        app.Ui.Invoke("NoticeBeside");

        var shown = await WaitBesideAsync(app, "showing");
        Assert.Equal("FAKE456", PhonesOf(shown)["beside"]!["serial"]!.GetValue<string>());
        Assert.Equal("Galaxy S21 Ultra and Pixel 7 side by side", PhonesOf(shown)["words"]!.GetValue<string>());
        var launch = package.ScrcpyLog().Last(l => l.StartsWith("args ", StringComparison.Ordinal) && l.Contains("--serial=FAKE456", StringComparison.Ordinal));
        Assert.Contains("--port=27200", launch, StringComparison.Ordinal);
        Assert.Contains("--no-audio", launch, StringComparison.Ordinal);
        // The main session stays on its own phone.
        Assert.Equal("FAKE123", shown["device"]!["serial"]!.GetValue<string>());
        Assert.Equal("FAKE456", new StateStore(package.Paths.State).Ui.SecondPhone);
        // Two views in their own shapes; copies have no room meanwhile.
        await app.WaitUntilAsync(() => app.Ui.Read("DeviceMeta", e => e.Name).Contains("2 phones", StringComparison.Ordinal), PhonesTimeout, "the chip to say two phones");
        await app.SaveScreenshotAsync("two-phones-beside.png");
        await app.QuitAsync();
    }

    [Fact]
    public async Task TwoPhones_KeysAndActionsGoToThePhoneInUse()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondPhone.WhenConnected = "always");
        WriteTwoPhones(package);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitBesideAsync(app, "showing");

        Assert.True((await app.SendAsync(Action("home"))).Ok);
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c == "-s FAKE123 shell input keyevent KEYCODE_HOME"), PhonesTimeout, "Home on the main phone");

        var switched = await app.SendAsync(PhonesIpc("switch"));
        Assert.True(switched.Ok, switched.Error);
        await app.WaitForStatusAsync(s => PhonesOf(s)["activeSerial"]!.GetValue<string>() == "FAKE456", PhonesTimeout, "the phone beside to be in use");
        Assert.Equal("Pixel 7", app.Ui.Read("PhoneStripName", e => e.Name));

        var home = await app.SendAsync(Action("home"));
        Assert.True(home.Ok, home.Error);
        Assert.Equal("Home on Pixel 7", home.Data!["text"]!.GetValue<string>());
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c == "-s FAKE456 shell input keyevent KEYCODE_HOME"), PhonesTimeout, "Home on the phone beside");

        // Copies belong to the main phone, and say so.
        var copy = await app.SendAsync(Action("copy-add"));
        Assert.False(copy.Ok);
        Assert.Equal(ActionRouting.CopiesAreTheMainPhones, copy.Error);

        // The phone's settings tab reads the phone in use.
        app.Ui.Select("TabPhone");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.StartsWith("-s FAKE456 shell settings list", StringComparison.Ordinal)), PhonesTimeout, "the phone tab to read the phone beside");
        await app.QuitAsync();
    }

    [Fact]
    public async Task TwoPhones_TheOtherLeavesAndComesBackByItself()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondPhone.WhenConnected = "always");
        WriteTwoPhones(package);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitBesideAsync(app, "showing");

        package.WriteScenario(new { Devices = new[] { new { Serial = "FAKE123", State = "device", Model = "SM-G998B" } } });
        await app.WaitForStatusAsync(s => PhonesOf(s)["beside"] is null, PhonesTimeout, "the phone beside to go");
        await app.WaitUntilAsync(() => app.ReadLog().Contains("FAKE456 is no longer connected", StringComparison.Ordinal), PhonesTimeout, "its leaving to be noted");

        // Remembered, it comes back by itself; asking is not needed again.
        WriteTwoPhones(package);
        await WaitBesideAsync(app, "showing", "the phone beside to come back");
        await app.QuitAsync();
    }

    [Fact]
    public async Task TwoPhones_OnePhoneOnUsbAndWifiIsShownOnce()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondPhone.WhenConnected = "always");
        package.WriteScenario(new
        {
            Devices = new object[]
            {
                new { Serial = "FAKE123", State = "device", Model = "SM-G998B" },
                new { Serial = "192.168.1.5:5555", State = "device", Model = "SM-G998B", Properties = new Dictionary<string, string> { ["ro.serialno"] = "FAKE123" } },
            },
        });
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.None);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // Its serial over Wi-Fi is read once; it is the same phone, so nothing is shown beside it.
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c == "-s 192.168.1.5:5555 shell getprop ro.serialno"), PhonesTimeout, "the Wi-Fi phone's serial to be read");
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        var status = await app.WaitForStatusAsync(s => s["mirroring"]!.GetValue<bool>(), PhonesTimeout, "the mirror");
        Assert.Null(PhonesOf(status)["beside"]);
        Assert.DoesNotContain(package.ScrcpyLog(), l => l.StartsWith("args ", StringComparison.Ordinal) && l.Contains("--port=27200", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact]
    public async Task TwoPhones_TheOtherCanBecomeTheMainPhone()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondPhone.WhenConnected = "always");
        WriteTwoPhones(package);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitBesideAsync(app, "showing");

        var swapped = await app.SendAsync(PhonesIpc("make-main"));
        Assert.True(swapped.Ok, swapped.Error);
        var status = await app.WaitForStatusAsync(
            s => s["device"]?["serial"]?.GetValue<string>() == "FAKE456" && s["mirroring"]!.GetValue<bool>() &&
                 PhonesOf(s)["beside"]?["serial"]?.GetValue<string>() == "FAKE123" && BesideState(s) == "showing",
            StartupTimeout,
            "the two phones to swap places");
        Assert.Equal("FAKE456", ConfigFile.Load(package.Paths.Config).Session.PreferredSerial);
        Assert.Equal("Pixel 7 and Galaxy S21 Ultra side by side", PhonesOf(status)["words"]!.GetValue<string>());
        await app.QuitAsync();
    }

    [Fact]
    public async Task TwoPhones_NeverForThisPhoneIsRemembered()
    {
        using var package = new TestPackage(withFakeTools: true);
        WriteTwoPhones(package);
        using (var first = new AppProcess(package))
        {
            await first.WaitForPhaseAsync("mirroring", StartupTimeout);
            await first.WaitUntilAsync(() => first.Ui.Exists("NoticeBesideNever"), PhonesTimeout, "the question about the second phone");
            first.Ui.Invoke("NoticeBesideNever");
            await first.WaitUntilAsync(() => new StateStore(package.Paths.State).GetDevice("FAKE456")?.ShowBeside == ShowBesideAnswers.Never, PhonesTimeout, "never to be remembered");
            await first.QuitAsync();
        }

        using var second = new AppProcess(package);
        await second.WaitForPhaseAsync("mirroring", StartupTimeout);
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        Assert.False(second.Ui.Exists("NoticeBeside"));

        // The phone menu can still show it.
        Assert.True((await second.SendAsync(PhonesIpc("beside", "FAKE456"))).Ok);
        await WaitBesideAsync(second, "showing");
        await second.QuitAsync();
    }

    [Fact]
    public async Task TwoPhones_TheCommandLineSaysWhichPhoneIsWhere()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.SecondPhone.WhenConnected = "always");
        WriteTwoPhones(package);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await WaitBesideAsync(app, "showing");

        var human = await app.RunCliAsync("phones");
        Assert.Contains("Galaxy S21 Ultra and Pixel 7 side by side", human, StringComparison.Ordinal);
        Assert.Contains("main phone · in use", human, StringComparison.Ordinal);
        Assert.Contains("beside", human, StringComparison.Ordinal);

        var stopped = JsonNode.Parse(await app.RunCliAsync("--json", "phones", "stop"))!.AsObject();
        Assert.True(stopped["ok"]!.GetValue<bool>(), stopped.ToJsonString());
        Assert.Null(stopped["data"]!["beside"]);
        await app.QuitAsync();
    }
}
