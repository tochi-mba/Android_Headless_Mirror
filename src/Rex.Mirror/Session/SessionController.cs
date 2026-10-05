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
public sealed partial class SessionController : IDisposable
{
    /// <summary>Polls the stopped phone must be missing from ADB before the app accepts it as unplugged.</summary>
    private const int StoppedPhoneMissedPolls = 3;

    private readonly AppHost _host;
    private readonly OwnedProcessJob _ownedProcesses = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Dispatcher? _dispatcher;
    private Task? _loop;
    private string _lastSnapshot = string.Empty;
    private string _stoppedSerial = string.Empty;
    private int _restartAttempts;
    private DateTime _mirrorStartedAt;

    /// <summary>The time limit, in minutes, the running mirror was started with; 0 for none.</summary>
    private int _mirrorTimeLimit;
    private bool _restartRequested;
    private DateTime _retryAfter = DateTime.MinValue;
    private DateTime _nextWirelessAttempt = DateTime.MinValue;
    private DateTime _nextUsbScan = DateTime.MinValue;
    private int _stoppedSerialMissedPolls;
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

    /// <summary>ADB interfaces Windows reports as attached that adb cannot see (see <see cref="AdbInterface.Unreachable"/>).</summary>
    public IReadOnlyList<AdbInterface> UnreachableAdbInterfaces { get; private set; } = [];
    public bool IsMirroring => Phase == SessionPhase.Mirroring && Scrcpy is { HasExited: false };
    private IReadOnlyList<string>? _activeLaunchSettings;
    public bool NeedsRestart => IsMirroring && _activeLaunchSettings is not null &&
        !_activeLaunchSettings.SequenceEqual(ScrcpyArguments.LaunchSettings(_host.Config, ActiveDevice?.IsTcp == true));

    /// <summary>Set by the window: the screen rectangle (pixels) where scrcpy should first appear.</summary>
    public Func<(int X, int Y, int Width, int Height)?>? LaunchRect { get; set; }

    /// <summary>Set by the window: the scrcpy windows of the copies of the phone, which follow the view shortcuts too.</summary>
    public Func<IEnumerable<IntPtr>>? CopyWindows { get; set; }

    /// <summary>How the main session shows the picture now (<see cref="DisplayOrientation"/>): its start, then every turn and flip since.</summary>
    public int ViewOrientation { get; private set; }

    /// <summary>Whether the main session's picture is paused.</summary>
    public bool ViewPaused { get; private set; }

    /// <summary>Whether scrcpy's frame rate counter is running in the main session.</summary>
    public bool FrameRateCounterOn { get; private set; }

    public event Action? Changed;
    public event Action<ScrcpyProcess>? MirrorReady;
    public event Action? MirrorEnded;

    /// <summary>Raised on the UI thread when the mirror ended without being stopped or restarted on purpose, with what the status says.</summary>
    public event Action<string>? EndedByItself;

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
        if (changed)
        {
            _host.Log.Info("ADB devices: " + (devices.Count == 0 ? "none" : string.Join(", ", devices.Select(d => $"{d.Serial} {d.State} ({d.Transport})"))));
        }

        _lastSnapshot = snapshot;

