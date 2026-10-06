using System.Windows;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;

namespace Rex.Mirror;

/// <summary>
/// The second screen: one app on a display of its own, shown beside the phone or instead of it. It
/// is a session of its own (<see cref="Session.SessionController.LaunchScreenAsync"/>) embedded in a
/// view the mirror area lays out with the phone; <see cref="SecondScreenPlan"/> decides what happens
/// next, and copies wait with no room while it is open.
/// </summary>
public partial class MainWindow
{
    private readonly SecondScreenPlan _screen = new();
    private ScrcpyProcess? _screenProcess;
    private MirrorHost? _screenView;
    private string _screenSerial = string.Empty;

    /// <summary>The second screen's view while one is open.</summary>
    internal MirrorHost? ScreenView => _screenView;

    internal ScreenState ScreenState => _screen.State;

    internal string ScreenApp => _screen.App;

    internal int? ScreenDisplayId => _screenProcess?.DisplayId;

    internal ScreenSpec? ScreenSpecNow => _screen.Spec;

    /// <summary>The names over the views now, and whether one is outlined: what the status reports.</summary>
    internal IReadOnlyList<string> ViewCaptions => _overlay.MarkCaptions;

    internal bool ViewCaptionsShowing => _overlay.MarkCaptionsShowing;

    internal bool ViewOutlined => _overlay.MarkOutlined;

    internal int ScreenProcessId => _screenProcess is { HasExited: false } process ? process.ProcessId : 0;

    /// <summary>True while the second screen's view has the keyboard: phone keys then go to its display.</summary>
    internal bool ScreenActive => _screenView is { HasChild: true } view && ReferenceEquals(_activeView, view);

    /// <summary>Why the shown phone cannot have a second screen, or null when it can.</summary>
    internal string? WhyNoSecondScreen =>
        !_host.Session.IsMirroring ? "Mirror the phone first."
        : _host.Session.Identity is { } identity ? SecondScreenPlan.WhyNot(identity.ApiLevel, identity.AndroidVersion)
        : null;

    /// <summary>One line for the Controls tab and the status: what the second screen is doing.</summary>
    internal string ScreenWords => _screen.State switch
    {
        ScreenState.Opening => $"Opening {AppName(_screen.App)} on a second screen…",
        ScreenState.Showing => $"{AppName(_screen.App)} on a second screen " +
            (_host.Config.SecondScreen.Placement == "instead" || Group.Arrangement is { Squeezed: true } ? "instead of the phone" : "beside the phone"),
        ScreenState.Failed => "Could not open it: " + _screen.Why,
        _ => WhyNoSecondScreen ?? "Off",
    };

    private void AttachSecondScreen()
    {
        _host.Session.MirrorReady += _ => ReopenSecondScreen();
        _host.Session.MirrorEnded += () => CloseSecondScreen(byPerson: false);
        _host.ConfigChanged += ApplySecondScreenConfig;
        Group.Split = _host.State.Ui.ViewSplit;
        ApplySecondScreenConfig();
    }

    /// <summary>The app's own name when the phone's list has it, else its package.</summary>
    private string AppName(string package) =>
        AppsSerial is { } serial && _host.State.GetDevice(serial)?.Apps?.FirstOrDefault(a => a.Package == package) is { } app ? app.Name : package;

    /// <summary>
    /// Opens an app on the second screen: on the open one when there is one (the display stays,
    /// only the app changes), otherwise on a new one.
    /// </summary>
    internal async Task<AndroidResult> OpenOnSecondScreenAsync(string package, bool fresh)
    {
        if (WhyNoSecondScreen is { } why)
        {
            SetStatus(why, isError: true);
            return AndroidResult.Failure(why);
        }

        var serial = _host.Session.ActiveDevice!.Serial;
        if (_screen.State == ScreenState.Showing && _screenProcess?.DisplayId is { } display)
        {
            var switched = await _host.Session.OpenAppOnDisplayAsync(serial, package, fresh, display);
            if (switched.Ok)
            {
                _screen.Switched(package);
                _host.State.SetSecondScreenApp(serial, package);
            }

            SetStatus(switched.Ok ? $"{AppName(package)} on the second screen" : "Could not open it there: " + switched.Text, !switched.Ok);
            ControlsPanel.Refresh();
            return switched;
        }

        _screenSerial = serial;
        _screen.Open(package, SpecFor(package, fresh));
        _host.State.SetSecondScreenApp(serial, package);
        return await LaunchSecondScreenAsync();
    }

