using System.Diagnostics;
using System.IO;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Launches scrcpy for one device, finds its window so the host can embed it, and reports
/// when it exits. scrcpy has no control API, so the window handle is the only bridge.
/// </summary>
public sealed class ScrcpyProcess : IDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _stderr = [];

    private ScrcpyProcess(Process process, string serial, string windowTitle)
    {
        _process = process;
        Serial = serial;
        WindowTitle = windowTitle;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => _exit.TrySetResult(SafeExitCode());
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (_stderr)
            {
                _stderr.Add(e.Data);
                if (_stderr.Count > 200)
                {
                    _stderr.RemoveAt(0);
                }
            }
        };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
    }

    public string Serial { get; }
    public string WindowTitle { get; }
    public int ProcessId => _process.Id;
    public IntPtr Hwnd { get; private set; }
    public uint ThreadId { get; private set; }
    public bool HasExited => _process.HasExited;
    public Task<int> Exited => _exit.Task;

    public IReadOnlyList<string> RecentStderr
    {
        get { lock (_stderr) { return _stderr.ToArray(); } }
    }

    public static ScrcpyProcess Launch(
        string scrcpyPath,
        IReadOnlyList<string> arguments,
        string serial,
        string windowTitle,
        OwnedProcessJob ownedProcesses)
    {
        var start = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            WorkingDirectory = Path.GetDirectoryName(scrcpyPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("Windows refused to start scrcpy.");
        try
        {
            // Assign immediately after creation. The Job Object is intentionally scoped
            // to scrcpy-owned session processes; the shared ADB server is never added.
            ownedProcesses.Assign(process);
            return new ScrcpyProcess(process, serial, windowTitle);
        }
        catch
        {
            ProcessRunner.Kill(process);
            process.Dispose();
            throw;
        }
    }

    /// <summary>Waits for scrcpy's window to appear (it only exists once the video stream is up).</summary>
    public async Task<bool> WaitForWindowAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
            {
                return false;
            }

            var hwnd = NativeMethods.FindProcessWindow((uint)_process.Id, WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                Hwnd = hwnd;
                ThreadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
                return true;
            }

            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public void Kill() => ProcessRunner.Kill(_process);

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }

    private int SafeExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
