using Rex.AndroidMirror.Cli;
using Spectre.Console.Testing;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class RexAppTests
{
    [Fact]
    public void MainMenu_ExposesDisplayTransportsAndKeepsExitLast()
    {
        Assert.Contains("Display transports", RexApp.MainMenuChoices);
        Assert.Equal("Exit", RexApp.MainMenuChoices[^1]);
        Assert.Equal(
            RexApp.MainMenuChoices.Count,
            RexApp.MainMenuChoices.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task FirstRun_CanBeDeclinedCleanly()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient
        {
            DefaultStatus = Status(setup: false)
        };
        var console = ConsoleWithInput();
        console.Input.PushTextWithEnter("n");

        var app = new RexApp(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: false);

        var code = await app.RunAsync();

        Assert.Equal(1, code);
        Assert.Contains("SETUP CHECK", console.Output);
        Assert.Contains("Run guided setup now", console.Output);
    }

    [Fact]
    public async Task FirstRunWizard_ConfiguresToolsStartupLockAndHeadlessDefaults()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient();
        var device = new RexDevice("USB123", "device", false, "Samsung", "S24", "Samsung S24");
        bridge.Devices.Add(device);
        bridge.EnqueueStatus(Status(setup: false));
        bridge.EnqueueStatus(Status(setup: true, devices: new[] { device }));

        var console = ConsoleWithInput();
        console.Input.PushTextWithEnter("y"); // run setup
        console.Input.PushTextWithEnter("y"); // autostart
        console.Input.PushKey(ConsoleKey.Enter); // Pattern
        console.Input.PushTextWithEnter("y"); // physical display off
        console.Input.PushTextWithEnter("y"); // stay awake
        console.Input.PushTextWithEnter("y"); // Precision Touchpad
        console.Input.PushTextWithEnter("n"); // auto-open Control Center
        console.Input.PushTextWithEnter("n"); // start now
        PushDown(console, RexApp.MainMenuChoices.Count - 1); // main menu -> Exit
        console.Input.PushKey(ConsoleKey.Enter);

        var app = new RexApp(
            package.Paths,
            runner,
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: true);

        var code = await app.RunAsync();

        Assert.Equal(0, code);
        Assert.Contains(runner.Calls, x =>
            x.Kind == "powershell" &&
            x.FileName == package.Paths.Setup &&
            x.Arguments.Contains("-SkipAutostart"));
        Assert.Contains(runner.Calls, x =>
            x.Kind == "powershell" &&
            x.FileName == package.Paths.InstallAutostart);

        var lockCall = Assert.Single(bridge.Calls, x => x.Action == "set-lock-mode");
        Assert.Equal("USB123", lockCall.Serial);
        Assert.Equal("pattern", lockCall.Value);

        Assert.Equal("true", package.Config.Get("TurnPhysicalScreenOff").Value);
        Assert.Equal("true", package.Config.Get("StayAwakeWhenUsb").Value);
        Assert.Equal("true", package.Config.Get("MirrorChrome.NativeTouchpadGestures").Value);
        Assert.Equal("false", package.Config.Get("ControlCenter.OpenOnLaunch").Value);
    }

    [Fact]
    public async Task FirstRunWizard_SkipsTouchpadQuestionWhenCapabilityIsUnavailable()
    {
        using var package = new TempPackage();
        var runner = new FakeProcessRunner();
        var bridge = new FakeBridgeClient();
        bridge.EnqueueStatus(Status(setup: false));
        bridge.EnqueueStatus(Status(setup: true));

        var console = ConsoleWithInput();
        console.Input.PushTextWithEnter("y"); // setup
        console.Input.PushTextWithEnter("n"); // autostart
        console.Input.PushTextWithEnter("n"); // no device -> do not wait (WaitForDevices sees none then asks)
        console.Input.PushTextWithEnter("y"); // physical display off
        console.Input.PushTextWithEnter("y"); // stay awake
        console.Input.PushTextWithEnter("n"); // auto-open controls
        console.Input.PushTextWithEnter("n"); // start now
        PushDown(console, RexApp.MainMenuChoices.Count - 1); // main menu -> Exit
        console.Input.PushKey(ConsoleKey.Enter);

        var app = new RexApp(
            package.Paths,
            runner,
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: false);

        var code = await app.RunAsync();

        Assert.Equal(0, code);
        Assert.DoesNotContain("Precision Touchpad gestures", console.Output);
    }

    [Fact]
    public async Task RuntimeControls_DispatchSleepAndReturnToMainMenu()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        var device = new RexDevice("USB123", "device", false, "Google", "Pixel", "Google Pixel");
        bridge.Devices.Add(device);
        bridge.DefaultStatus = Status(setup: true, devices: new[] { device });

        var console = ConsoleWithInput();
        console.Input.PushKey(ConsoleKey.DownArrow); // Runtime controls
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter); // Sleep phone (first)
        PushDown(console, 32); // Back in runtime controls
        console.Input.PushKey(ConsoleKey.Enter);
        PushDown(console, RexApp.MainMenuChoices.Count - 1); // main menu -> Exit
        console.Input.PushKey(ConsoleKey.Enter);

        var app = new RexApp(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: false);

        var code = await app.RunAsync();

        Assert.Equal(0, code);
        var call = Assert.Single(bridge.Calls, x => x.Action == "scrcpy-action");
        Assert.Equal("sleep", call.Name);
        Assert.Equal("USB123", call.Serial);
    }

    [Fact]
    public async Task DeviceAnimationMenu_UpdatesAllAnimationScales()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        var device = new RexDevice("USB123", "device", false, "Samsung", "S24", "Samsung S24");
        bridge.Devices.Add(device);
        bridge.DefaultStatus = Status(setup: true, devices: new[] { device });

        var console = ConsoleWithInput();
        PushDown(console, 3); // Device settings (Display is near the end of the main menu)
        console.Input.PushKey(ConsoleKey.Enter);
        PushDown(console, 8); // Animation speed
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow); // Fast 0.5x
        console.Input.PushKey(ConsoleKey.Enter);
        PushDown(console, 17); // Back device menu
        console.Input.PushKey(ConsoleKey.Enter);
        PushDown(console, RexApp.MainMenuChoices.Count - 1); // main menu -> Exit
        console.Input.PushKey(ConsoleKey.Enter);

        var app = new RexApp(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: false);

        Assert.Equal(0, await app.RunAsync());

        var animationCalls = bridge.Calls.Where(x => x.Action == "friendly-set").ToArray();
        Assert.Equal(3, animationCalls.Length);
        Assert.Equal(
            new[] { "animation-window", "animation-transition", "animation-duration" },
            animationCalls.Select(x => x.Name).ToArray());
        Assert.All(animationCalls, x => Assert.Equal("0.5", x.Value));
    }

    [Fact]
    public async Task AdvancedAndroid_ProtectedKeyNeverDispatchesAWrite()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        var device = new RexDevice("USB123", "device", false, "Samsung", "S24", "Samsung S24");
        bridge.Devices.Add(device);
        bridge.AndroidSettings.Add(("adb_enabled", "1", "protected"));

        var console = ConsoleWithInput();
        console.Input.PushKey(ConsoleKey.Enter); // system
        console.Input.PushTextWithEnter(""); // no filter
        console.Input.PushKey(ConsoleKey.Enter); // adb_enabled
        console.Input.PushKey(ConsoleKey.Enter); // Change value -> protected
        PushDown(console, 3); // namespace chooser -> Back
        console.Input.PushKey(ConsoleKey.Enter);

        var app = new RexApp(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: false);

        await app.AdvancedAndroidAsync();

        Assert.DoesNotContain(bridge.Calls, x =>
            x.Action is "settings-set" or "settings-delete");
        Assert.Contains("REX protects this key", console.Output);
    }

    [Fact]
    public async Task UnauthorizedDevice_DoesNotReceiveRuntimeCommands()
    {
        using var package = new TempPackage();
        var bridge = new FakeBridgeClient();
        bridge.Devices.Add(new RexDevice("LOCKED", "unauthorized", false, "", "", ""));
        bridge.DefaultStatus = Status(
            setup: true,
            devices: new[] { new RexDevice("LOCKED", "unauthorized", false, "", "", "") });

        var console = ConsoleWithInput();
        console.Input.PushKey(ConsoleKey.DownArrow); // Runtime controls
        console.Input.PushKey(ConsoleKey.Enter);
        PushDown(console, RexApp.MainMenuChoices.Count - 1); // main menu -> Exit
        console.Input.PushKey(ConsoleKey.Enter);

        var app = new RexApp(
            package.Paths,
            new FakeProcessRunner(),
            bridge,
            package.Config,
            console,
            pauseEnabled: false,
            precisionTouchpadEligible: false);

        Assert.Equal(0, await app.RunAsync());
        Assert.Empty(bridge.Calls);
    }

    private static TestConsole ConsoleWithInput()
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        return console;
    }

    private static void PushDown(TestConsole console, int count)
    {
        for (var i = 0; i < count; i++)
            console.Input.PushKey(ConsoleKey.DownArrow);
    }

    private static RexStatus Status(
        bool setup,
        IReadOnlyList<RexDevice>? devices = null)
    {
        return new RexStatus(
            setup,
            false,
            false,
            false,
            false,
            setup ? "adb.exe" : "",
            setup ? "scrcpy.exe" : "",
            devices ?? Array.Empty<RexDevice>());
    }
}