    /// <summary>The display the settings ask for now, with this app on it.</summary>
    private ScreenSpec SpecFor(string package, bool fresh)
    {
        var identity = _host.Session.Identity;
        var phone = identity is null ? (0, 0) : (identity.DisplayWidth, identity.DisplayHeight);
        return ScreenSpec.For(_host.Config.SecondScreen, ScreenViewPixels(), phone, package, fresh);
    }

    /// <summary>The pixels the second screen's cell will have, from the layout it would get now.</summary>
    private (int Width, int Height) ScreenViewPixels()
    {
        var arrangement = ViewsLayout.Arrange(Group.ActualWidth, Group.ActualHeight, Group.Aspect, _host.Config.SecondScreen, _host.Config.Views, Group.Gap, Group.Split);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        return ((int)(arrangement.Screen.Width * scale), (int)(arrangement.Screen.Height * scale));
    }

    private async Task<AndroidResult> LaunchSecondScreenAsync()
    {
        var spec = _screen.Spec!;
        EnsureScreenView(spec);
        SetStatus(ScreenWords);
        ControlsPanel.Refresh();
        Group.UpdateLayout();
        var at = _screenView!.ViewportScreenRect;
        var launch = await _host.Session.LaunchScreenAsync(spec, (at.Left, at.Top));
        if (launch.Process is not { } process)
        {
            return await ScreenFailedAsync(launch.Reason);
        }

        if (_screen.State == ScreenState.Off || _screenView is null)
        {
            // Closed while it was opening.
            process.Dispose();
            return AndroidResult.Failure("The second screen was closed.");
        }

        _screenProcess = process;
        _screenView.Attach(process.Hwnd, process.ThreadId, (uint)process.ProcessId);
        _screenView.SetVideoSize(spec.Width, spec.Height);
        _screen.Opened();
        _ = process.Exited.ContinueWith(_ => Dispatcher.BeginInvoke(() => OnScreenExited(process)), TaskScheduler.Default);
        if (_host.Session.ActiveDevice is { } device)
        {
            _host.State.NoteAppOpened(device.Serial, spec.App, DateTimeOffset.UtcNow, _host.Config.Apps.RecentCount);
        }

        SetStatus(ScreenWords);
        AfterScreenChange();
        _screenView.FocusChild();
        return AndroidResult.Success(ScreenWords);
    }

    /// <summary>One more try after a failure, then the reason, and the view goes.</summary>
    private async Task<AndroidResult> ScreenFailedAsync(string why)
    {
        _host.Log.Warn("Second screen: " + why);
        DropScreenSession();
        if (_screen.State == ScreenState.Off)
        {
            // Closed meanwhile: nothing to try again or to say.
            RemoveScreenView();
            return AndroidResult.Failure(why);
        }

        if (_screen.Failed(why) == ScreenStep.Retry)
        {
            return await LaunchSecondScreenAsync();
        }

        RemoveScreenView();
        SetStatus("Could not open the second screen: " + why, isError: true);
        AfterScreenChange();
        return AndroidResult.Failure(why);
    }

    private void OnScreenExited(ScrcpyProcess process)
    {
        if (!ReferenceEquals(process, _screenProcess))
        {
            process.Dispose();
            return;
        }

        _ = ScreenFailedAsync("It closed by itself.");
    }

    /// <summary>Shows the Apps tab asking for the app the second screen opens with.</summary>
    internal void ChooseAppForSecondScreen()
    {
        if (WhyNoSecondScreen is { } why)
        {
            SetStatus(why, isError: true);
            return;
        }

        ShowTab("apps");
        AppsPanel.ChooseForScreen();
        SetStatus("Choose an app for the second screen");
    }

