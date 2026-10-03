using System.Net.NetworkInformation;
using Rex.Core;
using Rex.Mirror.Mirror;

namespace Rex.Mirror.Session;

/// <summary>The outcome of opening the second screen's session.</summary>
public sealed record ScreenLaunch(ScrcpyProcess? Process, string Reason);

public sealed partial class SessionController
{
    /// <summary>The second screen's window title, unique so its window can be found.</summary>
    public static string ScreenTitle(string serial) => $"Android Headless Mirror [{serial}] second screen";

    /// <summary>
    /// Starts the second screen: a session of its own with a display of its own on the phone
    /// (<see cref="ScrcpyArguments.BuildScreen"/>), on the first free port of its range, through the
    /// server start gate, owned like the main session so it cannot outlive the app.
    /// </summary>
    public async Task<ScreenLaunch> LaunchScreenAsync(ScreenSpec spec, (int X, int Y) at, CancellationToken cancellationToken = default)
    {
        var device = ActiveDevice;
        if (device is null || Tools is null || !IsMirroring)
        {
            return new ScreenLaunch(null, "The phone is not being mirrored.");
        }

        var busy = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(endpoint => endpoint.Port);
        if (ScrcpyArguments.FirstFreeScreenPort(busy) is not { } port)
        {
            return new ScreenLaunch(null, $"Other programs hold every port from {ScrcpyArguments.ScreenPort} to {ScrcpyArguments.LastScreenPort}.");
        }

        var config = _host.Config;
        var title = ScreenTitle(device.Serial);
        var keyboardMode = ScrcpyArguments.KeyboardModeFor(config, _host.State.GetDevice(device.Serial));
        var args = ScrcpyArguments.BuildScreen(config, device.Serial, device.IsTcp, title, at, spec, keyboardMode, port);
        _host.Log.Info($"Opening the second screen of {device.Serial}: {string.Join(' ', args)}");

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
                _host.Log.Error($"Could not start the second screen of '{device.Serial}'", ex);
                return new ScreenLaunch(null, "scrcpy could not start: " + ex.Message);
            }

            appeared = await scrcpy.WaitForWindowAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(true);
        }

        if (!appeared)
        {
            var reason = ScrcpyArguments.ScreenFailure(scrcpy.RecentStderr);
            _host.Log.Warn("The second screen did not open a window: " + string.Join(" | ", scrcpy.RecentStderr.TakeLast(5)));
            scrcpy.Dispose();
            return new ScreenLaunch(null, reason);
        }

        return new ScreenLaunch(scrcpy, string.Empty);
    }

    /// <summary>Opens another app on a display of the phone's, the second screen's.</summary>
    public Task<AndroidResult> OpenAppOnDisplayAsync(string serial, string package, bool fresh, int displayId) =>
        Adb is null
            ? Task.FromResult(AndroidResult.Failure("The phone tools are not installed yet."))
            : Adb.LaunchAppAsync(serial, package, fresh, displayId);
}
