using Rex.Core;
using Rex.Mirror.Mirror;

namespace Rex.Mirror.Session;

/// <summary>The outcome of starting one copy of the phone.</summary>
public sealed record CopyLaunch(ScrcpyProcess? Process, string Reason)
{
    public bool Started => Process is not null;
}

public sealed partial class SessionController
{
    /// <summary>The window title of copy <paramref name="index"/>, unique so its window can be found.</summary>
    public static string CopyTitle(string serial, int index) =>
        $"Android Headless Mirror [{serial}] copy {index + 1}";

    /// <summary>
    /// Starts copy number <paramref name="index"/> of the phone being mirrored: its own scrcpy
    /// session with the copy flags (<see cref="ScrcpyArguments.Build"/>), owned like the main one so
    /// it cannot outlive the app, and waited for until its window exists. Returns why when it could
    /// not start (the phone has no encoder left, the session could not connect, …).
    /// </summary>
    public async Task<CopyLaunch> LaunchCopyAsync(int index, (int X, int Y, int Width, int Height)? window, CancellationToken cancellationToken = default)
    {
        var device = ActiveDevice;
        if (device is null || Tools is null || !IsMirroring)
        {
            return new CopyLaunch(null, "The phone is not being mirrored.");
        }

        var config = _host.Config;
        var title = CopyTitle(device.Serial, index);
        var keyboardMode = ScrcpyArguments.KeyboardModeFor(config, _host.State.GetDevice(device.Serial));
        var args = ScrcpyArguments.Build(config, device.Serial, device.IsTcp, title, window, recordPath: null, keyboardMode, copyIndex: index,
            displayOrientation: ViewOrientation);
        _host.Log.Info($"Starting copy {index + 1} of {device.Serial}: {string.Join(' ', args)}");

        ScrcpyProcess scrcpy;
        bool appeared;
        using (await ServerStart.EnterAsync(cancellationToken).ConfigureAwait(true))
        {
            try
            {
                scrcpy = ScrcpyProcess.Launch(Tools.Scrcpy, args, device.Serial, title, keyboardMode, _ownedProcesses);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _host.Log.Error($"Could not start copy {index + 1} of '{device.Serial}'", ex);
                return new CopyLaunch(null, "scrcpy could not start: " + ex.Message);
            }

            appeared = await scrcpy.WaitForWindowAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(true);
        }

        if (!appeared)
        {
            var reason = CopyFailure(scrcpy.RecentStderr);
            _host.Log.Warn($"Copy {index + 1} did not open a window: {reason}");
            scrcpy.Dispose();
            return new CopyLaunch(null, reason);
        }

        return new CopyLaunch(scrcpy, string.Empty);
    }

    /// <summary>
    /// Says why a copy failed, in words, from what scrcpy printed. The phone running out of video
    /// encoders is the one worth naming: it is the limit on how many copies a phone can hold.
    /// </summary>
    internal static string CopyFailure(IEnumerable<string> stderr)
    {
        var text = string.Join('\n', stderr);
        if (text.Contains("Could not create default video encoder", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Exception on thread Thread[video", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Failed to initialize video", StringComparison.OrdinalIgnoreCase))
        {
            return "The phone could not encode another copy. It has run out of video encoders.";
        }

        if (text.Contains("Didn't find class \"com.genymobile.scrcpy.Server\"", StringComparison.Ordinal) ||
            text.Contains("Server connection failed", StringComparison.OrdinalIgnoreCase))
        {
            return "The copy could not connect to the phone.";
        }

        if (text.Contains("Device disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return "The phone disconnected.";
        }

        var last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrEmpty(last) ? "The copy closed without saying why." : last;
    }
}