    /// <summary>
    /// A click on an app in the Apps tab: on the second screen while one is being chosen for, or as
    /// the setting says: on the phone, on the second screen, or asking which.
    /// </summary>
    internal async Task OpenFromAppsAsync(PhoneApp app, bool fresh, bool onScreen)
    {
        var where = onScreen ? "second-screen" : _host.Config.Apps.OpenOn;
        if (where == "ask" && WhyNoSecondScreen is null)
        {
            var answer = await ConfirmChoiceAsync($"Open {app.Name}", "On the phone's own screen, or on a second screen beside it?",
                "On a second screen", "On the phone", "Cancel");
            where = answer.Choice switch
            {
                "On a second screen" => "second-screen",
                "On the phone" => "phone",
                _ => string.Empty,
            };
        }

        if (where == "second-screen")
        {
            await OpenOnSecondScreenAsync(app.Package, fresh);
        }
        else if (where.Length > 0)
        {
            await OpenAppAsync(app, fresh);
        }
    }

    /// <summary>The keyboard goes back to the view that had it.</summary>
    internal void GiveKeyboardBack()
    {
        if (ActiveView.HasChild)
        {
            ActiveView.FocusChild();
        }
    }

    /// <summary>
    /// The second screen's key and action: closes an open one; otherwise opens it again with its last
    /// app, or, when it has never had one, asks for an app in the Apps tab.
    /// </summary>
    internal async Task<AndroidResult> ToggleSecondScreenAsync()
    {
        if (_screen.State is ScreenState.Showing or ScreenState.Opening)
        {
            CloseSecondScreen();
            return AndroidResult.Success("The second screen is closed");
        }

        var serial = AppsSerial;
        var last = _screen.App.Length > 0 ? _screen.App : serial is null ? string.Empty : _host.State.GetDevice(serial)?.SecondScreenApp ?? string.Empty;
        if (last.Length > 0)
        {
            return await OpenOnSecondScreenAsync(last, fresh: false);
        }

        ChooseAppForSecondScreen();
        return AndroidResult.Success("Choose an app for the second screen");
    }

    /// <summary>
    /// Closes the second screen. Its app moves to the phone or closes, as the settings say; closed by
    /// the mirror stopping, the app is remembered for opening it again with the mirror.
    /// </summary>
    internal void CloseSecondScreen(bool byPerson = true)
    {
        var app = _screen.App;
        if (_screen.Close() == ScreenStep.Nothing && _screenView is null)
        {
            return;
        }

        DropScreenSession();
        RemoveScreenView();
        if (byPerson)
        {
            SetStatus(_host.Config.SecondScreen.KeepAppsOnClose ? $"{AppName(app)} is back on the phone" : $"{AppName(app)} was closed");
        }

        AfterScreenChange();
    }

    /// <summary>The mirror started again: the second screen comes back with its app when asked to.</summary>
    private void ReopenSecondScreen()
    {
        var serial = _host.Session.ActiveDevice?.Serial;
        var app = _screen.App.Length > 0 ? _screen.App : serial is null ? string.Empty : _host.State.GetDevice(serial)?.SecondScreenApp ?? string.Empty;
        if (_host.Config.SecondScreen.ReopenOnStart && app.Length > 0 && _screen.State == ScreenState.Off && WhyNoSecondScreen is null)
        {
            _ = OpenOnSecondScreenAsync(app, fresh: false);
        }
    }

    /// <summary>
    /// Settings that change the display make it again with the same app; settings about the layout
    /// apply at once.
    /// </summary>
    private void ApplySecondScreenConfig()
    {
        Group.ScreenSettings = _host.Config.SecondScreen;
        Group.ViewsSettings = _host.Config.Views;
        Group.InvalidateMeasure();
        if (_screenView is not null)
        {
            _screenView.FillsViewport = _host.Config.SecondScreen.Size == "follow";
        }

        if (_screen.State == ScreenState.Showing && _screen.SettingsChanged(SpecFor(_screen.App, fresh: false)) == ScreenStep.Reopen)
        {
            SetStatus($"Reopening the second screen ({_screen.Spec!.Describe()})…");
            DropScreenSession();
            _ = LaunchSecondScreenAsync();
        }

        UpdateScreenSplitter();
    }

