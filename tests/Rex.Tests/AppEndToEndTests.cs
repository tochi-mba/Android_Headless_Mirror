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

    [Fact]
    public async Task AmbientBackgroundAndNavigator_FollowTheLiveMirror()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        // The soft background is a live copy of the on-screen mirror, captured small and blurred
        // before it ever reaches the window; nothing is fetched from the phone for it.
        await app.WaitForStatusAsync(
            s => s["ambientVisible"]!.GetValue<bool>() && s["ambientFrame"]!.GetValue<bool>(),
            StartupTimeout,
            "live soft background");
        await app.FocusAsync();
        await app.SaveScreenshotAsync("ambient-live.png");
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.WaitForStatusAsync(s => s["navigatorVisible"]!.GetValue<bool>(), StartupTimeout, "navigator");
        await app.SaveScreenshotAsync("navigator.png");

        // The navigator is a frame and a draggable viewport box, so it costs nothing to keep open:
        // zooming must not send a screenshot request to the phone.
        Assert.DoesNotContain(package.AdbCalls(), line => line.Contains("screencap", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact]
    public async Task SoftBackground_PaintsTheSpaceAroundThePhone()
    {
        // The soft background is meant to be seen. Checking that a picture is attached proves
        // nothing: a copy taken from the screen carries no transparency, so a frame can be present
        // and still draw as nothing at all. This compares what the window actually looks like with
        // the background on and off, and the phone itself is identical in both.
        using var package = new TestPackage(withFakeTools: true);
        TestPackage.WritePreviewImage(Path.Combine(package.ToolsFolder, "preview.png"));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(
            s => s["ambientVisible"]!.GetValue<bool>() && s["ambientFrame"]!.GetValue<bool>(),
            StartupTimeout,
            "soft background");
        await app.FocusAsync();
        using var lit = await app.CaptureWindowAsync();

        // Turning it off in the config file also exercises the live reload the settings panel uses.
        var config = ConfigFile.Load(package.Paths.Config);
        config.Ambient.Enabled = false;
        ConfigFile.Save(package.Paths.Config, config);
        await app.WaitForStatusAsync(s => !s["ambientVisible"]!.GetValue<bool>(), StartupTimeout, "soft background off");
        using var dark = await app.CaptureWindowAsync();

        var painted = PaintedFraction(lit, dark);
        Assert.True(painted > 0.15,
            $"The soft background must fill the space around the phone; only {painted:P0} of the window changed when it was switched off.");
        await app.QuitAsync();
    }

    /// <summary>
    /// How much of the window two captures disagree about, sampled coarsely because a blurred
    /// background covers broad areas and has no fine detail to miss.
    /// </summary>
    private static double PaintedFraction(System.Drawing.Bitmap lit, System.Drawing.Bitmap dark)
    {
        Assert.Equal(lit.Size, dark.Size);
        var changed = 0;
        var count = 0;
        for (var y = 0; y < lit.Height; y += 4)
        {
            for (var x = 0; x < lit.Width; x += 4)
            {
                var a = lit.GetPixel(x, y);
                var b = dark.GetPixel(x, y);
                var difference = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                if (difference > 12)
                {
                    changed++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : (double)changed / count;
    }

    [Fact]
    public async Task Landscape_RefitsThePictureAndTheSoftBackgroundAroundIt()
    {
        // Turning the phone changes the picture's shape. Keeping the old view would leave a wide
        // picture inside the tall rectangle the portrait one left behind: letterboxed on every
        // side, with the soft background still masked around a phone that is no longer there.
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => s["ambientVisible"]!.GetValue<bool>(), StartupTimeout, "soft background");

        var portrait = await Surface(app);
        Assert.True(portrait.Width < portrait.Height, "The phone starts upright.");
        Assert.InRange(portrait.Height, portrait.ViewportHeight - 2, portrait.ViewportHeight + 2);

        await app.ActionAsync("rotation-landscape");
        await app.WaitForStatusAsync(
            s => s["surface"]!["width"]!.GetValue<double>() > s["surface"]!["height"]!.GetValue<double>(),
            StartupTimeout,
            "the picture to turn");

        var landscape = await Surface(app);
        Assert.InRange(landscape.Width, landscape.ViewportWidth - 2, landscape.ViewportWidth + 2);
        Assert.True(landscape.Height <= landscape.ViewportHeight + 2, "The picture must still fit inside the mirror area.");
        await app.SaveScreenshotAsync("landscape-refit.png");
        await app.QuitAsync();
    }

    private static async Task<(double Width, double Height, double ViewportWidth, double ViewportHeight)> Surface(AppProcess app)
    {
        var status = await app.SendAsync(new IpcRequest("status"));
        var surface = status.Data!["surface"]!;
        return (
            surface["width"]!.GetValue<double>(),
            surface["height"]!.GetValue<double>(),
            surface["viewportWidth"]!.GetValue<double>(),
            surface["viewportHeight"]!.GetValue<double>());
    }

    [Fact]
    public async Task FullscreenControls_CanBeDraggedAnywhereAndPinnedBack()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.PressKeyAsync(0x7A); // F11
        await app.WaitForStatusAsync(
            data => data["fullscreen"]!.GetValue<bool>() && data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(15),
            "fullscreen controls");
        var monitor = app.MonitorBounds();

        // Dragging the bar itself puts it wherever it is dropped, and that is remembered. The press
        // lands just inside its leading edge, which is bar rather than button.
        app.MovePointerToTop();
        var bar = await SettledBar(app);
        var grabX = (int)(bar.Left + 4);
        var grabY = (int)(bar.Top + (bar.Height * 0.25));
        var dropX = monitor.Left + (int)(monitor.Width * 0.45);
        var dropY = monitor.Top + (int)(monitor.Height * 0.7);
        await app.DragAsync(grabX, grabY, dropX, dropY);

        await app.WaitUntilAsync(
            () => ConfigFile.Load(package.Paths.Config).Hud.IsPlaced,
            TimeSpan.FromSeconds(10),
            "the dragged position to be saved");

        // The bar follows the pointer: it keeps the place it was grabbed by, rather than jumping
        // its middle under the cursor.
        var placed = ConfigFile.Load(package.Paths.Config).Hud;
        var expectedX = (bar.Left + (bar.Width / 2) + (dropX - grabX)) / monitor.Width;
        var expectedY = (bar.Top + (bar.Height / 2) + (dropY - grabY)) / monitor.Height;
        Assert.Equal(expectedX, placed.X!.Value, 1);
        Assert.Equal(expectedY, placed.Y!.Value, 1);

        var moved = await SettledBar(app);
        Assert.True(moved.Top > monitor.Top + (monitor.Height / 2),
            "The controls must sit where they were dropped.");
        await app.SaveScreenshotAsync("fullscreen-hud-dragged.png");

        // Pressing a control is a click, never a drag: the bar must not run away with the pointer.
        var buttonX = (int)(moved.Left + (moved.Width * 0.17));
        var buttonY = (int)(moved.Top + (moved.Height * 0.25));
        await app.DragAsync(buttonX, buttonY, buttonX, buttonY);
        Assert.Contains(package.AdbCalls(), line => line.Contains("keyevent", StringComparison.Ordinal));
        var after = ConfigFile.Load(package.Paths.Config).Hud;
        Assert.Equal(placed.X, after.X);
        Assert.Equal(placed.Y, after.Y);

        // Choosing a position again pins them back, wherever they were dragged to.
        var config = ConfigFile.Load(package.Paths.Config);
        config.Hud.Position = "bottom";
        config.Hud.X = null;
        config.Hud.Y = null;
        ConfigFile.Save(package.Paths.Config, config);
        await app.WaitForStatusAsync(
            data => data["hudBar"]!["top"]!.GetValue<double>() > monitor.Top + (monitor.Height * 0.8),
            TimeSpan.FromSeconds(15),
            "the controls to go back to the bottom");

        await app.PressKeyAsync(0x1B); // Esc leaves fullscreen.
        await app.WaitForStatusAsync(data => !data["fullscreen"]!.GetValue<bool>(), TimeSpan.FromSeconds(10), "windowed again");
        await app.QuitAsync();
    }

    /// <summary>
    /// The bar once it has stopped moving. It is centred on whatever it is pinned to and its status
    /// line changes width as it reports things, so a position read a moment too early is no longer
    /// where the bar is when a press arrives.
    /// </summary>
    private static async Task<(double Left, double Top, double Width, double Height)> SettledBar(AppProcess app)
    {
        var previous = (Left: double.NaN, Top: double.NaN, Width: double.NaN, Height: double.NaN);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var status = await app.SendAsync(new IpcRequest("status"));
            var bar = status.Data!["hudBar"]!;
            var current = (
                bar["left"]!.GetValue<double>(),
                bar["top"]!.GetValue<double>(),
                bar["width"]!.GetValue<double>(),
                bar["height"]!.GetValue<double>());
            if (current == previous && current.Item3 > 0)
            {
                return current;
            }

            previous = current;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The fullscreen controls never settled in one place.");
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Theory]
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

    [Fact]
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
