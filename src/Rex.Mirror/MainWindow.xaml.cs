using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services;
using Rex.Mirror.Session;
using Rex.Mirror.Views;

namespace Rex.Mirror;

/// <summary>The single window: mirror on the left, side panel on the right, everything else inline.</summary>
public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly OverlayWindow _overlay;
    private readonly TouchInjector _injector = new();
    private readonly TouchpadBridge _touchpad;
    private readonly KeyboardTouch _keyboardTouch;
    private bool _browse;
    private readonly InputHooks _hooks = new();
    private readonly DispatcherTimer _overlayTimer;
    private readonly DispatcherTimer _ambientTimer;
    private readonly LiveCapture _liveCapture;
    private bool _ambientWanted;
    private bool _previewWanted;
    private PatternGuide? _guide;
    private HwndSource? _source;
    private bool _fullscreen;
    private bool _fullscreenTransition;
    private bool _sidebarWanted = true;
    private bool _quitting;
    private bool _trayHintShown;
    private double _navigatorStartZoom;
    private WindowState _restoreState = WindowState.Normal;
    private Rect _windowedBounds;
    public bool IsFullscreen => _fullscreen;
    public double SidebarScrollOffset => SidebarScroll.VerticalOffset;
    public bool AmbientFrameAvailable => Ambient.FrameAvailable;
    public string SidebarTab => CurrentTab();
    private Rect _sidebarWheelBounds = Rect.Empty;
    public bool HudVisible => _overlay.HudVisible;
    public RectD HudBarRect => _overlay.HudBarRect;
    public bool OnboardingVisible => Onboarding.Visibility == Visibility.Visible;
    public bool SidebarVisible => Sidebar.Visibility == Visibility.Visible;
    public double SidebarWidthDip => Math.Round(SidebarColumn.ActualWidth);
    public bool AmbientVisible => Ambient.IsShowing;
    public bool NavigatorPictureVisible => _overlay.NavigatorPictureAvailable;
    public string CapturePath => _liveCapture.Path;
    public bool NavigatorVisible => _overlay.NavigatorVisible;
    public bool NavigatorDragging => _overlay.NavigatorDragging;
    public RectD NavigatorRect => _overlay.NavigatorScreenRect;
    public bool TourVisible => TourLayer.IsRunning;
    public int TourStep => TourLayer.StepNumber;
    public int TourStepCount => TourLayer.StepCount;
    public string TipShowing => _tipShowing ?? string.Empty;
    public bool PatternGuideVisible => _guide?.IsVisible == true;
    public bool PatternGuideResolving => _guide?.IsResolving == true;
    public string PatternGuideSource => _guide?.Source ?? PatternGeometry.SourceUnavailable;
    public bool PatternSpinnerRunning => _overlay.PatternSpinnerRunning;
    public PatternBounds? PatternGuideBounds => _guide?.NormalizedBounds;

    /// <summary>True while plain keys drive the phone instead of typing into it.</summary>
    public bool BrowseMode => _browse;

    public MainWindow(AppHost host)
    {
        _host = host;
        _liveCapture = new LiveCapture(host.Log.Info);
        InitializeComponent();
        Host.MaxZoom = host.Config.Zoom.MaxZoom;

        _overlay = new OverlayWindow(this);
        _overlay.HudActionRequested += async id => await RunHudActionAsync(id);
        _overlay.PointerMessage += OnOverlayPointer;
        // Dragging moves the bar as it happens; the drop is what gets written to disk.
        _overlay.HudMovedTo += (x, y) => _host.PreviewConfig(c => { c.Hud.X = x; c.Hud.Y = y; });
        _overlay.HudMoveFinished += () =>
        {
            var (x, y) = (_host.Config.Hud.X, _host.Config.Hud.Y);
            _host.UpdateConfig(c => { c.Hud.X = x; c.Hud.Y = y; });
        };
        _overlay.PanDelta += (dx, dy) => Host.Pan(dx, dy);
        _overlay.NavigatorTarget += (fx, fy) => Host.CenterOn(fx, fy);
        _overlay.NavigatorDragStarted += () =>
        {
            _navigatorStartZoom = Host.Zoom;
            _overlay.NavigatorMinScale = Host.Zoom / Host.MaxZoom;
            _overlay.NavigatorMaxScale = Host.Zoom;
        };
        _overlay.NavigatorResize += (scale, x, y) =>
        {
            var (width, height) = Host.ViewportPixels;
            Host.SetZoom(_navigatorStartZoom / scale, width / 2.0, height / 2.0);
            Host.CenterOn(x, y);
        };
        _overlay.PanAllowed = () => _host.Config.Zoom.Enabled && Host.View.IsZoomed && NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
        _touchpad = new TouchpadBridge(Host, _injector, () => _host.Config, host.Log.Warn)
        {
            PanelAt = (x, y) => SidebarScroll.IsVisible && _sidebarWheelBounds.Contains(x, y),
            ScrollPanel = delta => SidebarScroll.ScrollToVerticalOffset(SidebarScroll.VerticalOffset - delta),
            MapToMain = OnMainView,
        };
        _keyboardTouch = new KeyboardTouch(Host, _injector, host.Log.Warn)
        {
            Fallback = action => _host.Session.PlayGestureOverAdbAsync(action, Host.SurfaceRect.Width > Host.SurfaceRect.Height),
            SurfaceOnScreen = () => IsVisible && WindowState != WindowState.Minimized,
            Settings = () => _host.Config.Input,
        };

        _overlayTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _overlayTimer.Tick += (_, _) => TrackOverlay();
        _ambientTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000 / 15.0) };
        _ambientTimer.Tick += (_, _) => CaptureAmbientFrame();

        Host.ViewChanged += _ => OnViewChanged();
        InitCopies();
        Host.ChildFocused += () => _overlay.ClearTrail();

        // WPF IsActive can lag when focus crosses into the embedded scrcpy HWND.
        // Route global hooks using the actual foreground window's root instead.
        _hooks.IsActive = () => _source is not null && IsVisible && WindowState != WindowState.Minimized &&
            NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2) == _source.Handle;
        _hooks.AltWheel = OnAltWheel;
        _hooks.PanelWheel = OnPanelWheel;
        _hooks.KeyDown = OnHotkey;
        _hooks.GlobalKey = OnGlobalKey;
        _hooks.Record = OnRecordKey;
        _hooks.PcAlt = down =>
        {
            // Whichever view has the keyboard: the main one or a copy.
            foreach (var view in AllViews)
            {
                if (down)
                {
                    view.HoldKeyboard();
                }
                else
                {
                    view.ReleaseKeyboard();
                }
            }
        };

        InitFiles();
        ControlsPanel.Attach(this, host);
        PhonePanel.Attach(this, host);
        AttachApps();
        SettingsPanel.Attach(this, host);
        InfoPanel.Attach(this, host);
        Onboarding.Attach(this, host);
        InitSound();

        host.Session.Changed += OnSessionChanged;
        host.Usb.Changed += OnUsbChanged;
        host.Session.MirrorReady += OnMirrorReady;
        host.Session.MirrorEnded += OnMirrorEnded;
        host.Session.LaunchRect = LaunchRect;
        host.ConfigChanged += OnConfigChanged;
        host.ConfigPreviewed += () => { Host.MaxZoom = _host.Config.Zoom.MaxZoom; PreviewCopies(); TrackOverlay(); };

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            _host.Log.Info("DPI awareness: " + DpiAwareness.Describe(_source.Handle));
            NativeMethods.ApplyDarkTitleBar(_source.Handle);
            _source.AddHook(WindowHook);
            NativeMethods.TryRegisterTouchpadWindow(_source.Handle, true);
        };
        Loaded += (_, _) => { _hooks.Install(); OnSessionChanged(); OfferWhatsNew(); };
        LocationChanged += (_, _) => TrackOverlay();
        SizeChanged += (_, _) => TrackOverlay();
        StateChanged += (_, _) => TrackOverlay();
        Activated += (_, _) => { if (ActiveView.HasChild) { ActiveView.FocusChild(); } };
        Deactivated += (_, _) => _overlay.ClearTrail();
        Closing += OnClosing;

        ApplyPlacement();
        SetSidebarVisible(host.State.Ui.SidebarVisible);
        SelectTab(host.State.Ui.SidebarTab);
        ApplyWindowPreferences();
        new WindowInteropHelper(this).EnsureHandle();
    }

    // ----- Window lifecycle -----

    public void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        Activate();
        TrackOverlay();
    }

    public void HideToTray()
    {
        _touchpad.Cancel();
        Hide();
        _overlay.Track(default, visible: false);
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _host.Tray?.Notify("Still running", "Android Headless Mirror keeps waiting for your phone in the tray.");
        }
    }

    /// <summary>
    /// True once the app is quitting. Work queued before then (a layout pass's follow-up, a USB
    /// check that was already running) can land after the application's resources are gone, and
    /// anything that looks one up would throw and keep the process from exiting.
    /// </summary>
    internal bool Quitting => _quitting;

    public void QuitApplication()
    {
        _quitting = true;
        SavePlacement();
        _host.Session.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _touchpad.Cancel();
        if (_quitting)
        {
            _ambientTimer.Stop();
            _liveCapture.Dispose();
            _overlay.Close();
            _hooks.Dispose();
            _copies?.Dispose();
            _files?.Dispose();
            StopSound();
            return;
        }

        if (_host.Config.App.RunInBackground)
        {
            e.Cancel = true;
            SavePlacement();
            HideToTray();
            return;
        }

        QuitApplication();
    }

    // ----- Session events -----

    private void OnSessionChanged()
    {
        if (_quitting)
        {
            return;
        }

        var session = _host.Session;
        RefreshRestartNotice();
        var mirroring = session.IsMirroring;
        var needsSetup = session.Phase == SessionPhase.NeedsSetup;
        var usbBlocked = session.Devices.Count == 0 && session.UnreachableAdbInterfaces.Count > 0;
        var onboarding = OnboardingView.IsNeeded(_host);

        StatusText.Text = session.Message;
        StatusDot.Fill = (Brush)FindResource(mirroring ? "Signal" : session.Devices.Any(d => d.IsReady) ? "Signal" : session.Devices.Count > 0 ? "Live" : "Muted");

        if (session.Identity is not null && session.ActiveDevice is not null)
        {
            DeviceName.Text = session.Identity.DisplayName;
            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(session.Identity.Model) && session.Identity.Model != session.Identity.DisplayName)
            {
                meta.Add(session.Identity.Model);
            }

            if (!string.IsNullOrWhiteSpace(session.Identity.AndroidVersion))
            {
                meta.Add("Android " + session.Identity.AndroidVersion);
            }

            meta.Add(session.ActiveDevice.Transport);
            if (session.Battery is not null)
            {
                meta.Add(session.Battery.Level + "%" + (session.Battery.Charging ? " ⚡" : string.Empty));
            }

            DeviceMeta.Text = string.Join(" · ", meta);
        }
        else
        {
            DeviceName.Text = DeviceStateText.Header(needsSetup, session.Devices, usbBlocked);
            DeviceMeta.Text = string.Empty;
        }

        var choosable = session.Devices.Count > 1;
        DeviceChevron.Visibility = choosable ? Visibility.Visible : Visibility.Collapsed;
        DeviceChip.IsHitTestVisible = choosable;
        DeviceChip.Focusable = choosable;
        DeviceChip.Cursor = choosable ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        AutomationProperties.SetName(DeviceChip, choosable
            ? $"Connected phone: {DeviceName.Text}. Choose which phone to mirror."
            : "Connected phone: " + DeviceName.Text);
        Title = session.Identity is null
            ? "Android Headless Mirror"
            : session.Identity.DisplayName + " - Android Headless Mirror";

        Onboarding.Visibility = onboarding ? Visibility.Visible : Visibility.Collapsed;
        MirrorArea.Visibility = onboarding ? Visibility.Collapsed : Visibility.Visible;
        Sidebar.Visibility = onboarding ? Visibility.Collapsed : (_sidebarWanted && !_fullscreen ? Visibility.Visible : Visibility.Collapsed);
        Host.SetShown(mirroring && !onboarding);
        UpdateCopiesShown();
        if (onboarding)
        {
            Onboarding.Refresh();
        }

        EmptyState.Visibility = mirroring ? Visibility.Collapsed : Visibility.Visible;

        if (!mirroring)
        {
            EmptyTitle.Text = session.Phase switch
            {
                SessionPhase.Starting => "Starting the mirror…",
                SessionPhase.Stopped => "Mirror stopped",
                _ when session.Devices.Any(d => d.IsUnauthorized) => "Approve USB debugging on the phone",
                _ when session.Devices.Any(d => d.IsReady) => "Phone ready",
                _ when usbBlocked => "Phone found, but Windows blocks ADB",
                _ => "Connect your phone",
            };
            EmptyText.Text = session.Phase == SessionPhase.Waiting && session.Devices.Count == 0 && !usbBlocked
                ? "Plug in an Android phone with USB debugging turned on. It appears here automatically."
                : session.Message;
            EmptyPrimary.Visibility = session.Phase == SessionPhase.Stopped ? Visibility.Visible : Visibility.Collapsed;
            EmptyRepair.Visibility = usbBlocked && session.Phase == SessionPhase.Waiting ? Visibility.Visible : Visibility.Collapsed;

            // Two primary buttons side by side say neither is the thing to do. Repairing the driver
            // is what unblocks everything else, so it takes the emphasis when it is offered.
            var repairing = EmptyRepair.Visibility == Visibility.Visible;
            EmptyPrimary.Style = (Style)FindResource(repairing ? "BaseButton" : "PrimaryButton");
        }

        foreach (var button in new[] { QuickHome, QuickBack, QuickRecents, QuickScreenshot })
        {
            button.IsEnabled = session.Devices.Any(d => d.IsReady);
        }

        QuickSleep.IsEnabled = mirroring;

        var question = session.PendingLockQuestionSerial is not null && _host.Config.PatternGuide.Enabled;
        if (question && !_fullscreen)
        {
            _tipShowing = null;
            ShowNoticeAnswers(question: true);
            NoticeBar.Visibility = Visibility.Visible;
            if (session.Identity is not null)
            {
                NoticeTitle.Text = $"How does {session.Identity.DisplayName} unlock?";
                NoticeText.Text = "Pattern phones get a nine-dot guide when the lock screen shows black. Nothing about the pattern itself is stored.";
            }
        }
        else if (ShowUsbNotice())
        {
            // A phone Windows cannot read outranks a hint: it is why nothing is on screen.
        }
        else if (_tipShowing is null || _fullscreen)
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

        if (session.Devices.Count > 1)
        {
            ShowTipOnce(Tips.SecondPhone);
        }

        NoticeConnections();

        HintText.Text = _browse
            ? KeyboardBrowse.Hint
            : session.IsMirroring
            ? $"{Shortcuts.Gesture("host-zoom")} zoom · {Shortcuts.Gesture("host-pan")} pan · {Shortcuts.Gesture("browse")} browse · {Shortcuts.Gesture("fullscreen")} fullscreen"
            : session.Devices.Count == 0
                ? $"Plug a phone in over USB · {Shortcuts.Gesture("tour")} shows the tour"
                : $"{Shortcuts.Gesture("tour")} shows the tour";
        ConsiderTour();

        ControlsPanel.Refresh();
        PhonePanel.Refresh();
        InfoPanel.Refresh();
        _host.Tray?.Refresh();
    }

    private void OnMirrorReady(ScrcpyProcess scrcpy)
    {
        FollowScrcpyTransfers(scrcpy);
        var session = _host.Session;
        if (session.Identity is { DisplayWidth: > 0, DisplayHeight: > 0 } identity)
        {
            Host.SetVideoSize(identity.DisplayWidth, identity.DisplayHeight, reported: false);
        }

        Host.SetShown(true);
        Host.Attach(scrcpy.Hwnd, scrcpy.ThreadId, (uint)scrcpy.ProcessId);
        // A new mirror shows a live picture, whatever the last one was left at.
        SetPaused(false);

        // scrcpy says what shape the video is every time it changes. Its own window resize is the
        // other signal, but it is sent once and can be lost to a layout pass that lands first,
        // which left a landscape picture boxed at portrait width; this report cannot be lost.
        scrcpy.VideoSizeChanged += (width, height) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(scrcpy, _host.Session.Scrcpy) && Host.HasChild)
            {
                _host.Log.Info($"The picture is now {width}x{height}.");
                Host.SetVideoSize(width, height);
            }
        });
        if (scrcpy.VideoSize is { } size)
        {
            Host.SetVideoSize(size.Width, size.Height);
        }

        FollowFrameRate(scrcpy);
        _hooks.Install();
        _overlayTimer.Start();

        // The orientation choice shows what the phone is actually set to, which can only be read
        // once there is a phone to ask.
        _ = ControlsPanel.RefreshRotationAsync();

        StartPatternGuideIfNeeded();

        if (!IsVisible && _host.Config.App.OpenOnConnect)
        {
            ShowFromTray();
        }

        if (scrcpy.KeyboardMode == ScrcpyArguments.FullKeyboardMode)
        {
            ShowTipSoon(Tips.HardwareKeyboard);
        }

        OnSessionChanged();
        TrackOverlay();

        // Copies follow the main picture: they start once it is up, one at a time.
        _copies?.Pump();
    }

    private void OnMirrorEnded()
    {
        _touchpad.Cancel();
        _ambientTimer.Stop();
        Ambient.SetFrame(null);
        _guide?.Dispose();
        _guide = null;
        Host.Detach();
        Host.SetShown(false);
        if (_fullscreen) ToggleFullscreen();
        _overlayTimer.Stop();
        _overlay.Track(default, visible: false);
        // Browse mode is about the picture that just went away; the next session starts typing.
        _browse = false;
        HideFrameRate();
        SetPaused(false);
        // Nothing to copy without the main picture: the copies close, and come back with it.
        _copies?.Pump();
        OnSessionChanged();
    }

    public void StartPatternGuideIfNeeded()
    {
        var session = _host.Session;
        if (_guide is not null || !_host.Config.PatternGuide.Enabled || session.ActiveDevice is null || session.Identity is null || session.Adb is null)
        {
            return;
        }

        var profile = _host.State.GetDevice(session.ActiveDevice.Serial);
        if (profile?.LockScreenMode != LockScreenModes.Pattern)
        {
            return;
        }

        _guide = new PatternGuide(_host, Host, _overlay, session.Adb, session.ActiveDevice.Serial, session.Identity);
        _guide.Changed += () => ControlsPanel.Refresh();
        _guide.Start();
    }

    private void OnConfigChanged()
    {
        _touchpad.Cancel();
        Host.MaxZoom = _host.Config.Zoom.MaxZoom;
        if (!_host.Config.Zoom.Enabled) Host.ResetZoom();
        else if (Host.Zoom > Host.MaxZoom)
        {
            var (width, height) = Host.ViewportPixels;
            Host.SetZoom(Host.MaxZoom, width / 2.0, height / 2.0);
        }
        if (!_host.Config.PatternGuide.Enabled && _guide is not null)
        {
            _guide.Dispose();
            _guide = null;
        }
        else
        {
            StartPatternGuideIfNeeded();
        }

        ApplyCopiesConfig();
        ApplyFilesConfig();
        _host.Session.ApplyFrameRateSetting();
        ApplyWindowPreferences();
        SettingsPanel.Refresh();
        OnSessionChanged();
    }

    private (int X, int Y, int Width, int Height)? LaunchRect()
    {
        var rect = Host.ViewportScreenRect;
        if (rect.Width < 50 || rect.Height < 50)
        {
            return null;
        }

        var fit = ZoomMath.FitRect(rect.Width, rect.Height, 9, 20);
        return (rect.Left + (int)fit.X, rect.Top + (int)fit.Y, Math.Max(200, (int)fit.Width), Math.Max(200, (int)fit.Height));
    }

    // ----- Overlay / zoom -----

    private void TrackOverlay()
    {
        if (SidebarScroll.IsVisible && _source is not null)
        {
            var origin = SidebarScroll.PointToScreen(new Point());
            var scale = VisualTreeHelper.GetDpi(SidebarScroll);
            _sidebarWheelBounds = new Rect(origin.X, origin.Y, SidebarScroll.ActualWidth * scale.DpiScaleX,
                SidebarScroll.ActualHeight * scale.DpiScaleY);
        }
        else _sidebarWheelBounds = Rect.Empty;
        ReleaseStaleAltHold();
        ReleaseFilesDrag();
        // Cheap, and catches what events miss: the window shown before it has a width, a size
        // that settles after the last layout.
        UpdateRoom();
        var visible = _host.Session.IsMirroring && IsVisible && WindowState != WindowState.Minimized && Host.HasChild;
        if (visible)
        {
            foreach (var view in AllViews)
            {
                view.SyncChildShape();
            }
        }

        _overlay.ReleaseStuckDrags();
        // The overlay covers the whole mirror area: with copies the main view is only one cell of it.
        _overlay.Track(visible ? AreaScreenRect() : default, visible);
        _overlay.UpdateHud(_fullscreen && visible, Host.Zoom, _host.Config.Hud);
        if (visible)
        {
            UpdateNavigator();
        }
    }

    private void OnViewChanged()
    {
        var zoomed = Host.View.IsZoomed;
        if (zoomed)
        {
            ShowTipSoon(Tips.FirstZoom);
        }

        ZoomBadge.Visibility = zoomed ? Visibility.Visible : Visibility.Collapsed;
        ZoomText.Text = $"{Host.Zoom * 100:0}%";
        UpdateNavigator();
        ControlsPanel.Refresh();
    }

    private void UpdateNavigator()
    {
        var view = Host.View;
        var zoom = _host.Config.Zoom;
        var show = (view.IsZoomed || zoom.NavigatorAlways) && zoom.ShowNavigator && Host.HasChild;
        var aspect = view.SurfaceHeight > 0 ? view.SurfaceWidth / view.SurfaceHeight : 0.45;
        _overlay.UpdateNavigator(show, aspect, Host.VisibleFraction(), zoom);
        // A blurred wash is the opposite of what High Contrast is for, so it stands down there.
        var ambient = _host.Config.Ambient.Enabled && Host.HasChild && !SystemParameters.HighContrast;
        Ambient.Update(ambient, PicturesInArea(), _host.Config.Ambient);
        _ambientWanted = ambient;
        _previewWanted = show && zoom.NavigatorPicture;

        // One capture feeds both pictures, at the faster of the two rates that are in use.
        var rate = Math.Max(_ambientWanted ? _host.Config.Ambient.FrameRate : 0, _previewWanted ? zoom.NavigatorFrameRate : 0);
        var interval = TimeSpan.FromMilliseconds(1000 / Math.Max(1, rate));
        if (_ambientTimer.Interval != interval) _ambientTimer.Interval = interval;
        if ((_ambientWanted || _previewWanted) && IsVisible && WindowState != WindowState.Minimized && !_fullscreenTransition)
        {
            if (!_ambientTimer.IsEnabled) _ambientTimer.Start();
        }
        else
        {
            _ambientTimer.Stop();
        }

        if (!_ambientWanted || !_ambientTimer.IsEnabled)
        {
            Ambient.SetFrame(null);
        }

        if (!_previewWanted || !_ambientTimer.IsEnabled)
        {
            _overlay.SetNavigatorFrame(null);
        }
    }

    /// <summary>
    /// One frame of the visible mirror surface for the soft background, taken on a worker thread so
    /// the window stays responsive, and never from the phone.
    /// </summary>
    private async void CaptureAmbientFrame()
    {
        if (!Host.HasChild || !_host.Session.IsMirroring)
        {
            return;
        }

        var surface = Host.SurfaceScreenRect;
        var viewport = Host.ViewportScreenRect;
        var visible = new RECT
        {
            Left = Math.Max(surface.Left, viewport.Left),
            Top = Math.Max(surface.Top, viewport.Top),
            Right = Math.Min(surface.Right, viewport.Right),
            Bottom = Math.Min(surface.Bottom, viewport.Bottom),
        };

        var previewWidth = _previewWanted ? _overlay.NavigatorPreviewPixelWidth : 0;
        var result = await _liveCapture.CaptureAsync(visible, _host.Config.Ambient.Blur, _ambientWanted, previewWidth);
        if (result is null || _quitting || !_ambientTimer.IsEnabled)
        {
            return;
        }

        if (result.Ambient is { } ambient && _ambientWanted)
        {
            Ambient.SetFrame(ambient);
        }

        if (result.Preview is { } preview && _previewWanted)
        {
            _overlay.SetNavigatorFrame(preview);
        }
    }

    // ----- Actions -----

    public async Task<AndroidResult> ApplyAppActionAsync(string id)
    {
        switch (id)
        {
            case "browse":
                SetBrowse(!_browse);
                return AndroidResult.Success(_browse ? "Browse mode on" : "Browse mode off");
            case var gesture when MirrorActions.IsGesture(gesture):
                return await _keyboardTouch.RunAsync(gesture);
            case "zoom-in":
                Host.ZoomStep(1, step: 0.25);
                return AndroidResult.Success($"{Host.Zoom * 100:0}%");
            case "zoom-out":
                Host.ZoomStep(-1, step: 0.25);
                return AndroidResult.Success($"{Host.Zoom * 100:0}%");
            case "zoom-reset":
                Host.ResetZoom();
                return AndroidResult.Success("100%");
            case "fullscreen":
                ToggleFullscreen();
                return AndroidResult.Success(_fullscreen ? "Fullscreen" : "Windowed");
            case "screenshot":
                _ = SaveScreenshotAsync();
                return AndroidResult.Success("Saving…");
            case "copy-add":
                return Copies.Add();
            case "copy-remove":
                return Copies.Remove();
            case "send-files":
                await SendFilesAsync();
                return AndroidResult.Success();
            case "send-copied-files":
                await SendCopiedFilesAsync();
                return AndroidResult.Success();
            case "sound-up" or "sound-down" or "sound-mute":
                return RunSound(id);
            default:
                return AndroidResult.Failure($"Unknown app action '{id}'.");
        }
    }

    public async Task RunActionAsync(string id)
    {
        await RunActionCoreAsync(id);
        // Back to whichever view was being used, the main one or a copy.
        if (ActiveView.HasChild)
        {
            ActiveView.FocusChild();
        }
    }

    /// <summary>Plays an action and shows what it did in the status bar; returns the same words.</summary>
    private async Task<(bool Ok, string Text)> RunActionCoreAsync(string id)
    {
        var result = await _host.Session.RunActionAsync(id, ApplyAppActionAsync);
        NoteAction(id, result.Ok);
        var text = result.Ok ? (string.IsNullOrWhiteSpace(result.Text) ? MirrorActions.Find(id)?.Label ?? id : result.Text) : result.Text;
        SetStatus(text, !result.Ok);
        return (result.Ok, text);
    }

    private async Task<(bool Ok, string Text)> SaveScreenshotAsync()
    {
        try { return await SaveScreenshotCoreAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _host.Log.Error("Could not save a screenshot", ex);
            SetStatus("Could not save screenshot: " + ex.Message, isError: true);
            return (false, "Could not save screenshot: " + ex.Message);
        }
    }

    private async Task<(bool Ok, string Text)> SaveScreenshotCoreAsync()
    {
        var (ok, text) = await _host.Session.SaveScreenshotAsync();
        var said = ok ? "Screenshot saved: " + Path.GetFileName(text) : text;
        SetStatus(said, !ok);
        if (ok)
        {
            StatusText.Inlines.Clear();
            StatusText.Inlines.Add("Screenshot saved: ");
            var link = new System.Windows.Documents.Hyperlink(
                new System.Windows.Documents.Run(Path.GetFileName(text)))
            {
                Foreground = (Brush)FindResource("Signal"),
                ToolTip = "Show in File Explorer: " + text,
            };
            link.Click += (_, _) => RevealScreenshot(text);
            StatusText.Inlines.Add(link);
        }

        return (ok, said);
    }

    private void RevealScreenshot(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                SetStatus("This screenshot has been moved or deleted.", isError: true);
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _host.Log.Error($"Could not reveal screenshot '{path}' in File Explorer", ex);
            SetStatus("Could not open File Explorer: " + ex.Message, isError: true);
        }
    }

    public void SetStatus(string text, bool isError = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(isError ? "Live" : "Muted");
        if (_fullscreen) _overlay.RevealHud(text);
    }

    public void RefreshAll()
    {
        OnSessionChanged();
        PhonePanel.Refresh(force: true);
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            if (enabled)
            {
                StartupRegistration.Enable(_host.ExecutablePath);
            }
            else
            {
                StartupRegistration.Disable();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _host.Log.Error($"Could not turn Start with Windows {(enabled ? "on" : "off")}", ex);
            SetStatus("Could not change Windows startup: " + ex.Message, true);
        }

        SettingsPanel.Refresh();
        _host.Tray?.Refresh();
    }

    public PatternGuide? Guide => _guide;

    private async void OnQuickAction(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            await RunActionAsync(id);
        }
    }

    private void OnLockAnswer(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string answer })
        {
            return;
        }

        if (answer == "later")
        {
            NoticeBar.Visibility = Visibility.Collapsed;
            return;
        }

        _host.Session.AnswerLockQuestion(answer);
        StartPatternGuideIfNeeded();
        OnSessionChanged();
    }

    private void OnEmptyPrimary(object sender, RoutedEventArgs e) => _host.Session.StartAgain();

    private async void OnRepairUsb(object sender, RoutedEventArgs e) => await RepairUsbAsync();

    /// <summary>
    /// Runs "rex usb repair" through the administrator prompt: it registers ADB interfaces Windows
    /// bound without the ADB class and resets USB devices Windows could not read.
    /// </summary>
    public async Task RepairUsbAsync()
    {
        EmptyRepair.IsEnabled = false;
        SetStatus("Waiting for administrator approval…");
        try
        {
            var (ok, message) = await _host.Usb.RepairAsync();
            SetStatus(message, isError: !ok);
        }
        finally
        {
            EmptyRepair.IsEnabled = true;
        }
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
