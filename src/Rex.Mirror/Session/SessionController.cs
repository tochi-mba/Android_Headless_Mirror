using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services;

namespace Rex.Mirror.Session;

public enum SessionPhase
{
    /// <summary>scrcpy/adb are not installed yet.</summary>
    NeedsSetup,

    /// <summary>Watching ADB for a ready phone.</summary>
    Waiting,

    /// <summary>scrcpy is starting for a phone.</summary>
    Starting,

    /// <summary>The phone is on screen.</summary>
    Mirroring,

    /// <summary>The user stopped the mirror; it stays closed until the phone reconnects or Start is pressed.</summary>
    Stopped,
}

/// <summary>
/// The supervisor: polls ADB, picks a phone, launches scrcpy, hands its window to the host,
/// and restarts or waits when it ends. State is only mutated on the UI thread, so the window
/// can bind to it directly.
/// </summary>
public sealed class SessionController : IDisposable
{
    private const int MaxConsecutiveRestarts = 4;

    private readonly AppHost _host;
    private readonly OwnedProcessJob _ownedProcesses = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Dispatcher? _dispatcher;
    private Task? _loop;
    private string _lastSnapshot = string.Empty;
    private string _stoppedSerial = string.Empty;
    private int _restartAttempts;
    private DateTime _mirrorStartedAt;
    private bool _restartRequested;
    private DateTime _retryAfter = DateTime.MinValue;
    private DateTime _nextWirelessAttempt = DateTime.MinValue;
    private readonly HashSet<string> _wirelessBootstrapped = new(StringComparer.Ordinal);
    private CancellationTokenSource? _batteryPoll;
    private ScrcpyProcess? _pending;
    private bool _disposed;

    public SessionController(AppHost host) => _host = host;

    public SessionPhase Phase { get; private set; } = SessionPhase.Waiting;
    public string Message { get; private set; } = "Starting…";
    public IReadOnlyList<AdbDevice> Devices { get; private set; } = [];
    public AdbDevice? ActiveDevice { get; private set; }
    public DeviceIdentity? Identity { get; private set; }
    public BatteryStatus? Battery { get; private set; }
    public ToolPaths? Tools { get; private set; }
    public AdbClient? Adb { get; private set; }
    public ScrcpyProcess? Scrcpy { get; private set; }
    public string? PendingLockQuestionSerial { get; private set; }
    public bool IsMirroring => Phase == SessionPhase.Mirroring && Scrcpy is { HasExited: false };

    /// <summary>Set by the window: the screen rectangle (pixels) where scrcpy should first appear.</summary>
    public Func<(int X, int Y, int Width, int Height)?>? LaunchRect { get; set; }

    public event Action? Changed;
    public event Action<ScrcpyProcess>? MirrorReady;
    public event Action? MirrorEnded;

    public void Start()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Tools = ToolLocator.Find(_host.Paths);
        if (Tools is null)
        {
            SetState(SessionPhase.NeedsSetup, "scrcpy is not installed yet.");
            return;
        }

