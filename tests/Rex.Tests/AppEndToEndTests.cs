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
    public async Task MirrorsAFakePhone_ZoomsAndAcceptsActions()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        Assert.Equal("FAKE123", status["device"]!["serial"]!.GetValue<string>());
        Assert.Equal("Galaxy S21 Ultra", status["device"]!["name"]!.GetValue<string>());
        Assert.Contains(package.ScrcpyLog(), line => line.Contains("--window-borderless", StringComparison.Ordinal) && line.Contains("--shortcut-mod=rctrl+ralt", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.EndsWith("shell wm dismiss-keyguard", StringComparison.Ordinal));

        var zoomed = await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        Assert.True(zoomed.Ok, zoomed.Error);
        Assert.True(zoomed.Data!["zoom"]!.GetValue<double>() > 1.0);
        await app.SaveScreenshotAsync("mirroring-zoomed.png");

        var reset = await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "reset" }));
        Assert.Equal(1.0, reset.Data!["zoom"]!.GetValue<double>());

        var sleep = await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "sleep" }));
        Assert.True(sleep.Ok, sleep.Error);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var keys = package.ScrcpyLog().Where(l => l.StartsWith("key ", StringComparison.Ordinal)).ToArray();
        Assert.Contains(keys, k => k.Contains("vk=163", StringComparison.Ordinal)); // Right Ctrl
        Assert.Contains(keys, k => k.Contains("vk=165", StringComparison.Ordinal)); // Right Alt
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
        await app.SaveScreenshotAsync("first-run.png");
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
        Assert.True((await app.SendAsync(new IpcRequest("ping"))).Ok);
        await app.QuitAsync();
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
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var status = (await app.SendAsync(new IpcRequest("status"))).Data!;
        Assert.True(status["fullscreen"]!.GetValue<bool>());
        Assert.Equal(1, status["zoom"]!.GetValue<double>());
        Assert.True(status["hudVisible"]!.GetValue<bool>());
        Assert.Equal(app.MonitorBounds(), app.WindowBounds());
        await app.SaveScreenshotAsync("fullscreen-hud.png");

        app.MovePointerToCenter();
        await Task.Delay(3600, TestContext.Current.CancellationToken);
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["hudVisible"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("fullscreen-clean.png");
        app.MovePointerToTop();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.True((await app.SendAsync(new IpcRequest("status"))).Data!["hudVisible"]!.GetValue<bool>());

        await app.ClickHudAsync(18, 38); // Rotation button in the centered HUD.
        await Task.Delay(250, TestContext.Current.CancellationToken);
        await app.SaveScreenshotAsync("fullscreen-rotation.png");
        await app.ClickHudAsync(8, 78); // Landscape in the expanded rotation row.
        await Task.Delay(800, TestContext.Current.CancellationToken);
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal));

        await app.ActionAsync("rotation-landscape");
        await app.ActionAsync("rotation-portrait");
        await app.ActionAsync("rotation-auto");
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 0", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system accelerometer_rotation 1", StringComparison.Ordinal));
        await app.PressKeyAsync(0x1B); // Escape returns to the previous window bounds.
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

        public async Task PressKeyAsync(byte key, bool repeat = false)
        {
            // A background test runner cannot always activate another process until it
            // has received input. Send a harmless Alt press before requesting focus.
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(FindMainWindow());
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Equal(FindMainWindow(), GetAncestor(GetForegroundWindow(), 2));
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

        public async Task<JsonObject> WaitForPhaseAsync(string phase, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            JsonObject? last = null;
            while (DateTime.UtcNow < deadline)
            {
                Assert.False(_process.HasExited, "The app exited early. Log:\n" + Log());
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
            await Task.Delay(800, TestContext.Current.CancellationToken);
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
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
                }

                bitmap.Save(Path.Combine(RepoPaths.Screens, name), System.Drawing.Imaging.ImageFormat.Png);
            }
            finally
            {
                SetThreadDpiAwarenessContext(previousDpiContext);
            }
        }

        public async Task QuitAsync()
        {
            await _client.SendAsync(new IpcRequest("quit"), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(await Task.Run(() => _process.WaitForExit(15000), TestContext.Current.CancellationToken), "The app did not exit after quit. Log:\n" + Log());
        }

        private string Log()
        {
            var path = _package.Paths.LogFile;
            return File.Exists(path) ? File.ReadAllText(path) : "(no log)";
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
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern bool SetPhysicalCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
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
