using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Profiles across processes: applied by key and by the command line through the running app,
/// switching by themselves in fullscreen and on battery, a phone's own profile when it connects,
/// the restart a profile may ask for, and what the tray offers.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static JsonNode ProfilesOf(JsonObject status) => status["profiles"]!;

    private static async Task<JsonObject> CurrentStatusAsync(AppProcess app)
    {
        var response = await app.SendAsync(new IpcRequest("status"));
        Assert.True(response.Ok, response.Error);
        return response.Data!.AsObject();
    }

    private static IpcRequest ProfileIpc(string verb, string name = "") =>
        new("profile", new Dictionary<string, string> { ["verb"] = verb, ["name"] = name });

    /// <summary>A profile in the package's folder before the app starts, as someone's saved one.</summary>
    private static void SaveProfile(TestPackage package, string name, params (string Path, JsonNode Value)[] settings)
    {
        ProfileStore.For(package.Paths).Save(new Profile(
            name,
            settings.ToDictionary(s => s.Path, s => s.Value, StringComparer.Ordinal),
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task Profiles_FullscreenUsesItsOwnAndPutsTheRestBack()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Profiles.WhenFullscreen = "Quiet");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.False(ConfigFile.Load(package.Paths.Config).Sound.MuteWhenHidden);

        await app.FocusAsync();
        await app.PressKeyAsync(0x7A); // F11
        var on = await app.WaitForStatusAsync(
            s => ProfilesOf(s)["automatic"]?.GetValue<string>() == "Quiet",
            TimeSpan.FromSeconds(15),
            "the fullscreen profile to switch on");
        Assert.True(on["fullscreen"]!.GetValue<bool>());
        Assert.Equal("Quiet", ProfilesOf(on)["current"]!.GetValue<string>());
        Assert.True(ConfigFile.Load(package.Paths.Config).Sound.MuteWhenHidden);

        await app.PressKeyAsync(0x1B); // Esc leaves fullscreen; what the profile changed goes back.
        await app.WaitForStatusAsync(
            s => !s["fullscreen"]!.GetValue<bool>() && ProfilesOf(s)["automatic"] is null,
            TimeSpan.FromSeconds(15),
            "the fullscreen profile to end");
        Assert.False(ConfigFile.Load(package.Paths.Config).Sound.MuteWhenHidden);
        await app.QuitAsync();
    }

    [Fact]
    public async Task Profiles_BatteryUsesItsOwnAndKeepsWhatYouChangedMeanwhile()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Profiles.WhenOnBattery = "Battery saver");
        package.SetPower("mains");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        package.SetPower("battery");
        await app.WaitForStatusAsync(
            s => ProfilesOf(s)["automatic"]?.GetValue<string>() == "Battery saver" && ProfilesOf(s)["onBattery"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(15),
            "the battery profile to switch on");
        Assert.Equal(30, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);

        // A setting changed by hand while it is on belongs to the person and stays.
        package.EditConfig(c => c.Mirror.MaxFps = 45);
        package.SetPower("mains");
        await app.WaitForStatusAsync(
            s => ProfilesOf(s)["automatic"] is null,
            TimeSpan.FromSeconds(15),
            "the battery profile to end");
        var config = ConfigFile.Load(package.Paths.Config);
        Assert.Equal(45, config.Mirror.MaxFps);
        Assert.Equal(1920, config.Mirror.MaxSize);
        await app.QuitAsync();
    }

    [Fact]
    public async Task Profiles_EachPhoneGetsItsOwnWhenItConnects()
    {
        using var package = new TestPackage(withFakeTools: true);
        SaveProfile(package, "For this phone", ("Zoom.MaxZoom", JsonValue.Create(5.0)));
        var state = new StateStore(package.Paths.State);
        state.RememberDevice("FAKE123", "Galaxy S21 Ultra", "SM-G998B");
        state.Update(s => s.Devices["FAKE123"].Profile = "For this phone");

        using var app = new AppProcess(package);
        await app.WaitForStatusAsync(
            s => ProfilesOf(s)["automatic"]?.GetValue<string>() == "For this phone",
            StartupTimeout,
            "the phone's own profile to switch on");
        Assert.Equal(5.0, ConfigFile.Load(package.Paths.Config).Zoom.MaxZoom);
        await app.QuitAsync();
    }

    [Fact]
    public async Task Profiles_KeysApplyThemInTheListsOrder()
    {
        using var package = new TestPackage(withFakeTools: true);
        SaveProfile(package, "First", ("Zoom.WheelStep", JsonValue.Create(0.3)));
        SaveProfile(package, "Second", ("Zoom.WheelStep", JsonValue.Create(0.4)));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.FocusAsync();

        await app.PressCtrlAltKeyAsync(0x71); // F2: the second profile.
        await app.WaitForStatusAsync(
            s => ProfilesOf(s)["manual"]!.GetValue<string>() == "Second",
            TimeSpan.FromSeconds(15),
            "the second profile to be applied");
        Assert.Equal(0.4, ConfigFile.Load(package.Paths.Config).Zoom.WheelStep);

        // A number with no profile only says so; with the keys off, the key does nothing at all.
        await app.PressCtrlAltKeyAsync(0x72); // F3
        await app.WaitUntilAsync(
            () => app.Ui.Read("StatusText", e => e.Name).StartsWith("There is no profile 3.", StringComparison.Ordinal),
            TimeSpan.FromSeconds(15),
            "the missing profile to be said");
        package.EditConfig(c => c.Profiles.Keys = false);
        await app.WaitForStatusAsync(s => s["configReloads"]!.GetValue<int>() > 0, TimeSpan.FromSeconds(15), "the setting to arrive");
        await app.PressCtrlAltKeyAsync(0x70); // F1
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal("Second", ProfilesOf(await CurrentStatusAsync(app))["manual"]!.GetValue<string>());
        await app.QuitAsync();
    }

    [Fact]
    public async Task Profiles_RestartAtOnceWhenAskedAndTheMirrorUsesIt()
    {
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Profiles.AfterApplying = "restart");
        SaveProfile(package, "Small", ("Mirror.MaxSize", JsonValue.Create(1280)));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var firstPid = (await CurrentStatusAsync(app))["mirrorProcessId"]!.GetValue<int>();

        var applied = await app.SendAsync(ProfileIpc("apply", "Small"));
        Assert.True(applied.Ok, applied.Error);
        Assert.Contains("the mirror restarts to use it", applied.Data!["words"]!.GetValue<string>(), StringComparison.Ordinal);
        await app.WaitForStatusAsync(
            s => s["mirroring"]!.GetValue<bool>() && s["mirrorProcessId"]!.GetValue<int>() != firstPid && !s["restartRequired"]!.GetValue<bool>(),
            StartupTimeout,
            "the mirror to restart with the profile");
        var launch = package.ScrcpyLog().Last(l => l.StartsWith("args ", StringComparison.Ordinal));
        Assert.Contains("--max-size=1280", launch, StringComparison.Ordinal);
        await app.QuitAsync();
    }

    [Fact]
    public async Task Profiles_TheCommandLineWorksThroughTheRunningApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // Saved through the app: the window and the tray follow without a restart.
        package.EditConfig(c => c.Zoom.WheelStep = 0.25);
        await app.WaitForStatusAsync(s => s["configReloads"]!.GetValue<int>() > 0, TimeSpan.FromSeconds(15), "the change to arrive");
        // The real rex.exe, on this app's pipe: machine mode wraps the app's answer in a reply of its own.
        var saved = JsonNode.Parse(await app.RunCliAsync("--json", "profile", "save", "Mine"))!.AsObject();
        Assert.True(saved["ok"]!.GetValue<bool>(), saved.ToJsonString());
        Assert.Equal("Mine", saved["data"]!["name"]!.GetValue<string>());
        var status = await app.WaitForStatusAsync(
            s => ProfilesOf(s)["list"]!.AsArray().Any(n => n!.GetValue<string>() == "Mine"),
            TimeSpan.FromSeconds(15),
            "the app to list the saved profile");
        Assert.Contains("Mine", ProfilesOf(status)["tray"]!.AsArray().Select(n => n!.GetValue<string>().TrimStart('*', ' ')));

        var applied = JsonNode.Parse(await app.RunCliAsync("--json", "profile", "apply", "Mine"))!.AsObject();
        Assert.True(applied["ok"]!.GetValue<bool>(), applied.ToJsonString());
        Assert.Equal("Mine", applied["data"]!["name"]!.GetValue<string>());
        await app.WaitForStatusAsync(
            s => ProfilesOf(s)["manual"]!.GetValue<string>() == "Mine",
            TimeSpan.FromSeconds(15),
            "the app to show it applied");
        Assert.Contains("Mine applied", await app.RunCliAsync("profile", "apply", "Mine"), StringComparison.Ordinal);
        await app.QuitAsync();
    }
}
