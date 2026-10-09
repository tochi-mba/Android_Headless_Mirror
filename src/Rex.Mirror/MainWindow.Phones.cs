using System.Windows;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services.Sound;

namespace Rex.Mirror;

/// <summary>What the phone beside the main one is doing.</summary>
internal enum OtherPhoneState
{
    Off,
    Starting,
    Showing,

    /// <summary>Stopped while the window is out of sight; it starts again when the window shows.</summary>
    Paused,
}

/// <summary>
/// A second, different phone beside the main one: noticing that one has connected and asking (or
/// not, as set), its own session (<see cref="Session.SessionController.LaunchOtherPhoneAsync"/>)
/// in a view laid out with the main phone, restarting it or giving up as
/// <see cref="SecondPhonePlan"/> says, and the phone the side panel, the keys and the buttons act
/// on: the one whose view was used last.
/// </summary>
public partial class MainWindow
{
    private const string BesideNotice = "beside";

    private OtherPhoneState _otherState;
    private AdbDevice? _other;
    private ScrcpyProcess? _otherProcess;
    private MirrorHost? _otherView;
    private DeviceIdentity? _otherIdentity;
    private BatteryStatus? _otherBattery;
    private DateTime _otherBatteryRead;
    private DateTime _otherStartedAt;
    private int _otherRestarts;
    private DispatcherTimer? _otherRetry;
    private DateTime? _hiddenSince;
    private string? _askingAbout;
    private string _otherArguments = string.Empty;
    private PhoneSound? _otherSound;

    /// <summary>The phone whose own profile was last handed to the profiles, when profiles follow the phone in use.</summary>
    private string? _profileFollows;

    /// <summary>Phones turned down with "Not now", or stopped by hand, until they next connect.</summary>
    private readonly HashSet<string> _notNow = new(StringComparer.Ordinal);

    /// <summary>Hardware serials read so far, by ADB serial; written on the UI thread, read by the main session's poll.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _hardware = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hardwareAsked = new(StringComparer.Ordinal);

    /// <summary>Phones on Wi-Fi whose serial has been asked for and answered (or failed): until then they may be the main phone under another name.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _hardwareRead = new(StringComparer.Ordinal);

    /// <summary>While the two phones swap places, the phone the main session is starting on; nothing is shown beside until it mirrors.</summary>
    private string? _swappingTo;
    private DateTime _swapGivesUpAt;

    internal OtherPhoneState OtherState => _otherState;

    internal AdbDevice? OtherDevice => _other;

    internal MirrorHost? OtherView => _otherView;

    internal int OtherProcessId => _otherProcess is { HasExited: false } process ? process.ProcessId : 0;

    internal BatteryStatus? OtherBattery => _otherBattery;

    /// <summary>True while the other phone's view has the keyboard: the side panel, keys and buttons then act on it.</summary>
    internal bool OtherActive => _otherView is { HasChild: true } view && ReferenceEquals(_activeView, view);

    /// <summary>Why the last attempt to show the other phone did not hold, or empty.</summary>
    internal string OtherWhy { get; private set; } = string.Empty;

    /// <summary>The phone the side panel and the actions act on: the other one while its view is in use, else the main one.</summary>
    internal (AdbClient Adb, string Serial)? TargetPhone =>
        _host.Session.Adb is not { } adb ? null
        : OtherActive && _other is { } other ? (adb, other.Serial)
        : _host.Session.ActiveDevice is { } main ? (adb, main.Serial)
        : _host.Session.Devices.FirstOrDefault(d => d.IsReady && !IsBeside(d)) is { } ready ? (adb, ready.Serial)
        : null;

    /// <summary>The identity of <see cref="TargetPhone"/>, when it is known.</summary>
    internal DeviceIdentity? TargetIdentity => OtherActive ? _otherIdentity : _host.Session.Identity;

    internal BatteryStatus? TargetBattery => OtherActive ? _otherBattery : _host.Session.Battery;

    private void AttachPhones()
    {
        _host.Session.ShownBeside = IsBeside;
        _host.Session.Changed += EvaluatePhones;
        _host.Session.MirrorReady += _ =>
        {
            if (_host.Session.Identity is { HardwareSerial.Length: > 0 } identity)
            {
                _hardware[identity.Serial] = identity.HardwareSerial;
            }

            EvaluatePhones();
        };
        _host.ConfigChanged += ApplySecondPhoneConfig;
        // Clicking the main phone's picture makes it the phone in use again.
        Host.ChildFocused += () =>
        {
            if (_otherView is not null)
            {
                AfterTargetChange();
            }
        };
        ApplySecondPhoneConfig();
    }