        var unreachable = await ScanUsbAsync(devices).ConfigureAwait(false);
        await OnUi(() =>
        {
            Devices = devices;
            if (unreachable is not null)
            {
                UnreachableAdbInterfaces = unreachable;
                changed = true;
            }

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

        if (phase == SessionPhase.Stopped)
        {
            if (devices.Any(d => d.Serial == _stoppedSerial && d.IsReady))
            {
                _stoppedSerialMissedPolls = 0;
                return;
            }

            // A stop stays a stop until the phone really leaves. ADB drops a device from one poll
            // often enough (a busy server, a USB hiccup) that a single miss must not restart it.
            if (++_stoppedSerialMissedPolls < StoppedPhoneMissedPolls)
            {
                return;
            }

            _stoppedSerialMissedPolls = 0;
            _stoppedSerial = string.Empty;
            _restartAttempts = 0;
        }

        var config = _host.Config;
        // Never the phone shown beside: two sessions on one phone would undo each other.
        var choosable = ShownBeside is { } beside ? devices.Where(d => !beside(d)).ToArray() : devices;
        var selected = DeviceSelection.Select(choosable, DeviceSelection.EffectivePreferredSerial(config, _host.State), config.Session.PreferUsb);
        if (selected is null)
        {
            await OnUi(() => SetState(SessionPhase.Waiting, DeviceStateText.Describe(devices, UnreachableAdbInterfaces.Count > 0))).ConfigureAwait(false);
            await TryWirelessAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await StartMirrorAsync(selected, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// While adb sees nothing, checks every few seconds whether Windows has an attached ADB
    /// interface that adb cannot enumerate. Returns the new list only when it changed.
    /// </summary>
    private Task<IReadOnlyList<AdbInterface>?> ScanUsbAsync(IReadOnlyList<AdbDevice> devices)
    {
        if (devices.Count > 0)
        {
            return Task.FromResult<IReadOnlyList<AdbInterface>?>(UnreachableAdbInterfaces.Count > 0 ? [] : null);
        }

        if (DateTime.UtcNow < _nextUsbScan)
        {
            return Task.FromResult<IReadOnlyList<AdbInterface>?>(null);
        }

        _nextUsbScan = DateTime.UtcNow.AddSeconds(5);
        return Task.Run(() =>
        {
            var unreachable = UsbAdbInterfaces.Unreachable();
            var same = unreachable.Select(u => u.InstanceId).SequenceEqual(UnreachableAdbInterfaces.Select(u => u.InstanceId));
            return same ? null : unreachable;
        });
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
            _host.Log.Warn($"Could not read the identity of phone '{device.Serial}'", ex);
        }

        // Some phones (and emulators) report no model through getprop, and the name would then be
        // the serial number. ADB's own device list usually still has the model, which reads better
        // everywhere the phone is named: the chip, the title bar, the tray and the questions asked.
        if (identity.DisplayName == device.Serial && device.Model.Length > 0)
        {
            identity = identity with { Model = device.Model };
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
            recordPath = Path.Combine(directory, ScrcpyArguments.RecordingFileName(config.Mirror.RecordFormat, DateTime.Now, config.App.CaptureNames,
                identity.DisplayName, identity.Model, name => File.Exists(Path.Combine(directory, name))));
        }

        var launchRect = await OnUi(() => LaunchRect?.Invoke()).ConfigureAwait(false);
        var title = $"Android Headless Mirror [{device.Serial}]";

    retryScrcpy:
        var keyboardMode = ScrcpyArguments.KeyboardModeFor(config, _host.State.GetDevice(device.Serial));
        var args = ScrcpyArguments.Build(config, device.Serial, device.IsTcp, title, launchRect, recordPath, keyboardMode);
        var launchSettings = ScrcpyArguments.LaunchSettings(config, device.IsTcp);
        _host.Log.Info($"Launching scrcpy for {device.Serial} ({device.Transport}): {string.Join(' ', args)}");

        ScrcpyProcess scrcpy;
        bool appeared;
        using (await ServerStart.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                scrcpy = ScrcpyProcess.Launch(Tools!.Scrcpy, args, device.Serial, title, keyboardMode, _ownedProcesses);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _host.Log.Error("Could not start scrcpy", ex);
                await OnUi(() => SetState(SessionPhase.Waiting, "Could not start scrcpy: " + ex.Message)).ConfigureAwait(false);
                _retryAfter = DateTime.UtcNow.AddSeconds(config.Session.RetrySeconds);
                return;
            }

            _pending = scrcpy;
            appeared = await scrcpy.WaitForWindowAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);
            _pending = null;
        }

        if (!appeared)
        {
            var reason = ScrcpyFailureText(scrcpy);
            _host.Log.Warn("scrcpy did not open a window: " + reason);
            if (TryUseCompatibilityKeyboard(scrcpy))
            {
                scrcpy.Dispose();
                await OnUi(() => SetState(SessionPhase.Starting, "This phone needs compatibility keyboard mode. Reconnecting…")).ConfigureAwait(false);
                goto retryScrcpy;
            }

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
            _activeLaunchSettings = launchSettings;
            _mirrorStartedAt = DateTime.UtcNow;
            _mirrorTimeLimit = config.Mirror.TimeLimitMinutes;
            // A new session shows the picture as its arguments say, and playing.
            ViewOrientation = DisplayOrientation.Initial(config.Mirror);
            ViewPaused = false;
            FrameRateCounterOn = config.App.ShowFrameRate;
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

        _host.Log.Info($"scrcpy exited with code {exitCode}: {ScrcpyFailureText(scrcpy)}");
        var userStopped = _stoppedSerial == scrcpy.Serial;
        StopBatteryPoll();
        Scrcpy = null;
        Battery = null;
        if (!_restartRequested)
        {
            CloseAppsOpenedHere();
        }

        MirrorEnded?.Invoke();

        if (TryUseCompatibilityKeyboard(scrcpy))
        {
            _retryAfter = DateTime.MinValue;
            _restartAttempts = 0;
            SetState(SessionPhase.Waiting, "This phone needs compatibility keyboard mode. Reconnecting…");
            scrcpy.Dispose();
            ActiveDevice = null;
            Identity = null;
            Changed?.Invoke();
            return;
        }

        var config = _host.Config;
        var ranFor = DateTime.UtcNow - _mirrorStartedAt;
        // A briefly visible window does not mean a crash loop recovered.
        if (ranFor >= TimeSpan.FromMinutes(1))
        {
            _restartAttempts = 0;
        }

        var phoneStillReady = Devices.Any(d => d.Serial == scrcpy.Serial && d.IsReady);
        if (userStopped)
        {
            SetState(SessionPhase.Stopped, "Mirror stopped. Press Start, or reconnect the phone.");
        }
        else if (_restartRequested)
        {
            _retryAfter = DateTime.MinValue;
            SetState(SessionPhase.Waiting, "Restarting mirror…");
        }
        else if (MirrorTimeLimit.Reached(_mirrorTimeLimit, ranFor))
        {
            // Its time was up, as the person asked: a stop, not a crash to start again from.
            _stoppedSerial = scrcpy.Serial;
            SetState(SessionPhase.Stopped, MirrorTimeLimit.Stopped(_mirrorTimeLimit));
        }
        else if (!phoneStillReady)
        {
            // The USB link dropped (phones re-enumerate when they change USB mode, e.g. on unlock).
            // That is not a stop: pick the phone up again as soon as ADB sees it.
            _stoppedSerial = string.Empty;
            _retryAfter = DateTime.UtcNow.AddSeconds(1);
            _host.Log.Info($"{scrcpy.Serial} is no longer ready in ADB; waiting for it to come back.");
            SetState(SessionPhase.Waiting, "Phone disconnected. Waiting for it to come back…");
        }
        else if (config.Session.RestartOnUnexpectedExit && _restartAttempts < config.Session.RestartLimit)
        {
            _restartAttempts++;
            _retryAfter = DateTime.UtcNow.AddSeconds(config.Session.RetrySeconds);
            SetState(SessionPhase.Waiting, $"Mirror ended unexpectedly ({ScrcpyFailureText(scrcpy)}). Restarting…");
        }
        else
        {
            _stoppedSerial = scrcpy.Serial;
            SetState(SessionPhase.Stopped, config.Session.RestartOnUnexpectedExit && _restartAttempts >= config.Session.RestartLimit
                ? "The mirror keeps closing. Press Start to try again, or check Info for details."
                : "Mirror closed. Press Start, or reconnect the phone.");
        }

        var byItself = !userStopped && !_restartRequested && phoneStillReady;
        _restartRequested = false;
        scrcpy.Dispose();
        ActiveDevice = null;
        Identity = null;
        Changed?.Invoke();
        if (byItself)
        {
            EndedByItself?.Invoke(Message);
        }
    }

    private bool TryUseCompatibilityKeyboard(ScrcpyProcess scrcpy)
    {
        if (scrcpy.KeyboardMode != ScrcpyArguments.FullKeyboardMode ||
            !ScrcpyArguments.IsUhidPermissionFailure(scrcpy.RecentStderr))
        {
            return false;
        }

        // Remembered per phone: the refusal never changes, and each retry costs a failed launch.
        _host.State.SetCompatibilityKeyboard(scrcpy.Serial, true);
        _host.Log.Warn($"UHID keyboard is unavailable on {scrcpy.Serial}; retrying with SDK raw-key compatibility mode, and starting there from now on.");
        return true;
    }

    /// <summary>
    /// Plays a keyboard gesture through Android's own input tool, for when there is no picture to
    /// touch: the window is in the tray, or Windows refused touch injection. The paths are the
    /// same ones a finger would take over the mirror, scaled to the phone's screen.
    /// </summary>
    public async Task<AndroidResult> PlayGestureOverAdbAsync(string action, bool landscape, string? serial = null)
    {
        var device = serial is not null ? Devices.FirstOrDefault(d => d.Serial == serial && d.IsReady) : ActiveDevice ?? Devices.FirstOrDefault(d => d.IsReady);
        if (device is null || Adb is null)
        {
            return AndroidResult.Failure("No phone is connected.");
        }

        var (width, height) = serial is null && Identity is { DisplayWidth: > 0, DisplayHeight: > 0 } identity
            ? (identity.DisplayWidth, identity.DisplayHeight)
            : await Adb.GetDisplaySizeAsync(device.Serial).ConfigureAwait(true);
        if (width <= 0 || height <= 0)
        {
            return AndroidResult.Failure("The phone did not report its screen size.");
        }

        // wm size is the natural orientation; Android's input tool wants the current one.
        if (landscape != width > height)
        {
            (width, height) = (height, width);
        }

        var input = _host.Config.Input;
        var strokes = KeyboardTouch.Strokes(action, new RECT { Left = 0, Top = 0, Right = width, Bottom = height }, input.SwipeLength);
        if (strokes.Count == 0)
        {
            return AndroidResult.Failure($"'{action}' is not a gesture.");
        }

        foreach (var stroke in strokes)
        {
            var first = stroke[0];
            var last = stroke[^1];
            var result = stroke.Count == 1
                ? await Adb.TapAsync(device.Serial, first.X, first.Y).ConfigureAwait(true)
                : await Adb.SwipeAsync(device.Serial, first.X, first.Y, last.X, last.Y, input.SwipeMilliseconds).ConfigureAwait(true);
            if (!result.Ok)
            {
                return result;
            }
        }

        return AndroidResult.Success(KeyboardTouch.Outcome(action));
    }

    private static string ScrcpyFailureText(ScrcpyProcess scrcpy)
    {
        if (ScrcpyArguments.MissingEncoder(scrcpy.RecentStderr) is { } encoder)
        {
            return encoder;
        }

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
        _stoppedSerialMissedPolls = 0;
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

    public async Task<AndroidResult> RunActionAsync(string id, Func<string, Task<AndroidResult>>? appActions = null)
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

                return await MirrorActions.RunAdbAsync(Adb, device.Serial, action.Id).ConfigureAwait(true);
            }

            case ActionKind.Scrcpy:
            {
                var scrcpy = Scrcpy;
                if (scrcpy is null || scrcpy.HasExited)
                {
                    return AndroidResult.Failure("The mirror is not running.");
                }

                var shortcut = ScrcpyShortcuts.For(action.Id);
                if (shortcut is null || !ScrcpyShortcutSender.Send(scrcpy.Hwnd, shortcut))
                {
                    return AndroidResult.Failure($"Could not send '{action.Label}' to the mirror.");
                }

                if (action.Id == "fps")
                {
                    FrameRateCounterOn = !FrameRateCounterOn;
                }

                if (ScrcpyShortcuts.AppliesToEveryView(action.Id))
                {
                    FollowView(action.Id);
                    foreach (var copy in CopyWindows?.Invoke() ?? [])
                    {
                        ScrcpyShortcutSender.Send(copy, shortcut);
                    }
                }

                return AndroidResult.Success(action.Label);
            }

            default:
                return appActions is null
                    ? AndroidResult.Failure($"'{action.Label}' is only available in the app.")
                    : await appActions(action.Id).ConfigureAwait(true);
        }
    }