        Adb = new AdbClient(Tools.Adb, _host.Runner);
        SetState(SessionPhase.Waiting, "Looking for a phone…");
        _loop = Task.Run(() => RunLoopAsync(_shutdown.Token));
    }

    public async Task InstallToolsAsync(IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        var installer = new ScrcpyInstaller();
        Tools = await installer.InstallLatestAsync(_host.Paths, progress, cancellationToken).ConfigureAwait(true);
        _host.Log.Info($"Installed scrcpy {Tools.Version} at {Tools.Scrcpy}");
        Adb = new AdbClient(Tools.Adb, _host.Runner);
        SetState(SessionPhase.Waiting, "Looking for a phone…");
        _loop ??= Task.Run(() => RunLoopAsync(_shutdown.Token));
    }

    // ----- Watch loop -----

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Adb!.StartServerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _host.Log.Warn("adb start-server failed: " + ex.Message);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _host.Log.Error("Watch loop error", ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_host.Config.Session.PollSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var devices = await Adb!.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = string.Join("|", devices.Select(d => d.Serial + ":" + d.State));
        var changed = snapshot != _lastSnapshot;
        _lastSnapshot = snapshot;

        await OnUi(() =>
        {
            Devices = devices;
            if (changed)
            {
                Changed?.Invoke();
            }
        }).ConfigureAwait(false);

        var phase = Phase;
        if (phase is SessionPhase.Mirroring or SessionPhase.Starting || DateTime.UtcNow < _retryAfter)
        {
            return;
        }

        if (phase == SessionPhase.Stopped && devices.Any(d => d.Serial == _stoppedSerial && d.IsReady))
        {
            return;
        }

        if (phase == SessionPhase.Stopped)
        {
            _stoppedSerial = string.Empty;
            _restartAttempts = 0;
        }

        var config = _host.Config;
        var selected = DeviceSelection.Select(devices, DeviceSelection.EffectivePreferredSerial(config, _host.State), config.Session.PreferUsb);
        if (selected is null)
        {
            await OnUi(() => SetState(SessionPhase.Waiting, DeviceStateText.Describe(devices))).ConfigureAwait(false);
            await TryWirelessAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await StartMirrorAsync(selected, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartMirrorAsync(AdbDevice device, CancellationToken cancellationToken)
    {
        await OnUi(() =>
        {
            ActiveDevice = device;
            SetState(SessionPhase.Starting, $"Connecting to {device.Serial}…");
        }).ConfigureAwait(false);

        DeviceIdentity identity;
        try
        {
            identity = await Adb!.GetIdentityAsync(device.Serial, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            identity = new DeviceIdentity(device.Serial, string.Empty, device.Model, string.Empty, string.Empty, string.Empty, 0, 0);
            _host.Log.Warn("Could not read the phone identity: " + ex.Message);
        }

        _host.State.RememberDevice(device.Serial, identity.DisplayName, identity.Model);
        var profile = _host.State.GetDevice(device.Serial);
        var askLock = _host.Config.PatternGuide.Enabled && _host.Config.PatternGuide.AskPerDevice &&
                      string.IsNullOrEmpty(profile?.LockScreenMode);

        var config = _host.Config;
        if (!device.IsTcp && config.Wireless.Enabled && config.Wireless.EnableTcpipWhenUsbAvailable && _wirelessBootstrapped.Add(device.Serial))
        {
            await BootstrapWirelessAsync(device.Serial, cancellationToken).ConfigureAwait(false);
        }

        var adb = Adb!;
        if (config.Session.WakeBeforeMirror)
        {
            await adb.WakeAsync(device.Serial, cancellationToken).ConfigureAwait(false);
        }

        if (config.Session.DismissKeyguard)
        {
            await adb.DismissKeyguardAsync(device.Serial, cancellationToken).ConfigureAwait(false);
        }

        string? recordPath = null;
        if (config.Mirror.RecordOnStart)
        {
            var directory = _host.Paths.Inside(config.Mirror.RecordDirectory);
            Directory.CreateDirectory(directory);
            recordPath = Path.Combine(directory, "android-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".mp4");
        }

        var launchRect = await OnUi(() => LaunchRect?.Invoke()).ConfigureAwait(false);
        var title = $"Android Headless Mirror [{device.Serial}]";

        var args = ScrcpyArguments.Build(config, device.Serial, device.IsTcp, title, launchRect, recordPath);
        _host.Log.Info($"Launching scrcpy for {device.Serial} ({device.Transport}): {string.Join(' ', args)}");

        ScrcpyProcess scrcpy;
        try
        {
            scrcpy = ScrcpyProcess.Launch(Tools!.Scrcpy, args, device.Serial, title, _ownedProcesses);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _host.Log.Error("Could not start scrcpy", ex);
            await OnUi(() => SetState(SessionPhase.Waiting, "Could not start scrcpy: " + ex.Message)).ConfigureAwait(false);
            _retryAfter = DateTime.UtcNow.AddSeconds(config.Session.RetrySeconds);
            return;
        }

        _pending = scrcpy;
        var appeared = await scrcpy.WaitForWindowAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);
        _pending = null;
        if (!appeared)
        {
            var reason = ScrcpyFailureText(scrcpy);
            _host.Log.Warn("scrcpy did not open a window: " + reason);
            scrcpy.Dispose();
            await OnUi(() => SetState(SessionPhase.Waiting, reason)).ConfigureAwait(false);
            _retryAfter = DateTime.UtcNow.AddSeconds(config.Session.RetrySeconds);
            return;
        }

        await OnUi(() =>
        {
            Scrcpy = scrcpy;
            Identity = identity;
            PendingLockQuestionSerial = askLock ? device.Serial : null;
            _mirrorStartedAt = DateTime.UtcNow;
            SetState(SessionPhase.Mirroring, identity.DisplayName);
            MirrorReady?.Invoke(scrcpy);
            StartBatteryPoll(device.Serial);
        }).ConfigureAwait(false);

        _ = scrcpy.Exited.ContinueWith(t => OnUi(() => OnScrcpyExited(scrcpy, t.Result)), TaskScheduler.Default);
    }

    private void OnScrcpyExited(ScrcpyProcess scrcpy, int exitCode)
    {
        if (!ReferenceEquals(scrcpy, Scrcpy))
        {
            scrcpy.Dispose();
            return;
        }

        _host.Log.Info($"scrcpy exited with code {exitCode}");
        var userStopped = _stoppedSerial == scrcpy.Serial;
        StopBatteryPoll();
        Scrcpy = null;
        Battery = null;
        MirrorEnded?.Invoke();

        var config = _host.Config;
        // A briefly visible window does not mean a crash loop recovered.
        if (DateTime.UtcNow - _mirrorStartedAt >= TimeSpan.FromMinutes(1))
        {
            _restartAttempts = 0;
        }

        if (userStopped)
        {
            SetState(SessionPhase.Stopped, "Mirror stopped. Press Start, or reconnect the phone.");
        }
        else if (_restartRequested)
        {
            _retryAfter = DateTime.MinValue;
            SetState(SessionPhase.Waiting, "Restarting mirror…");
        }
        else if (config.Session.RestartOnUnexpectedExit && _restartAttempts < MaxConsecutiveRestarts &&
                 Devices.Any(d => d.Serial == scrcpy.Serial && d.IsReady))
        {
            _restartAttempts++;
            _retryAfter = DateTime.UtcNow.AddSeconds(config.Session.RetrySeconds);
            SetState(SessionPhase.Waiting, $"Mirror ended unexpectedly ({ScrcpyFailureText(scrcpy)}). Restarting…");
        }
        else
        {
            _stoppedSerial = scrcpy.Serial;
            SetState(SessionPhase.Stopped, _restartAttempts >= MaxConsecutiveRestarts
                ? "The mirror keeps closing. Press Start to try again, or check Info for details."
                : "Mirror closed. Press Start, or reconnect the phone.");
        }

        _restartRequested = false;
        scrcpy.Dispose();
        ActiveDevice = null;
        Identity = null;
        Changed?.Invoke();
    }

    private static string ScrcpyFailureText(ScrcpyProcess scrcpy)
    {
        var lines = scrcpy.RecentStderr
            .Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || l.Contains("WARN", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Replace("ERROR:", string.Empty, StringComparison.Ordinal).Trim())
            .Where(l => l.Length > 0)
            .TakeLast(2)
            .ToArray();
        return lines.Length > 0 ? string.Join(" · ", lines) : "scrcpy closed without an error message";
    }

    // ----- Public commands (UI thread) -----

    public void StopMirror()
    {
        var scrcpy = Scrcpy;
        if (scrcpy is null)
        {
            return;
        }

        _stoppedSerial = scrcpy.Serial;
        _restartRequested = false;
        _host.Log.Info("Mirror stopped by the user.");
        scrcpy.Kill();
    }

    public void StartAgain()
    {
        _stoppedSerial = string.Empty;
        _retryAfter = DateTime.MinValue;
        _restartAttempts = 0;
        if (Phase == SessionPhase.Stopped)
        {
            SetState(SessionPhase.Waiting, "Looking for a phone…");
        }
    }

    public void RestartMirror()
    {
        var scrcpy = Scrcpy;
        if (scrcpy is null)
        {
            StartAgain();
            return;
        }

        _stoppedSerial = string.Empty;
        _restartAttempts = 0;
        _restartRequested = true;
        _retryAfter = DateTime.MinValue;
        _host.Log.Info("Mirror restart requested (settings changed).");
        scrcpy.Kill();
    }

    public void AnswerLockQuestion(string mode)
    {
        var serial = PendingLockQuestionSerial;
        if (serial is null)
        {
            return;
        }

        _host.State.SetLockScreenMode(serial, mode);
        PendingLockQuestionSerial = null;
        Changed?.Invoke();
    }

    public async Task<AndroidResult> RunActionAsync(string id, Func<string, AndroidResult>? appActions = null)
    {
        var action = MirrorActions.Find(id);
        if (action is null)
        {
            return AndroidResult.Failure($"Unknown action '{id}'.");
        }

        switch (action.Kind)
        {
            case ActionKind.Adb:
            {
                var device = ActiveDevice ?? Devices.FirstOrDefault(d => d.IsReady);
                if (device is null || Adb is null)
                {
                    return AndroidResult.Failure("No phone is connected.");
                }

                var command = MirrorActions.AdbCommand(action.Id)!.Value;
                return command.Kind == "key"
                    ? await Adb.KeyEventAsync(device.Serial, command.Argument).ConfigureAwait(true)
                    : command.Kind == "rotation"
                    ? await Adb.SetRotationOverrideAsync(device.Serial, command.Argument).ConfigureAwait(true)
                    : await Adb.StatusBarAsync(device.Serial, command.Argument).ConfigureAwait(true);
            }

            case ActionKind.Scrcpy:
            {
                var scrcpy = Scrcpy;
                if (scrcpy is null || scrcpy.HasExited)
                {
                    return AndroidResult.Failure("The mirror is not running.");
                }

                var shortcut = ScrcpyShortcuts.For(action.Id);
                return shortcut is not null && ScrcpyShortcutSender.Send(scrcpy.Hwnd, shortcut)
                    ? AndroidResult.Success(action.Label)
                    : AndroidResult.Failure($"Could not send '{action.Label}' to the mirror.");
            }

            default:
                return appActions?.Invoke(action.Id) ?? AndroidResult.Failure($"'{action.Label}' is only available in the app.");
        }
    }

    public async Task<(bool Ok, string Text)> SaveScreenshotAsync()
    {
        var device = ActiveDevice ?? Devices.FirstOrDefault(d => d.IsReady);
        if (device is null || Adb is null)
        {
            return (false, "No phone is connected.");
        }

        var bytes = await Adb.ScreencapAsync(device.Serial).ConfigureAwait(true);
        if (bytes is null)
        {
            return (false, "The phone did not return a screenshot.");
        }

        var directory = _host.Paths.Inside(_host.Config.App.ScreenshotDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "android-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png");
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
        return (true, path);
    }

    // ----- Helpers -----

    private void StartBatteryPoll(string serial)
    {
        StopBatteryPoll();
        var cts = new CancellationTokenSource();
        _batteryPoll = cts;
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var battery = await Adb!.GetBatteryAsync(serial, cts.Token).ConfigureAwait(false);
                    await OnUi(() =>
                    {
                        Battery = battery;
                        Changed?.Invoke();
                    }).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cts.Token).ConfigureAwait(false);
                }
            }
        }, cts.Token);
    }

    private void StopBatteryPoll()
    {
        _batteryPoll?.Cancel();
        _batteryPoll?.Dispose();
        _batteryPoll = null;
    }

    private async Task BootstrapWirelessAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            var ips = await Adb!.GetPhoneIpCandidatesAsync(serial, cancellationToken).ConfigureAwait(false);
            if (ips.Count > 0)
            {
                _host.State.AddWirelessHosts(ips);
            }

            var result = await Adb.TcpipAsync(serial, _host.Config.Wireless.Port, cancellationToken).ConfigureAwait(false);
            _host.Log.Info($"adb tcpip: {result.StdOut.Trim()}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _host.Log.Warn("Wireless bootstrap failed: " + ex.Message);
        }
    }

    private async Task TryWirelessAsync(CancellationToken cancellationToken)
    {
        var config = _host.Config;
        if (!config.Wireless.Enabled || DateTime.UtcNow < _nextWirelessAttempt)
        {
            return;
        }

        _nextWirelessAttempt = DateTime.UtcNow.AddSeconds(10);
        var hosts = _host.State.WirelessHosts.Concat(config.Wireless.ManualHosts)
            .Where(AdbParsing.IsPrivateIPv4)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var address in hosts)
        {
            var result = await Adb!.ConnectAsync($"{address}:{config.Wireless.Port}", cancellationToken).ConfigureAwait(false);
            if (result.StdOut.Contains("connected to", StringComparison.OrdinalIgnoreCase))
            {
                _host.Log.Info($"Wireless ADB connected to {address}:{config.Wireless.Port}");
            }
        }
    }

    private void SetState(SessionPhase phase, string message)
    {
        Phase = phase;
        Message = message;
        Changed?.Invoke();
    }

    private Task OnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    private Task<T> OnUi<T>(Func<T> func)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return _dispatcher.InvokeAsync(func).Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        StopBatteryPoll();
        _pending?.Dispose();
        Scrcpy?.Dispose();
        Scrcpy = null;
        _ownedProcesses.Dispose();
        _shutdown.Dispose();
    }
}