    /// <summary>Whether a phone is the one shown beside, under any of its serials. Safe from the session's poll thread.</summary>
    private bool IsBeside(AdbDevice device)
    {
        var other = _other;
        if (other is null)
        {
            return false;
        }

        if (device.Serial == other.Serial)
        {
            return true;
        }

        var mine = PhonePick.HardwareOf(other, _hardware);
        return mine.Length > 0 && string.Equals(PhonePick.HardwareOf(device, _hardware), mine, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A phone's name as the window shows it, told apart from the other phone when they share a model.</summary>
    internal string NameOf(string serial)
    {
        string Own(string s) =>
            s == _host.Session.ActiveDevice?.Serial && _host.Session.Identity is { } main ? main.DisplayName
            : s == _other?.Serial && _otherIdentity is { } other ? other.DisplayName
            : _host.State.GetDevice(s) is { Name.Length: > 0 } known ? known.Name
            : _host.Session.Devices.FirstOrDefault(d => d.Serial == s) is { Model.Length: > 0 } listed ? listed.Model.Replace('_', ' ')
            : s;

        var shown = new List<(string, string, string)>();
        foreach (var s in new[] { _host.Session.ActiveDevice?.Serial, _other?.Serial, serial }.OfType<string>().Distinct())
        {
            shown.Add((s, Own(s), _hardware.TryGetValue(s, out var hardware) ? hardware : string.Empty));
        }

        return PhoneNames.Distinct(shown)[serial];
    }

    /// <summary>
    /// Looks at the phones there are now: the one beside left or is turned off; or another ready
    /// phone is shown, asked about, or left alone, as <see cref="SecondPhonePlan.OnConnect"/> says.
    /// </summary>
    private void EvaluatePhones()
    {
        if (_quitting)
        {
            return;
        }

        var settings = _host.Config.SecondPhone;
        var devices = _host.Session.Devices;
        foreach (var gone in _notNow.Where(serial => !devices.Any(d => d.Serial == serial)).ToArray())
        {
            // Unplugged: next time it connects it is asked about again.
            _notNow.Remove(gone);
        }

        if (_other is { } other)
        {
            if (!devices.Any(d => d.Serial == other.Serial && d.IsReady))
            {
                var name = NameOf(other.Serial);
                _host.Log.Info($"{other.Serial} is no longer connected, so it is no longer shown beside.");
                StopOther(byPerson: false);
                SetStatus($"{name} disconnected");
            }
            else if (!settings.Enabled)
            {
                StopOther(byPerson: false);
            }

            return;
        }

        if (!settings.Enabled || _host.Session.ActiveDevice is not { } main || !_host.Session.IsMirroring)
        {
            DropBesideQuestion();
            return;
        }

        if (_swappingTo is { } swapping)
        {
            // A swap that never got the main session onto the other phone stops waiting after a minute.
            if (main.Serial != swapping && DateTime.UtcNow < _swapGivesUpAt)
            {
                return;
            }

            _swappingTo = null;
        }

        LearnHardware(devices);
        // A phone on Wi-Fi whose serial is not known yet may be the main phone itself: it waits until it is.
        var known = devices.Where(d => !d.IsTcp || PhonePick.HardwareOf(d, _hardware).Length > 0 || _hardwareRead.ContainsKey(d.Serial)).ToArray();
        var candidate = PhonePick.Other(known, main, _hardware, _host.State.Ui.SecondPhone, _notNow);
        if (candidate is null)
        {
            DropBesideQuestion();
            return;
        }

        switch (SecondPhonePlan.OnConnect(settings, candidate.Serial, _host.State.GetDevice(candidate.Serial)?.ShowBeside, _host.State.Ui.SecondPhone))
        {
            case OtherPhoneStep.Show:
                DropBesideQuestion();
                _ = ShowBesideAsync(candidate);
                break;
            case OtherPhoneStep.Ask:
                AskAboutBeside(candidate);
                break;
            default:
                DropBesideQuestion();
                break;
        }
    }

    /// <summary>Reads, once each, the hardware serial of phones on Wi-Fi, which their ADB serial may not say.</summary>
    private void LearnHardware(IReadOnlyList<AdbDevice> devices)
    {
        if (_host.Session.Adb is not { } adb)
        {
            return;
        }

        foreach (var device in devices.Where(d => d.IsReady && d.IsTcp && _hardwareAsked.Add(d.Serial)))
        {
            _ = Task.Run(async () =>
            {
                var hardware = await adb.GetHardwareSerialAsync(device.Serial).ConfigureAwait(false);
                if (hardware.Length > 0)
                {
                    _hardware[device.Serial] = hardware;
                }

                _hardwareRead[device.Serial] = true;
                await Dispatcher.BeginInvoke(EvaluatePhones);
            });
        }
    }

    // ----- Asking -----

    private void AskAboutBeside(AdbDevice candidate)
    {
        // Asked and still on screen: nothing to do. Pushed aside (by the lock question, a USB
        // problem), it is asked again once the notice bar is free.
        if (_askingAbout == candidate.Serial && _tipShowing == BesideNotice && NoticeBeside.Visibility == Visibility.Visible)
        {
            return;
        }

        // The lock question, a USB problem and a hint already up come first; this waits its turn.
        if (_tipShowing is not null || _usbNoticeShowing || _host.Session.PendingLockQuestionSerial is not null || NoticeBar.Visibility == Visibility.Visible)
        {
            return;
        }

        _askingAbout = candidate.Serial;
        _tipShowing = BesideNotice;
        var main = _host.Session.ActiveDevice is { } m ? NameOf(m.Serial) : "this phone";
        NoticeTitle.Text = $"{NameOf(candidate.Serial)} is connected too.";
        NoticeText.Text = $"Show it beside {main}? Each phone gets a view of its own; the side panel follows the one you click.";
        ShowNoticeAnswers(question: false);
        NoticeDismiss.Visibility = Visibility.Collapsed;
        ShowBesideButtons(true);
        NoticeBar.Visibility = Visibility.Visible;
    }

    private void ShowBesideButtons(bool shown)
    {
        foreach (var button in new[] { NoticeBeside, NoticeBesideNotNow, NoticeBesideNever })
        {
            button.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void DropBesideQuestion()
    {
        if (_tipShowing != BesideNotice)
        {
            return;
        }

        _tipShowing = null;
        _askingAbout = null;
        ShowBesideButtons(false);
        NoticeBar.Visibility = Visibility.Collapsed;
    }

    private void OnBesideAnswer(object sender, RoutedEventArgs e)
    {
        var serial = _askingAbout;
        var answer = (string)((FrameworkElement)sender).Tag;
        DropBesideQuestion();
        if (serial is null || _host.Session.Devices.FirstOrDefault(d => d.Serial == serial && d.IsReady) is not { } device)
        {
            return;
        }

        switch (answer)
        {
            case "show":
                _ = ShowBesideAsync(device);
                break;
            case "never":
                _host.State.Update(state =>
                {
                    if (state.Devices.TryGetValue(serial, out var profile))
                    {
                        profile.ShowBeside = ShowBesideAnswers.Never;
                    }
                });
                _notNow.Add(serial);
                SetStatus($"{NameOf(serial)} will not be offered beside again. The phone menu can still show it.");
                break;
            default:
                _notNow.Add(serial);
                break;
        }

        GiveKeyboardBack();
    }

    // ----- Showing it -----

    /// <summary>Shows a phone beside the main one: its view, then its own session in it.</summary>
    internal async Task<AndroidResult> ShowBesideAsync(AdbDevice device)
    {
        if (_other is not null && _other.Serial != device.Serial)
        {
            StopOther(byPerson: false);
        }

        _other = device;
        _otherState = OtherPhoneState.Starting;
        OtherWhy = string.Empty;
        _notNow.Remove(device.Serial);
        if (_host.Config.SecondPhone.Remember && _host.State.Ui.SecondPhone != device.Serial)
        {
            _host.State.SetUi(_host.State.Ui with { SecondPhone = device.Serial });
        }

        EnsureOtherView();
        SetStatus($"Showing {NameOf(device.Serial)} beside…");
        AfterPhonesChange();
        if (_host.Session.Adb is { } adb && _otherIdentity?.Serial != device.Serial)
        {
            try
            {
                _otherIdentity = await adb.GetIdentityAsync(device.Serial);
                if (_otherIdentity.HardwareSerial.Length > 0)
                {
                    _hardware[device.Serial] = _otherIdentity.HardwareSerial;
                }

                if (_otherIdentity.DisplayWidth > 0 && _otherIdentity.DisplayHeight > 0)
                {
                    Group.OtherAspect = (double)_otherIdentity.DisplayWidth / _otherIdentity.DisplayHeight;
                    _otherView!.SetVideoSize(_otherIdentity.DisplayWidth, _otherIdentity.DisplayHeight, reported: false);
                }

                _host.State.RememberDevice(device.Serial, _otherIdentity.DisplayName, _otherIdentity.Model);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                _host.Log.Warn($"Could not read {device.Serial}'s details: {ex.Message}");
            }
        }

        return await LaunchOtherAsync();
    }

    private async Task<AndroidResult> LaunchOtherAsync()
    {
        if (_other is not { } device || _otherView is not { } view)
        {
            return AndroidResult.Failure("No phone is shown beside.");
        }

        Group.UpdateLayout();
        var at = view.ViewportScreenRect;
        _otherArguments = OtherArgumentsKey(device);
        var launch = await _host.Session.LaunchOtherPhoneAsync(device, (at.Left, at.Top, Math.Max(200, at.Right - at.Left), Math.Max(200, at.Bottom - at.Top)));
        if (_other?.Serial != device.Serial || _otherView is null)
        {
            // Stopped while it was starting.
            launch.Process?.Kill();
            launch.Process?.Dispose();
            return AndroidResult.Failure("It was stopped.");
        }

        if (launch.Process is not { } process)
        {
            return OtherEnded(stoppedOnPurpose: false, ranFor: TimeSpan.Zero, why: launch.Reason);
        }

        _otherProcess = process;
        _otherStartedAt = DateTime.UtcNow;
        _otherState = OtherPhoneState.Showing;
        // The phone you were using keeps the keyboard; clicking the new one moves it there.
        view.Attach(process.Hwnd, process.ThreadId, (uint)process.ProcessId, focus: false);
        process.VideoSizeChanged += (width, height) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(process, _otherProcess) && _otherView is { } shown && width > 0 && height > 0)
            {
                shown.SetVideoSize(width, height);
                Group.OtherAspect = (double)width / height;
                Group.InvalidateMeasure();
            }
        });
        _ = process.Exited.ContinueWith(_ => Dispatcher.BeginInvoke(() => OnOtherExited(process)), TaskScheduler.Default);
        BeginOtherSound(process);
        _ = ReadOtherBatteryAsync(force: true);
        SetStatus($"{NameOf(device.Serial)} is beside {(_host.Session.ActiveDevice is { } main ? NameOf(main.Serial) : "the main phone")}");
        AfterPhonesChange();
        return AndroidResult.Success(SummaryOfPhones());
    }

    private void OnOtherExited(ScrcpyProcess process)
    {
        if (!ReferenceEquals(process, _otherProcess))
        {
            process.Dispose();
            return;
        }

        var ranFor = DateTime.UtcNow - _otherStartedAt;
        var why = ScrcpyFailureWords(process);
        _otherProcess = null;
        _otherView?.Detach();
        process.Dispose();
        OtherEnded(stoppedOnPurpose: false, ranFor, why);
    }

    private static string ScrcpyFailureWords(ScrcpyProcess process)
    {
        var lines = process.RecentStderr.Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)).TakeLast(1).ToArray();
        return lines.Length > 0 ? lines[0].Replace("ERROR:", string.Empty, StringComparison.Ordinal).Trim() : "it closed by itself";
    }

    /// <summary>After the other session ended: start it again, wait for the phone, or give up, as the plan says.</summary>
    private AndroidResult OtherEnded(bool stoppedOnPurpose, TimeSpan ranFor, string why)
    {
        if (_other is not { } device)
        {
            return AndroidResult.Failure(why);
        }

        var ready = _host.Session.Devices.Any(d => d.Serial == device.Serial && d.IsReady);
        var (next, restarts) = SecondPhonePlan.AfterExit(stoppedOnPurpose, ready, _host.Config.Session.RestartOnUnexpectedExit, _otherRestarts, ranFor,
            _host.Config.Session.RestartLimit);
        _otherRestarts = restarts;
        OtherWhy = why;
        _host.Log.Info($"The session beside of {device.Serial} ended ({why}): {next}");
        switch (next)
        {
            case OtherPhoneAfterExit.Restart:
                _otherState = OtherPhoneState.Starting;
                SetStatus($"{NameOf(device.Serial)} closed ({why}). Starting it again…");
                _otherRetry ??= new DispatcherTimer();
                _otherRetry.Interval = TimeSpan.FromSeconds(Math.Max(1, _host.Config.Session.RetrySeconds));
                _otherRetry.Tick -= OnOtherRetry;
                _otherRetry.Tick += OnOtherRetry;
                _otherRetry.Start();
                AfterPhonesChange();
                return AndroidResult.Failure(why);
            case OtherPhoneAfterExit.GiveUp:
            {
                var name = NameOf(device.Serial);
                StopOther(byPerson: false);
                _notNow.Add(device.Serial);
                SetStatus($"{name} keeps closing ({why}). Show it beside again from the phone menu.", isError: true);
                return AndroidResult.Failure(why);
            }

            default:
                StopOther(byPerson: false);
                return AndroidResult.Failure(why);
        }
    }

    private void OnOtherRetry(object? sender, EventArgs e)
    {
        _otherRetry?.Stop();
        if (_other is not null && _otherProcess is null && _otherState == OtherPhoneState.Starting)
        {
            _ = LaunchOtherAsync();
        }
    }

    /// <summary>
    /// Stops showing the other phone. Stopped by hand, it is not offered again until it next
    /// connects, nor brought back by itself.
    /// </summary>
    internal void StopOther(bool byPerson)
    {
        var device = _other;
        _otherRetry?.Stop();
        EndOtherSound();
        var process = _otherProcess;
        _otherProcess = null;
        _otherView?.Detach();
        if (process is not null)
        {
            process.Kill();
            process.Dispose();
        }

        RemoveOtherView();
        _other = null;
        _otherIdentity = null;
        _otherBattery = null;
        _otherState = OtherPhoneState.Off;
        _otherRestarts = 0;
        if (byPerson && device is not null)
        {
            _notNow.Add(device.Serial);
            if (_host.State.Ui.SecondPhone == device.Serial)
            {
                _host.State.SetUi(_host.State.Ui with { SecondPhone = string.Empty });
            }

            SetStatus($"{NameOf(device.Serial)} is no longer shown beside");
        }

        AfterPhonesChange();
    }

    /// <summary>
    /// The other phone becomes the main one and the main one goes beside it: both sessions start
    /// again in their new places, and copies and the second screen go with the main phone.
    /// </summary>
    internal AndroidResult MakeOtherTheMain()
    {
        if (_other is not { } other || _host.Session.ActiveDevice is not { } main)
        {
            return AndroidResult.Failure("No phone is shown beside.");
        }

        _swappingTo = other.Serial;
        _swapGivesUpAt = DateTime.UtcNow.AddMinutes(1);
        StopOther(byPerson: false);
        _notNow.Remove(main.Serial);
        _host.State.SetUi(_host.State.Ui with { SecondPhone = main.Serial });
        _host.UpdateConfig(c => c.Session.PreferredSerial = other.Serial);
        _host.Session.RestartMirror();
        SetStatus($"{NameOf(other.Serial)} is the main phone now");
        return AndroidResult.Success($"{NameOf(other.Serial)} is the main phone now");
    }

    private void EnsureOtherView()
    {
        if (_otherView is not null)
        {
            return;
        }

        var view = new MirrorHost { MaxZoom = Host.MaxZoom };
        view.ChildFocused += () =>
        {
            _activeView = view;
            AfterTargetChange();
        };
        _otherView = view;
        Group.Children.Insert(Group.OtherIndex, view);
        Group.Other = view;
    }

    private void RemoveOtherView()
    {
        if (_otherView is not { } view)
        {
            return;
        }

        if (ReferenceEquals(_activeView, view))
        {
            _activeView = Host;
        }

        _otherView = null;
        Group.Other = null;
        Group.Children.Remove(view);
        view.Dispose();
    }

    /// <summary>The layout, copies, the panels and the marks follow a phone coming or going.</summary>
    private void AfterPhonesChange()
    {
        OnSessionChanged();
        Group.InvalidateMeasure();
        UpdateRoom();
        UpdateCopiesShown();
        AfterTargetChange();
        TrackOverlay();
    }

    /// <summary>The side panel and the strip above it follow the phone in use.</summary>
    private void AfterTargetChange()
    {
        var two = _otherView is not null && _other is not null;
        PhoneStrip.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        if (two && TargetPhone is { } target)
        {
            var other = OtherActive ? _host.Session.ActiveDevice?.Serial : _other!.Serial;
            PhoneStripName.Text = NameOf(target.Serial);
            PhoneStripSwitch.Content = other is null ? "Use the other phone" : "Use " + NameOf(other);
            PhoneStripSwitch.IsEnabled = other is not null;
            PhoneStripSwitch.ToolTip = Shortcuts.Tip("The tabs, keys and buttons act on the other phone", "phone-switch");
        }

        FollowProfile();
        _otherSound?.Refresh();
        _sound?.Refresh();
        ShowSound();
        ControlsPanel.Refresh();
        PhonePanel.Refresh(force: true);
        PhonePanel.Advanced.TargetChanged();
        InfoPanel.Refresh();
        AppsEdited();
    }

    /// <summary>
    /// With profiles following the phone in use, the phone now in use counts as the one connected:
    /// its own profile switches on, and the other's is put back.
    /// </summary>
    private void FollowProfile()
    {
        var target = TargetPhone?.Serial;
        if (!_host.Config.SecondPhone.FollowsProfile || target is null || target == _profileFollows || _other is null && _profileFollows is null)
        {
            return;
        }

        _profileFollows = _other is null ? null : target;
        _host.Profiles.PhoneConnected(target);
    }

    private void OnPhoneStripSwitch(object sender, RoutedEventArgs e) => SwitchPhone();

    /// <summary>Use the other phone: its view takes the keyboard, and everything follows it.</summary>
    internal AndroidResult SwitchPhone()
    {
        if (_otherView is not { HasChild: true } other)
        {
            return AndroidResult.Failure("Only one phone is shown.");
        }

        var to = OtherActive ? Host : other;
        _activeView = to;
        to.FocusChild();
        AfterTargetChange();
        return AndroidResult.Success("Using " + (TargetPhone is { } target ? NameOf(target.Serial) : "the other phone"));
    }

    /// <summary>The "phone beside" action: stops showing the one beside, or shows another connected phone.</summary>
    internal async Task<AndroidResult> ToggleBesideAsync()
    {
        if (_other is not null)
        {
            StopOther(byPerson: true);
            return AndroidResult.Success("Only one phone is shown now");
        }

        if (_host.Session.ActiveDevice is not { } main)
        {
            return AndroidResult.Failure("Mirror a phone first.");
        }

        if (!_host.Config.SecondPhone.Enabled)
        {
            return AndroidResult.Failure("Showing a second phone is off in Settings.");
        }

        var candidate = PhonePick.Other(_host.Session.Devices, main, _hardware, _host.State.Ui.SecondPhone, new HashSet<string>());
        return candidate is null
            ? AndroidResult.Failure("No other phone is connected.")
            : await ShowBesideAsync(candidate);
    }

    /// <summary>"Galaxy S21 Ultra and Pixel 7 side by side", for the status line and the pipe.</summary>
    internal string SummaryOfPhones() =>
        _other is { } other && _host.Session.ActiveDevice is { } main
            ? $"{NameOf(main.Serial)} and {NameOf(other.Serial)} side by side"
            : "One phone shown";

    /// <summary>
    /// The phone's settings, the keys and the buttons for the phone beside: what goes over ADB uses
    /// its serial, scrcpy's shortcuts its own session; gestures and screenshots are played and
    /// taken for it. Null means the action goes as usual.
    /// </summary>
    internal async Task<(bool Ok, string Text)?> RunOnOtherPhoneAsync(string id)
    {
        if (!OtherActive || _other is not { } device || _host.Session.Adb is not { } adb)
        {
            return null;
        }

        var label = MirrorActions.Find(id)?.Label ?? id;
        var name = NameOf(device.Serial);
        var route = ActionRouting.ForOtherPhone(id);
        switch (route.Kind)
        {
            case RouteKind.Refuse:
                return (false, route.Why!);
            case RouteKind.OtherPhoneAdb:
            {
                var result = await MirrorActions.RunAdbAsync(adb, device.Serial, id);
                return (result.Ok, result.Ok ? $"{label} on {name}" : result.Text);
            }

            case RouteKind.OtherPhoneSession:
                return _otherProcess is { HasExited: false } process && ScrcpyShortcutSender.Send(process.Hwnd, route.Shortcut!)
                    ? (true, $"{label} on {name}")
                    : (false, $"Could not send that to {name}.");
        }

        if (MirrorActions.IsGesture(id))
        {
            var played = await _host.Session.PlayGestureOverAdbAsync(id, Group.OtherAspect > 1, device.Serial);
            return (played.Ok, played.Ok ? $"{played.Text} on {name}" : played.Text);
        }

        if (id == "screenshot")
        {
            return await SaveScreenshotAsync(device.Serial);
        }

        return null;
    }
}
