using Rex.Core;

namespace Rex.Mirror.Session;

/// <summary>
/// The phone's apps: reading the list with scrcpy, opening one, and closing again the ones opened
/// from here when the mirror stops (when that is asked for). Every server start goes through
/// <see cref="ServerStart"/>, so reading the list never races the mirror or a copy starting.
/// </summary>
public sealed partial class SessionController
{
    private readonly List<(string Serial, string Package)> _openedHere = [];
    private Task<AppsRead>? _appsRead;
    private string _appsReadSerial = string.Empty;

    /// <summary>One scrcpy server starting at a time: the mirror, its copies and the app list all take it.</summary>
    public ServerStartGate ServerStart { get; } = new();

    /// <summary>True while the phone's app list is being read.</summary>
    public bool ReadingApps => _appsRead is { IsCompleted: false };

    /// <summary>Why the last read of a phone's apps failed, or empty.</summary>
    public string AppsError { get; private set; } = string.Empty;

    /// <summary>Raised on the UI thread when a read starts or ends, and when an app is opened.</summary>
    public event Action? AppsChanged;

    /// <summary>
    /// Reads a phone's apps and remembers them for that phone. Asking again while a read of the same
    /// phone runs joins that read instead of starting a second scrcpy.
    /// </summary>
    public Task<AppsRead> ReadAppsAsync(string serial)
    {
        if (_appsRead is { IsCompleted: false } running && _appsReadSerial == serial)
        {
            return running;
        }

        _appsReadSerial = serial;
        _appsRead = ReadAppsNowAsync(serial);
        return _appsRead;
    }

    private async Task<AppsRead> ReadAppsNowAsync(string serial)
    {
        // ReadAppsAsync stores this Task in _appsRead immediately after this method yields. Without
        // the yield, AppsChanged is raised before that assignment, so a visible Apps panel sees
        // ReadingApps=false and starts another read. A failed "Try again" then recurses until the
        // process exhausts its stack.
        await Task.Yield();
        AppsError = string.Empty;
        AppsChanged?.Invoke();
        AppsRead read;
        if (Tools is null)
        {
            read = AppsRead.Failed("The phone tools are not installed yet.");
        }
        else
        {
            using (await ServerStart.EnterAsync().ConfigureAwait(true))
            {
                read = await AppLister.ReadAsync(_host.Runner, Tools.Scrcpy, serial).ConfigureAwait(true);
            }
        }

        if (read.Ok)
        {
            _host.State.SetApps(serial, read.Apps, DateTimeOffset.UtcNow);
            _host.Log.Info($"Read {read.Apps.Count} apps from {serial}.");
        }
        else
        {
            AppsError = read.Error;
            _host.Log.Warn($"Could not read the apps of {serial}: {read.Error}");
        }

        AppsChanged?.Invoke();
        return read;
    }

    /// <summary>
    /// Opens an app on a phone and remembers it as opened, for the recent list and for closing it
    /// again when the mirror stops. Fresh closes it first.
    /// </summary>
    public async Task<AndroidResult> OpenAppAsync(string serial, string package, bool fresh)
    {
        if (Adb is null)
        {
            return AndroidResult.Failure("The phone tools are not installed yet.");
        }

        var result = await Adb.LaunchAppAsync(serial, package, fresh).ConfigureAwait(true);
        if (result.Ok)
        {
            _host.State.NoteAppOpened(serial, package, DateTimeOffset.UtcNow, _host.Config.Apps.RecentCount);
            if (!_openedHere.Contains((serial, package)))
            {
                _openedHere.Add((serial, package));
            }

            AppsChanged?.Invoke();
        }

        return result;
    }

    /// <summary>Closes the apps opened from here, when the mirror stops and the setting asks for it.</summary>
    private void CloseAppsOpenedHere()
    {
        var opened = _openedHere.ToArray();
        _openedHere.Clear();
        if (!_host.Config.Apps.CloseWhenMirrorStops || Adb is not { } adb)
        {
            return;
        }

        foreach (var (serial, package) in opened)
        {
            _ = adb.ForceStopAsync(serial, package);
        }
    }
}