    private void EnsureScreenView(ScreenSpec spec)
    {
        if (_screenView is not null)
        {
            _screenView.FillsViewport = spec.Follows;
            return;
        }

        var view = new MirrorHost { FillsViewport = spec.Follows, MaxZoom = Host.MaxZoom };
        view.ChildFocused += () => _activeView = view;
        _screenView = view;
        Group.Children.Insert(1, view);
        Group.Screen = view;
        AfterScreenChange();
    }

    private void DropScreenSession()
    {
        var process = _screenProcess;
        _screenProcess = null;
        _screenView?.Detach();
        if (process is not null)
        {
            process.Kill();
            process.Dispose();
        }
    }

    private void RemoveScreenView()
    {
        if (_screenView is not { } view)
        {
            return;
        }

        if (ReferenceEquals(_activeView, view))
        {
            _activeView = Host;
        }

        _screenView = null;
        Group.Screen = null;
        Group.Children.Remove(view);
        view.Dispose();
    }

    /// <summary>Copies make room or take it back, the layout and the marks follow, and the panel says so.</summary>
    private void AfterScreenChange()
    {
        Group.InvalidateMeasure();
        UpdateRoom();
        UpdateScreenSplitter();
        ControlsPanel.Refresh();
        TrackOverlay();
    }

    /// <summary>
    /// The marks over the views while a second screen is open: a name on each view (when they show
    /// different things, or always), and an outline round the one that has the keyboard.
    /// </summary>
    private void UpdateViewMarks(bool visible)
    {
        var captions = new List<(Rect, string)>();
        Rect? outline = null;
        // Two phones name themselves; with a second screen too, it is named beside them.
        var phones = visible && MarkPhones(captions, ref outline);
        // Named only when they show different things, the names say themselves and step aside.
        var briefly = _host.Config.Views.Captions == "auto";
        if (!visible || _screenView is null || Group.Arrangement is not { } arrangement)
        {
            _overlay.ShowMarks(captions, outline, briefly);
            return;
        }

        static Rect Box(RectD r) => new(r.X, r.Y, r.Width, r.Height);
        var views = _host.Config.Views;
        var several = arrangement.Phone is not null;
        if (ViewsSettings.Shows(views.Captions, several))
        {
            if (!phones && arrangement.Phone is { } phone)
            {
                captions.Add((Box(phone), "Phone · " + (_host.Session.Identity?.DisplayName ?? "your phone")));
            }

            captions.Add((Box(arrangement.Screen), "Second screen · " + AppName(_screen.App)));
        }

        if (ViewsSettings.Shows(views.Outline, several))
        {
            outline = ScreenActive ? Box(arrangement.Screen)
                : phones ? outline
                : arrangement.Phone is { } held ? Box(held)
                : null;
        }

        _overlay.ShowMarks(captions, outline, briefly);
    }

    /// <summary>
    /// A display that follows the view is made again at each new size, so while the window is being
    /// dragged its picture holds still, and it takes the new size once the drag ends.
    /// </summary>
    private void HoldScreenWhileSizing(bool sizing)
    {
        if (_screenView is { FillsViewport: true } view && !_host.Config.SecondScreen.ResizeWhileDragging)
        {
            view.Frozen = sizing;
        }
    }

    /// <summary>
    /// Routes an action while the second screen has the keyboard: Home, Back, Recents and its own
    /// view's turns go to its session; turning the phone is refused with the reason. Null means the
    /// action goes as usual.
    /// </summary>
    internal (bool Ok, string Text)? RunOnSecondScreen(string id)
    {
        if (!ScreenActive || _screenProcess is not { HasExited: false } process)
        {
            return null;
        }

        var route = ActionRouting.For(id, screenActive: true);
        return route.Kind switch
        {
            RouteKind.Screen => ScrcpyShortcutSender.Send(process.Hwnd, route.Shortcut!)
                ? (true, (MirrorActions.Find(id)?.Label ?? id) + " on the second screen")
                : (false, "Could not send that to the second screen."),
            RouteKind.Refuse => (false, route.Why!),
            _ => null,
        };
    }
}