    /// <summary>Keeps <see cref="ViewOrientation"/> and <see cref="ViewPaused"/> in step with a view shortcut just sent.</summary>
    private void FollowView(string actionId)
    {
        if (DisplayOrientation.TransformFor(actionId) is { } transform)
        {
            ViewOrientation = DisplayOrientation.Apply(ViewOrientation, transform);
        }
        else if (actionId is "pause" or "resume")
        {
            ViewPaused = actionId == "pause";
        }
    }

    /// <summary>
    /// Starts or stops scrcpy's frame rate counter in the running session to match the setting,
    /// so turning the readout on or off needs no restart.
    /// </summary>
    public void ApplyFrameRateSetting()
    {
        var wanted = _host.Config.App.ShowFrameRate;
        if (!IsMirroring || Scrcpy is not { } scrcpy || FrameRateCounterOn == wanted || ScrcpyShortcuts.For("fps") is not { } toggle)
        {
            return;
        }

        if (ScrcpyShortcutSender.Send(scrcpy.Hwnd, toggle))
        {
            FrameRateCounterOn = wanted;
        }
    }

    /// <summary>Saves a picture of a phone's screen: the given one, or the mirrored one.</summary>
    public async Task<(bool Ok, string Text)> SaveScreenshotAsync(string? serial = null)
    {
        var device = serial is not null ? Devices.FirstOrDefault(d => d.Serial == serial && d.IsReady) : ActiveDevice ?? Devices.FirstOrDefault(d => d.IsReady);
        if (device is null || Adb is null)
        {
            return (false, "No phone is connected.");
        }

        var bytes = await Adb.ScreencapAsync(device.Serial).ConfigureAwait(true);
        if (bytes is null)
        {
            return (false, "The phone did not return a screenshot.");
        }

        var directory = _host.Paths.ScreenshotFolder(_host.Config.App.ScreenshotDirectory);
        Directory.CreateDirectory(directory);
        var format = _host.Config.App.ScreenshotFormat;
        var phone = device.Serial == ActiveDevice?.Serial && Identity is { } shown ? shown : null;
        var path = Path.Combine(directory, CaptureName.Unique(_host.Config.App.CaptureNames, DateTime.Now,
            phone?.DisplayName ?? device.Model.Replace('_', ' '), phone?.Model ?? device.Model, ScreenshotFile.Extension(format),
            name => File.Exists(Path.Combine(directory, name))));
        try
        {
            await File.WriteAllBytesAsync(path, ScreenshotFile.Encode(bytes, format)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            return (false, "The phone's screenshot could not be read: " + ex.Message);
        }

        if (_host.Config.App.CopyScreenshots)
        {
            try
            {
                System.Windows.Clipboard.SetImage(ScreenshotFile.Decode(bytes));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or NotSupportedException or FileFormatException)
            {
                // Another app holds the clipboard; the file is saved all the same.
                _host.Log.Warn("Could not copy the screenshot to the clipboard", ex);
            }
        }

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
                    _host.Log.Warn($"Could not refresh the wireless connection for '{serial}'", ex);
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
            _host.Log.Warn($"Wireless bootstrap failed for '{serial}'", ex);
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
