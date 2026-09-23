using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Launches the real desktop app against the fake adb and fake scrcpy, drives it over the pipe
/// and saves screenshots under artifacts/screens. This is the closest CI gets to a phone.
/// </summary>
[Collection("desktop")]
public sealed class AppEndToEndTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);

    [Fact(Timeout = 90_000)]
    public async Task AmbientAndNavigatorPreview_RenderThePhoneCapture()
    {
        using var package = new TestPackage(withFakeTools: true);
        TestPackage.WritePreviewImage(Path.Combine(package.ToolsFolder, "preview.png"));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        // The soft background is a live copy of the on-screen mirror; the navigator thumbnail is a phone screenshot.
        await app.WaitForStatusAsync(s => s["ambientVisible"]!.GetValue<bool>(), StartupTimeout, "live soft background");
        await app.FocusAsync();
        await app.SaveScreenshotAsync("ambient-live.png");
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.WaitForStatusAsync(s => s["previewAvailable"]!.GetValue<bool>() && s["navigatorVisible"]!.GetValue<bool>(), StartupTimeout, "navigator thumbnail");
        await app.SaveScreenshotAsync("navigator-thumbnail.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task SidebarWheelScrollsSettingsWithoutReachingPhone()
    {
        using var package = new TestPackage(withFakeTools: true);
        new StateStore(package.Paths.State).SetUi(new UiState { SidebarTab = "settings", SidebarVisible = true });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var before = package.ScrcpyLog().Count(line => line.Contains("mousewheel", StringComparison.Ordinal));
        await app.ScrollSidebarAsync();
        await app.SaveScreenshotAsync("sidebar-scroll.png");
        await app.WaitForStatusAsync(s => s["sidebarScrollOffset"]!.GetValue<double>() > 0, TimeSpan.FromSeconds(5), "sidebar scroll");
        Assert.Equal(before, package.ScrcpyLog().Count(line => line.Contains("mousewheel", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 90_000)]
    public async Task MirrorsAFakePhone_ZoomsAndAcceptsActions()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        Assert.Equal("FAKE123", status["device"]!["serial"]!.GetValue<string>());
        Assert.Equal("Galaxy S21 Ultra", status["device"]!["name"]!.GetValue<string>());
        Assert.True(app.IsPerMonitorV2(), "The main window must run with PerMonitorV2 DPI awareness.");
        Assert.Contains(package.ScrcpyLog(), line => line.Contains("--window-borderless", StringComparison.Ordinal) && line.Contains("--shortcut-mod=rctrl", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.EndsWith("shell wm dismiss-keyguard", StringComparison.Ordinal));

        var zoomed = await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        Assert.True(zoomed.Ok, zoomed.Error);
        Assert.True(zoomed.Data!["zoom"]!.GetValue<double>() > 1.0);
        await app.SaveScreenshotAsync("mirroring-zoomed.png");

        var reset = await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "reset" }));
        Assert.Equal(1.0, reset.Data!["zoom"]!.GetValue<double>());

        var sleep = await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "sleep" }));
        Assert.True(sleep.Ok, sleep.Error);
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(l => l.StartsWith("key ", StringComparison.Ordinal) && l.Contains("vk=79", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "scrcpy shortcut key log");
        var keys = package.ScrcpyLog().Where(l => l.StartsWith("key ", StringComparison.Ordinal)).ToArray();
        Assert.Contains(keys, k => k.Contains("vk=163", StringComparison.Ordinal)); // Right Ctrl
        Assert.DoesNotContain(keys, k => k.Contains("vk=165", StringComparison.Ordinal)); // Right Alt is not part of the scrcpy modifier.
        Assert.Contains(keys, k => k.Contains("vk=79", StringComparison.Ordinal));  // O

        var home = await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "home" }));
        Assert.True(home.Ok, home.Error);
        Assert.Contains(package.AdbCalls(), line => line.EndsWith("input keyevent KEYCODE_HOME", StringComparison.Ordinal));

        var shot = await app.SendAsync(new IpcRequest("screenshot"));
        Assert.True(shot.Ok, shot.Error);
        Assert.True(File.Exists(shot.Data!["path"]!.GetValue<string>()));

        await app.SaveScreenshotAsync("mirroring.png");

        var stop = await app.SendAsync(new IpcRequest("session-stop"));
        Assert.True(stop.Ok, stop.Error);
        var stopped = await app.WaitForPhaseAsync("stopped", TimeSpan.FromSeconds(15));
        Assert.False(stopped["mirroring"]!.GetValue<bool>());

        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task WithoutTools_ShowsFirstRunSetup()
    {
        using var package = new TestPackage(withFakeTools: false);
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("needssetup", StartupTimeout);

        Assert.False(status["mirroring"]!.GetValue<bool>());
        Assert.True(status["onboarding"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("first-run-no-scrcpy.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task FirstRun_GuidesUntilTheFirstPhoneIsMirrored()
    {
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        using var app = new AppProcess(package);
        var waiting = await app.WaitForPhaseAsync("waiting", StartupTimeout);
        Assert.True(waiting["onboarding"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("first-run.png");

        package.WriteScenario(new { Devices = new[] { new { Serial = "FAKE123", State = "unauthorized", Model = "Fake Phone" } } });
        await app.WaitForStatusAsync(data => data["onboarding"]!.GetValue<bool>() && data["phase"]!.GetValue<string>() == "waiting"
            && data["message"]!.GetValue<string>().Contains("Allow", StringComparison.Ordinal), StartupTimeout, "the tap-Allow hint");
        await app.SaveScreenshotAsync("first-run-allow.png");

        File.Delete(package.FakeAdbScenario);
        var mirroring = await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.False(mirroring["onboarding"]!.GetValue<bool>());
        await app.QuitAsync();

        // Once a phone has been mirrored the guide stays away, even with nothing connected.
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        using var second = new AppProcess(package);
        Assert.False((await second.WaitForPhaseAsync("waiting", StartupTimeout))["onboarding"]!.GetValue<bool>());
        await second.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task PatternGuide_DiscoversAndroidGeometryAndRenders()
    {
        using var package = new TestPackage(withFakeTools: true);
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        package.WriteScenario(new
        {
            Devices = new[] { new { Serial = "FAKE123", State = "device", Model = "Fake Phone" } },
            KeyguardLocked = true,
            UiHierarchyDelayMs = 1500,
            UiHierarchy = """
                <hierarchy rotation="0"><node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
                  <node class="com.android.internal.widget.LockPatternView" bounds="[140,820][940,1620]" />
                </node></hierarchy>
                """,
        });

        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var ready = await app.WaitForStatusAsync(
            data => data["patternGuide"]?["visible"]?.GetValue<bool>() == true &&
                data["patternGuide"]?["resolving"]?.GetValue<bool>() == false &&
                data["patternGuide"]?["source"]?.GetValue<string>() == PatternGeometry.SourceUiView,
            TimeSpan.FromSeconds(8),
            "discovered pattern geometry");
        Assert.Equal(PatternGeometry.SourceUiView, ready["patternGuide"]!["source"]!.GetValue<string>());
        await app.SaveScreenshotAsync("pattern-ready.png");
        await app.DragPatternAndCaptureAsync("pattern-trace.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task PhoneThatReenumerates_IsPickedUpAgainWithoutPressingStart()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // The phone drops off USB (a Samsung changes USB mode on unlock) and scrcpy dies with it.
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        await app.WaitForStatusAsync(data => data["visibleDevices"]!.GetValue<int>() == 0, TimeSpan.FromSeconds(10), "ADB to lose the phone");
        app.KillMirror();
        var waiting = await app.WaitForPhaseAsync("waiting", StartupTimeout);
        Assert.Contains("disconnected", waiting["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

        File.Delete(package.FakeAdbScenario);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.Equal(2, package.ScrcpyLog().Count(line => line.StartsWith("args ", StringComparison.Ordinal)));
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task SecondInstance_HandsOverToTheFirst()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        using var second = app.StartAnother();
        Assert.True(await Task.Run(() => second.WaitForExit(15000), TestContext.Current.CancellationToken));
        Assert.Equal(0, second.ExitCode);
        Assert.True((await app.SendAsync(new IpcRequest("ping"))).Ok);

        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task Pipe_AcceptsConcurrentClientsAndSurvivesMalformedRequest()
    {
        using var package = new TestPackage(withFakeTools: false);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("needssetup", StartupTimeout);

        var replies = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => app.SendAsync(new IpcRequest("ping"))));
        Assert.All(replies, reply => Assert.True(reply.Ok, reply.Error));
        var malformed = await app.SendRawAsync("{\"command\":42}");
        Assert.False(malformed.Ok);

        var unsupported = await app.SendRawAsync("""{"v":1,"command":"ping","args":{}}""");
        Assert.False(unsupported.Ok);
        Assert.Contains("Unsupported protocol version", unsupported.Error);

        var oversized = await app.SendRawAsync(new string('x', Rex.Mirror.Services.PipeServer.MaxRequestBytes + 1));
        Assert.False(oversized.Ok);
        Assert.Contains("byte limit", oversized.Error);

        var stalled = await app.SendPartialAsync("{\"v\":2,\"command\":\"ping\"");
        Assert.False(stalled.Ok);
        Assert.Contains("timed out", stalled.Error, StringComparison.OrdinalIgnoreCase);

        Assert.True((await app.SendAsync(new IpcRequest("ping"))).Ok);
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task ForceKillingRex_CleansOwnedScrcpyProcessTree()
    {
        using var package = new TestPackage(withFakeTools: true, configure: config =>
            config.Mirror.ExtraArgs = "--rex-spawn-child");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(line => line.StartsWith("child-pid ", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "fake scrcpy child process");

        var processIds = package.ScrcpyLog()
            .Where(line => line.StartsWith("pid ", StringComparison.Ordinal) || line.StartsWith("child-pid ", StringComparison.Ordinal))
            .Select(line => int.Parse(line[(line.LastIndexOf(' ') + 1)..]))
            .Distinct()
            .ToArray();
        Assert.True(processIds.Length >= 2, "Expected fake scrcpy and its child process.");

        await app.KillAppOnlyAsync();

        foreach (var processId in processIds)
        {
            await AppProcess.WaitForProcessExitAsync(processId, TimeSpan.FromSeconds(10));
        }
    }

    [Theory(Timeout = 90_000)]
    [InlineData(false, 1)]
    [InlineData(true, 5)]
    public async Task UnexpectedExit_RespectsRestartPreferenceAndLimit(bool restart, int launches)
    {
        using var package = new TestPackage(withFakeTools: true, configure: config =>
        {
            config.Session.RestartOnUnexpectedExit = restart;
            config.Session.RetrySeconds = 1;
        });
        using var app = new AppProcess(package);
        for (var i = 0; i < launches; i++)
        {
            await app.WaitForPhaseAsync("mirroring", StartupTimeout);
            app.KillMirror();
            await app.WaitForPhaseAsync(i == launches - 1 ? "stopped" : "waiting", StartupTimeout);
        }

        await Task.Delay(2500, TestContext.Current.CancellationToken);
        Assert.Equal(launches, package.ScrcpyLog().Count(line => line.StartsWith("args ", StringComparison.Ordinal)));
        // An explicit restart must still work when automatic restart is disabled or exhausted.
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForPhaseAsync("waiting", StartupTimeout);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.QuitAsync();
    }

    [Fact(Timeout = 90_000)]
    public async Task Fullscreen_FillsMonitorHidesHudRotatesAndRestoresWindow()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var windowed = app.WindowBounds();
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.PressKeyAsync(0x7A, repeat: true); // F11: holding it must only toggle once.
        var status = await app.WaitForStatusAsync(
            data => data["fullscreen"]!.GetValue<bool>() && data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5),
            "fullscreen HUD");
        Assert.True(status["fullscreen"]!.GetValue<bool>());
        Assert.Equal(1, status["zoom"]!.GetValue<double>());
        Assert.True(status["hudVisible"]!.GetValue<bool>());
        Assert.Equal(app.MonitorBounds(), app.WindowBounds());
        await app.SaveScreenshotAsync("fullscreen-hud.png");

        app.MovePointerToCenter();
        await app.WaitForStatusAsync(
            data => !data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5),
            "HUD auto-hide");
        await app.SaveScreenshotAsync("fullscreen-clean.png");
        app.MovePointerToTop();
        await app.WaitForStatusAsync(
            data => data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(3),
            "HUD reveal");

        await app.ActionAsync("rotation-landscape");
        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "landscape rotation command");
        await app.SaveScreenshotAsync("fullscreen-rotation.png");
        await app.ActionAsync("rotation-portrait");
        await app.ActionAsync("rotation-auto");
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 0", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system accelerometer_rotation 1", StringComparison.Ordinal));
        await app.PressKeyAsync(0x1B); // Escape returns to the previous window bounds.
        await app.WaitForStatusAsync(
            data => !data["fullscreen"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5),
            "windowed mode after Escape");
        await app.WaitUntilAsync(
            () => app.WindowBounds() == windowed,
            TimeSpan.FromSeconds(5),
            "window bounds to restore after leaving fullscreen");
        Assert.Equal(windowed, app.WindowBounds());

        await app.ActionAsync("fullscreen");
        await app.SendAsync(new IpcRequest("session-stop"));
        await app.WaitForPhaseAsync("stopped", StartupTimeout);
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["fullscreen"]!.GetValue<bool>());
        await app.QuitAsync();
    }
}

[CollectionDefinition("desktop", DisableParallelization = true)]
public sealed class DesktopCollection;
