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
    public async Task AmbientAndNavigatorPreview_RenderStockFixture()
    {
        using var package = new TestPackage(withFakeTools: true);
        using (var stock = System.Drawing.Image.FromFile(Path.Combine(RepoPaths.Root, "tests", "fixtures", "mountain-lake.jpg")))
            stock.Save(Path.Combine(package.ToolsFolder, "preview.png"), System.Drawing.Imaging.ImageFormat.Png);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => s["previewAvailable"]!.GetValue<bool>(), StartupTimeout, "stock preview");
        await app.PressKeyAsync(0x11); // Bring the test window to the foreground before capturing.
        await app.SaveScreenshotAsync("ambient-stock.png");
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.SaveScreenshotAsync("navigator-stock.png");
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
    public async Task PatternGuide_ShowsLoadingUntilAndroidGeometryIsReady()
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
        await app.WaitForStatusAsync(
            data => data["patternGuide"]?["resolving"]?.GetValue<bool>() == true,
            TimeSpan.FromSeconds(8),
            "pattern geometry loading state");
        await app.SaveScreenshotAsync("pattern-loading.png");

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

        await app.ClickHudAsync(18, 38); // Rotation button in the centered HUD.
        await app.SaveScreenshotAsync("fullscreen-rotation.png");
        await app.ClickHudAsync(8, 78); // Landscape in the expanded rotation row.
        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "landscape rotation command");

        await app.ActionAsync("rotation-landscape");
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

    private sealed class AppProcess : IDisposable
    {
        private readonly TestPackage _package;
        private readonly string _pipe = "rex-tests-" + Guid.NewGuid().ToString("N");
        private readonly Process _process;
        private readonly IpcClient _client;

        public AppProcess(TestPackage package)
        {
            _package = package;
            _client = new IpcClient(_pipe);
            _process = Start();
        }

        private Process Start()
        {
            Assert.True(File.Exists(RepoPaths.MirrorExecutable), "RexMirror.exe must be built before the end-to-end tests run.");
            var start = new ProcessStartInfo(RepoPaths.MirrorExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = _package.Root,
            };
            start.ArgumentList.Add("--root");
            start.ArgumentList.Add(_package.Root);
            start.Environment[Ipc.PipeNameOverride] = _pipe;
            start.Environment.Remove(ToolLocator.AdbOverride);
            start.Environment.Remove(ToolLocator.ScrcpyOverride);
            return Process.Start(start) ?? throw new InvalidOperationException("Could not start RexMirror.exe.");
        }

        public Process StartAnother() => Start();

        public async Task ActionAsync(string action)
        {
            var result = await SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = action }));
            Assert.True(result.Ok, result.Error);
        }

        public System.Drawing.Rectangle WindowBounds()
        {
            var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                Assert.True(GetWindowRect(FindMainWindow(), out var rect));
                return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            finally { SetThreadDpiAwarenessContext(previous); }
        }

        public System.Drawing.Rectangle MonitorBounds()
        {
            var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { return System.Windows.Forms.Screen.FromHandle(FindMainWindow()).Bounds; }
            finally { SetThreadDpiAwarenessContext(previous); }
        }

        public void MovePointerToCenter()
        {
            var bounds = WindowBounds();
            SetPhysicalCursorPos(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        public async Task ScrollSidebarAsync()
        {
            var main = FindMainWindow();
            for (var attempt = 0; attempt < 20 && GetAncestor(GetForegroundWindow(), 2) != main; attempt++)
            {
                keybd_event(0x12, 0, 0, UIntPtr.Zero);
                keybd_event(0x12, 0, 2, UIntPtr.Zero);
                await Task.Delay(50, TestContext.Current.CancellationToken);
                SetForegroundWindow(main);
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            Assert.Equal(main, GetAncestor(GetForegroundWindow(), 2));
            var bounds = WindowBounds();
            var dpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { SetCursorPos(bounds.Right - 100, bounds.Top + bounds.Height / 2); }
            finally { SetThreadDpiAwarenessContext(dpiContext); }
            await Task.Delay(200, TestContext.Current.CancellationToken);
            mouse_event(0x0800, 0, 0, unchecked((uint)-360), UIntPtr.Zero);
        }

        public void MovePointerToTop()
        {
            var bounds = WindowBounds();
            SetPhysicalCursorPos(bounds.Left + bounds.Width / 2, bounds.Top + 2);
        }

        public async Task ClickHudAsync(double offsetFromCenter, double top)
        {
            var bounds = WindowBounds();
            var scale = GetDpiForWindow(FindMainWindow()) / 96.0;
            var context = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                SetCursorPos(bounds.Left + bounds.Width / 2 + (int)(offsetFromCenter * scale), bounds.Top + (int)(top * scale));
            }
            finally { SetThreadDpiAwarenessContext(context); }
            await Task.Delay(200, TestContext.Current.CancellationToken);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(80, TestContext.Current.CancellationToken);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        }

        public async Task DragPatternAndCaptureAsync(string name)
        {
            var executable = Path.Combine(_package.ToolsFolder, "scrcpy.exe");
            using var mirror = Process.GetProcessesByName("scrcpy")
                .Single(process => string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase));
            var child = FindChildWindow(FindMainWindow(), mirror.Id);
            Assert.NotEqual(IntPtr.Zero, child);
            Assert.True(GetWindowRect(child, out var rect));

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            var points = new[]
            {
                (X: rect.Left + (int)(width * 0.253), Y: rect.Top + (int)(height * 0.397)),
                (X: rect.Left + (int)(width * 0.747), Y: rect.Top + (int)(height * 0.397)),
                (X: rect.Left + (int)(width * 0.747), Y: rect.Top + (int)(height * 0.618)),
            };

            SetPhysicalCursorPos(points[0].X, points[0].Y);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            try
            {
                for (var target = 1; target < points.Length; target++)
                {
                    var start = points[target - 1];
                    var end = points[target];
                    for (var step = 1; step <= 12; step++)
                    {
                        SetPhysicalCursorPos(
                            start.X + (end.X - start.X) * step / 12,
                            start.Y + (end.Y - start.Y) * step / 12);
                        await Task.Delay(25, TestContext.Current.CancellationToken);
                    }
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
                await SaveScreenshotAsync(name);
            }
            finally
            {
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            }
        }

        public async Task PressKeyAsync(byte key, bool repeat = false)
        {
            // Windows processes injected input asynchronously. Wait for the focus
            // handover, including the app's redirect into its embedded child, before
            // sending shortcuts. Never send F11 into an unrelated foreground app.
            var main = FindMainWindow();
            for (var attempt = 0; attempt < 20 && GetAncestor(GetForegroundWindow(), 2) != main; attempt++)
            {
                keybd_event(0x12, 0, 0, UIntPtr.Zero);
                keybd_event(0x12, 0, 2, UIntPtr.Zero);
                await Task.Delay(50, TestContext.Current.CancellationToken);
                SetForegroundWindow(main);
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            Assert.Equal(main, GetAncestor(GetForegroundWindow(), 2));
            keybd_event(key, 0, 0, UIntPtr.Zero);
            if (repeat)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
                keybd_event(key, 0, 0, UIntPtr.Zero);
            }
            keybd_event(key, 0, 2, UIntPtr.Zero);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        public void KillMirror()
        {
            var executable = Path.Combine(_package.ToolsFolder, "scrcpy.exe");
            using var mirror = Process.GetProcessesByName("scrcpy")
                .Single(process => string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase));
            mirror.Kill();
        }

        public async Task<IpcResponse> SendRawAsync(string line)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", _pipe,
                System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(line.AsMemory(), timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            return Ipc.ParseResponse((await reader.ReadLineAsync(timeout.Token))!);
        }

        public async Task<IpcResponse> SendPartialAsync(string partial)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", _pipe,
                System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, new System.Text.UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(partial.AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            return Ipc.ParseResponse((await reader.ReadLineAsync(timeout.Token))!);
        }

        public bool IsPerMonitorV2()
        {
            var context = GetWindowDpiAwarenessContext(FindMainWindow());
            return AreDpiAwarenessContextsEqual(context, new IntPtr(-4));
        }

        public async Task<JsonObject> WaitForStatusAsync(
            Func<JsonObject, bool> predicate,
            TimeSpan timeout,
            string description)
        {
            var deadline = DateTime.UtcNow + timeout;
            JsonObject? last = null;
            while (DateTime.UtcNow < deadline)
            {
                var response = await SendAsync(new IpcRequest("status"));
                if (response is { Ok: true, Data: JsonObject data })
                {
                    last = data;
                    if (predicate(data))
                    {
                        return data;
                    }
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for {description}. Last status: {last?.ToJsonString()}");
        }

        public async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, string description)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for {description}.");
        }

        public async Task KillAppOnlyAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: false);
            }

            Assert.True(
                await Task.Run(() => _process.WaitForExit(10000), TestContext.Current.CancellationToken),
                "RexMirror.exe did not exit after forced termination.");
        }

        public static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    if (process.HasExited)
                    {
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    return;
                }

                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"Process {processId} was still alive after {timeout}.");
        }

        public async Task<JsonObject> WaitForPhaseAsync(string phase, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            JsonObject? last = null;
            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    Assert.Fail("The app exited early. Log:\n" + Log());
                }
                var response = await _client.SendAsync(new IpcRequest("status"), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
                if (response is { Ok: true, Data: JsonObject data })
                {
                    last = data;
                    if (data["phase"]!.GetValue<string>() == phase)
                    {
                        return data;
                    }
                }

                await Task.Delay(250, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException($"The app never reached phase '{phase}'. Last: {last?.ToJsonString()}\nLog:\n{Log()}");
        }

        public async Task<IpcResponse> SendAsync(IpcRequest request) =>
            await _client.SendAsync(request, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The app did not answer on the pipe.");

        public async Task SaveScreenshotAsync(string name)
        {
            await Task.Yield();
            _ = DwmFlush();
            // GetWindowRect and CopyFromScreen must both use physical pixels on scaled displays.
            var previousDpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                var hwnd = FindMainWindow();
                Assert.NotEqual(IntPtr.Zero, hwnd);
                Assert.True(GetWindowRect(hwnd, out var rect));
                Assert.True(rect.Right - rect.Left >= 720 && rect.Bottom - rect.Top >= 480,
                    "The capture must contain the app window, not a tooltip or tray window.");

                Directory.CreateDirectory(RepoPaths.Screens);
                using var bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
                for (var attempt = 0; ; attempt++)
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
                    }

                    // A freshly shown WPF window is white until its first frame; the app is always dark.
                    if (!IsUnpainted(bitmap) || attempt >= 20)
                    {
                        break;
                    }

                    await Task.Delay(150, TestContext.Current.CancellationToken);
                }

                bitmap.Save(Path.Combine(RepoPaths.Screens, name), System.Drawing.Imaging.ImageFormat.Png);
            }
            finally
            {
                SetThreadDpiAwarenessContext(previousDpiContext);
            }
        }

        private static bool IsUnpainted(System.Drawing.Bitmap bitmap)
        {
            for (var y = bitmap.Height / 4; y < bitmap.Height; y += bitmap.Height / 4)
            {
                for (var x = bitmap.Width / 4; x < bitmap.Width; x += bitmap.Width / 4)
                {
                    if (bitmap.GetPixel(x, y).GetBrightness() < 0.9f)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static IntPtr FindChildWindow(IntPtr parent, int processId)
        {
            var found = IntPtr.Zero;
            EnumChildWindows(parent, (window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner == processId)
                {
                    found = window;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        public async Task QuitAsync()
        {
            await _client.SendAsync(new IpcRequest("quit"), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(await Task.Run(() => _process.WaitForExit(15000), TestContext.Current.CancellationToken), "The app did not exit after quit. Log:\n" + Log());
        }

        private string Log()
        {
            var path = _package.Paths.LogFile;
            if (!File.Exists(path))
            {
                return "(no log)";
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException ex)
            {
                return $"(log temporarily unavailable: {ex.Message})";
            }
        }

        private IntPtr FindMainWindow()
        {
            // Process.MainWindowHandle may select a tooltip owned by the WPF process.
            var found = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == _process.Id)
                {
                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(hwnd, title, title.Capacity);
                    if (title.ToString() == "Android Headless Mirror")
                    {
                        found = hwnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            _process.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern bool SetPhysicalCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}

[CollectionDefinition("desktop", DisableParallelization = true)]
public sealed class DesktopCollection;
