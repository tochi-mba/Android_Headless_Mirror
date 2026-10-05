using Rex.Core;
using Rex.Mirror.Mirror;

namespace Rex.Mirror.Session;

public sealed partial class SessionController
{
    /// <summary>
    /// Says whether a phone belongs to the session beside the main one, so the main session never
    /// picks it (under either of its serials) while it is shown there.
    /// </summary>
    public Func<AdbDevice, bool>? ShownBeside { get; set; }

    /// <summary>The other phone's window title, unique so its window can be found.</summary>
    public static string OtherPhoneTitle(string serial) => $"Android Headless Mirror [{serial}] beside";

    /// <summary>
    /// Starts the session of a second, different phone (<see cref="ScrcpyArguments.BuildOtherPhone"/>)
    /// through the server start gate, owned like the main session so it cannot outlive the app.
    /// </summary>
    public async Task<ScreenLaunch> LaunchOtherPhoneAsync(AdbDevice device, (int X, int Y, int Width, int Height)? window, CancellationToken cancellationToken = default)
    {
        if (Tools is null)
        {
            return new ScreenLaunch(null, "The phone tools are not installed yet.");
        }

        var config = _host.Config;
        var title = OtherPhoneTitle(device.Serial);
        var keyboardMode = ScrcpyArguments.KeyboardModeFor(config, _host.State.GetDevice(device.Serial));
        var args = ScrcpyArguments.BuildOtherPhone(config, device.Serial, device.IsTcp, title, window, keyboardMode);
        _host.Log.Info($"Showing {device.Serial} beside the main phone: {string.Join(' ', args)}");

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
                _host.Log.Error($"Could not start the session of '{device.Serial}'", ex);
                return new ScreenLaunch(null, "scrcpy could not start: " + ex.Message);
            }

            appeared = await scrcpy.WaitForWindowAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(true);
        }

        if (!appeared)
        {
            var reason = ScrcpyFailureText(scrcpy);
            _host.Log.Warn($"{device.Serial} did not open a window beside the main phone: " + string.Join(" | ", scrcpy.RecentStderr.TakeLast(5)));
            scrcpy.Dispose();
            return new ScreenLaunch(null, reason);
        }

        return new ScreenLaunch(scrcpy, string.Empty);
    }
}
