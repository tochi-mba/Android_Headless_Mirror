using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Tests.Support;

/// <summary>
/// One running RexMirror.exe against a <see cref="TestPackage"/>: drives it over the pipe, with
/// real input, and through UI Automation; captures screenshots; waits for observable state.
/// </summary>
public sealed class AppProcess : IDisposable
{
    private readonly TestPackage _package;
    private readonly string _pipe = "rex-tests-" + Guid.NewGuid().ToString("N");
    private readonly Process _process;
    private readonly IpcClient _client;

    /// <summary>Every window these tests have started, so none can outlive the test that owns it.</summary>
    private static readonly List<Process> Started = [];

    public AppProcess(TestPackage package)
    {
        KillStrays();
        _package = package;
        _client = new IpcClient(_pipe);
        _process = Start();
    }

    /// <summary>
    /// Closes anything an earlier test left running. A test that overruns its timeout is abandoned
    /// where it stands, so its window is never disposed; desktop tests run one at a time, which
    /// makes any window still alive here a leak that would leave the next test driving two windows.
    /// </summary>
    private static void KillStrays()
    {
        lock (Started)
        {
            foreach (var process in Started)
            {
                Kill(process);
            }

            Started.Clear();
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // Already gone, or already disposed by the test that owned it.
        }
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
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start RexMirror.exe.");
        lock (Started)
        {
            Started.Add(process);
        }

        return process;
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
        await FocusAsync();
        var bounds = WindowBounds();
        var dpiContext = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { SetCursorPos(bounds.Right - 100, bounds.Top + bounds.Height / 2); }
        finally { SetThreadDpiAwarenessContext(dpiContext); }
        await Task.Delay(200, TestContext.Current.CancellationToken);
        mouse_event(0x0800, 0, 0, unchecked((uint)-360), UIntPtr.Zero);
    }

    /// <summary>The very corner of the window, for a HUD pinned to bottom-right.</summary>
    public void MovePointerToCorner()
    {
        var bounds = WindowBounds();
        SetPhysicalCursorPos(bounds.Right - 6, bounds.Bottom - 6);
    }

    public void MovePointerToTop()
    {
        var bounds = WindowBounds();
        SetPhysicalCursorPos(bounds.Left + bounds.Width / 2, bounds.Top + 2);
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

    /// <summary>
    /// Presses at one screen point, moves to another and lets go, in steps, the way a hand does.
    /// A single jump would not look like a drag to anything watching the pointer.
    /// </summary>
    public async Task DragAsync(int fromX, int fromY, int toX, int toY)
    {
        MovePointer(fromX, fromY);
        await Task.Delay(80, TestContext.Current.CancellationToken);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        try
        {
            const int steps = 14;
            for (var step = 1; step <= steps; step++)
            {
                MovePointer(
                    fromX + ((toX - fromX) * step / steps),
                    fromY + ((toY - fromY) * step / steps));
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }

            await Task.Delay(120, TestContext.Current.CancellationToken);
        }
        finally
        {
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Puts the pointer at a point in physical pixels, which is what the app reports.
    ///
    /// The awareness has to be set for this one call. It belongs to the thread, and an await in the
    /// middle of a drag can come back on a different one, which would silently scale every point
    /// after it and land the press somewhere else entirely.
    /// </summary>
    private static void MovePointer(int x, int y)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            SetPhysicalCursorPos(x, y);
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    public async Task PressKeyAsync(byte key, bool repeat = false)
    {
        // Never send F11 into an unrelated foreground app: wait for the focus handover first.
        await FocusAsync();
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

        throw new TimeoutException($"Timed out waiting for {description}. Last status: {last?.ToJsonString()}{Environment.NewLine}Log:{Environment.NewLine}{Log()}");
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

        throw new TimeoutException($"Timed out waiting for {description}.{Environment.NewLine}Log:{Environment.NewLine}{Log()}");
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
        using var bitmap = await CaptureWindowAsync();
        Directory.CreateDirectory(RepoPaths.Screens);
        bitmap.Save(Path.Combine(RepoPaths.Screens, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>The window exactly as it appears on screen, for tests that judge what is drawn.</summary>
    public async Task<System.Drawing.Bitmap> CaptureWindowAsync()
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

            var bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
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

            return bitmap;
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
        lock (Started)
        {
            Started.Remove(_process);
        }

        Kill(_process);
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

    /// <summary>UI Automation over the main window (tabs, toggles, sliders, combos, buttons).</summary>
    public AppAutomation Ui => _ui ??= new AppAutomation(FindMainWindow());
    private AppAutomation? _ui;

    /// <summary>Runs rex.exe against this package and returns its stdout.</summary>
    public async Task<string> RunCliAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(RepoPaths.CliExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _package.Root,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[AppPaths.RootEnvironmentVariable] = _package.Root;
        start.Environment[Ipc.PipeNameOverride] = _pipe;
        using var cli = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var stdout = cli.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = cli.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await cli.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr;
            return output;
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!cli.HasExited)
                {
                    cli.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the timeout and cleanup.
            }

            throw new TimeoutException($"rex.exe did not exit within 20 seconds: {string.Join(' ', arguments)}");
        }
    }

    /// <summary>Brings the main window to the foreground the way a user would (Alt tap defeats the foreground lock).</summary>
    public async Task FocusAsync()
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
    }

    /// <summary>Alt + mouse wheel over the mirror, as real input, so the low-level hook path is exercised.</summary>
    public async Task AltWheelOverMirrorAsync(int notches)
    {
        await FocusAsync();
        var bounds = WindowBounds();
        SetPhysicalCursorPos(bounds.Left + bounds.Width / 3, bounds.Top + bounds.Height / 2);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        try
        {
            for (var i = 0; i < Math.Abs(notches); i++)
            {
                mouse_event(0x0800, 0, 0, unchecked((uint)(notches > 0 ? 120 : -120)), UIntPtr.Zero);
                await Task.Delay(80, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
        }
    }

    /// <summary>The close button: with "keep running in the tray" on, the window hides and the app lives on.</summary>
    public void CloseWindow() => PostMessage(FindMainWindow(), 0x0010, IntPtr.Zero, IntPtr.Zero);

    public void MoveWindow(int left, int top, int width, int height)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { Assert.True(SetWindowPos(FindMainWindow(), IntPtr.Zero, left, top, width, height, 0x0004 | 0x0010)); }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    public double DpiScale() => GetDpiForWindow(FindMainWindow()) / 96.0;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
