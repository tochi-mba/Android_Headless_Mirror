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
    private readonly InputHooks _hooks = new();
    private readonly DispatcherTimer _overlayTimer;
    private readonly DispatcherTimer _ambientTimer;
    private readonly LiveCapture _liveCapture = new();
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
    public bool AmbientFrameAvailable => _overlay.AmbientFrameAvailable;
    public string SidebarTab => CurrentTab();
    private Rect _sidebarWheelBounds = Rect.Empty;
    public bool HudVisible => _overlay.HudVisible;
    public RectD HudBarRect => _overlay.HudBarRect;
    public bool OnboardingVisible => Onboarding.Visibility == Visibility.Visible;
    public bool SidebarVisible => Sidebar.Visibility == Visibility.Visible;
    public double SidebarWidthDip => Math.Round(SidebarColumn.ActualWidth);
    public bool AmbientVisible => _overlay.AmbientVisible;
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

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Host.MaxZoom = host.Config.Zoom.MaxZoom;

        _overlay = new OverlayWindow(this);
        _overlay.HudActionRequested += async id => await RunActionAsync(id);
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
        };

        _overlayTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _overlayTimer.Tick += (_, _) => TrackOverlay();
        _ambientTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1000 / 15.0) };
        _ambientTimer.Tick += (_, _) => CaptureAmbientFrame();

        Host.ViewChanged += _ => OnViewChanged();
        Host.ChildFocused += () => _overlay.ClearTrail();

        // WPF IsActive can lag when focus crosses into the embedded scrcpy HWND.
        // Route global hooks using the actual foreground window's root instead.
        _hooks.IsActive = () => _source is not null && IsVisible && WindowState != WindowState.Minimized &&
            NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2) == _source.Handle;
        _hooks.AltWheel = OnAltWheel;
        _hooks.PanelWheel = OnPanelWheel;
        _hooks.KeyDown = OnHotkey;

        ControlsPanel.Attach(this, host);
        PhonePanel.Attach(this, host);
        SettingsPanel.Attach(this, host);
        InfoPanel.Attach(this, host);
        Onboarding.Attach(this, host);

        host.Session.Changed += OnSessionChanged;
        host.Session.MirrorReady += OnMirrorReady;
        host.Session.MirrorEnded += OnMirrorEnded;
        host.Session.LaunchRect = LaunchRect;
        host.ConfigChanged += OnConfigChanged;
        host.ConfigPreviewed += () => { Host.MaxZoom = _host.Config.Zoom.MaxZoom; TrackOverlay(); };

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            _host.Log.Info("DPI awareness: " + DpiAwareness.Describe(_source.Handle));
            NativeMethods.ApplyDarkTitleBar(_source.Handle);
            _source.AddHook(WindowHook);
            NativeMethods.TryRegisterTouchpadWindow(_source.Handle, true);
        };
        Loaded += (_, _) => { _hooks.Install(); OnSessionChanged(); };
        LocationChanged += (_, _) => TrackOverlay();
        SizeChanged += (_, _) => TrackOverlay();
        StateChanged += (_, _) => TrackOverlay();
        Activated += (_, _) => { if (Host.HasChild) { Host.FocusChild(); } };
        Deactivated += (_, _) => _overlay.ClearTrail();
        Closing += OnClosing;

        ApplyPlacement();
        SetSidebarVisible(host.State.Ui.SidebarVisible);
        SelectTab(host.State.Ui.SidebarTab);
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
        var session = _host.Session;
        SettingsPanel.RefreshRestartNotice();
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
            : session.Identity.DisplayName + " — Android Headless Mirror";

        Onboarding.Visibility = onboarding ? Visibility.Visible : Visibility.Collapsed;
        MirrorArea.Visibility = onboarding ? Visibility.Collapsed : Visibility.Visible;
        Sidebar.Visibility = onboarding ? Visibility.Collapsed : (_sidebarWanted && !_fullscreen ? Visibility.Visible : Visibility.Collapsed);
        Host.SetShown(mirroring && !onboarding);
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
        else if (_tipShowing is null || _fullscreen)
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

        if (session.Devices.Count > 1)
        {
            ShowTipOnce(Tips.SecondPhone);
        }

        HintText.Text = session.IsMirroring
            ? $"{Shortcuts.Gesture("host-zoom")} zoom · {Shortcuts.Gesture("host-pan")} pan · {Shortcuts.Gesture("fullscreen")} fullscreen"
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
        var session = _host.Session;
        if (session.Identity is { DisplayWidth: > 0, DisplayHeight: > 0 } identity)
        {
            Host.SetVideoSize(identity.DisplayWidth, identity.DisplayHeight);
        }

        Host.SetShown(true);
        Host.Attach(scrcpy.Hwnd, scrcpy.ThreadId, (uint)scrcpy.ProcessId);
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

        OnSessionChanged();
        TrackOverlay();
    }

    private void OnMirrorEnded()
    {
        _touchpad.Cancel();
        _ambientTimer.Stop();
        _overlay.SetAmbientFrame(null);
        _guide?.Dispose();
        _guide = null;
        Host.Detach();
        Host.SetShown(false);
        if (_fullscreen) ToggleFullscreen();
        _overlayTimer.Stop();
        _overlay.Track(default, visible: false);
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
        var visible = _host.Session.IsMirroring && IsVisible && WindowState != WindowState.Minimized && Host.HasChild;
        _overlay.ReleaseStuckDrags();
        _overlay.Track(visible ? Host.ViewportScreenRect : default, visible);
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
        var show = view.IsZoomed && _host.Config.Zoom.ShowNavigator && Host.HasChild;
        var aspect = view.SurfaceHeight > 0 ? view.SurfaceWidth / view.SurfaceHeight : 0.45;
        _overlay.UpdateNavigator(show, aspect, Host.VisibleFraction(), _host.Config.Zoom);
        // A blurred wash is the opposite of what High Contrast is for, so it stands down there.
        var ambient = _host.Config.Ambient.Enabled && Host.HasChild && !SystemParameters.HighContrast;
        _overlay.UpdateAmbient(ambient, Host.SurfaceRect, _host.Config.Ambient);
        var interval = TimeSpan.FromMilliseconds(1000 / _host.Config.Ambient.FrameRate);
        if (_ambientTimer.Interval != interval) _ambientTimer.Interval = interval;
        if (ambient && IsVisible && WindowState != WindowState.Minimized && !_fullscreenTransition)
        {
            if (!_ambientTimer.IsEnabled) _ambientTimer.Start();
        }
        else
        {
            _ambientTimer.Stop();
            _overlay.SetAmbientFrame(null);
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

        var frame = await _liveCapture.CaptureAsync(visible, _host.Config.Ambient.Blur);
        if (frame is not null && !_quitting && _ambientTimer.IsEnabled)
        {
            _overlay.SetAmbientFrame(frame);
        }
    }

    private bool OnPanelWheel(int delta, int screenX, int screenY)
    {
        if (!SidebarScroll.IsVisible) return false;
        if (!_sidebarWheelBounds.Contains(screenX, screenY))
            return false;
        // scrcpy can retain native keyboard focus even when the pointer is over WPF.
        // Consume this event before Windows delivers it to the focused child HWND.
        Dispatcher.BeginInvoke(() =>
        {
            var distance = SystemParameters.WheelScrollLines < 0
                ? SidebarScroll.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            SidebarScroll.ScrollToVerticalOffset(SidebarScroll.VerticalOffset - delta / 120.0 * distance);
        });
        return true;
    }

    private bool OnAltWheel(int delta, int screenX, int screenY)
    {
        if (!_host.Config.Zoom.Enabled || !_host.Config.Zoom.WheelZoom || !Host.HasChild)
        {
            return false;
        }

        var viewport = Host.ViewportScreenRect;
        if (screenX < viewport.Left || screenX >= viewport.Right || screenY < viewport.Top || screenY >= viewport.Bottom)
        {
            return false;
        }

        var direction = delta > 0 ? 1 : -1;
        var anchorX = screenX - viewport.Left;
        var anchorY = screenY - viewport.Top;
        var step = _host.Config.Zoom.WheelStep;
        Dispatcher.BeginInvoke(() => Host.ZoomStep(direction, anchorX, anchorY, step));
        return true;
    }

    private bool OnHotkey(int virtualKey, bool ctrl, bool alt, bool shift)
    {
        var guide = _guide;
        if (guide is { IsCalibrating: true } && !alt && guide.CanHandleCalibrationKey(virtualKey))
        {
            var plainCalibrationKey = !ctrl;
            var fineArrowKey = ctrl && virtualKey is NativeMethods.VK_LEFT or NativeMethods.VK_UP or NativeMethods.VK_RIGHT or NativeMethods.VK_DOWN;
            if (plainCalibrationKey || fineArrowKey)
            {
                Dispatcher.BeginInvoke(() => guide.HandleCalibrationKey(virtualKey, ctrl, shift));
                return true;
            }
        }

        switch (virtualKey)
        {
            case NativeMethods.VK_F11:
                Dispatcher.BeginInvoke(ToggleFullscreen);
                return true;
            case NativeMethods.VK_ESCAPE when _fullscreen:
                Dispatcher.BeginInvoke(ToggleFullscreen);
                return true;
            case 'L' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("rotation-landscape"));
                return true;
            case 'U' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("rotation-portrait"));
                return true;
            case 'A' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("rotation-auto"));
                return true;
            case NativeMethods.VK_LEFT when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("rotate-left"));
                return true;
            case NativeMethods.VK_RIGHT when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("rotate-right"));
                return true;
            case 'P' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _guide?.Toggle());
                return true;
            case 'C' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _guide?.StartCalibration());
                return true;
            case NativeMethods.VK_F1:
                Dispatcher.BeginInvoke(StartTour);
                return true;
            case 'S' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("screenshot"));
                return true;
            case 'H' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("home"));
                return true;
            case 'B' when ctrl && alt:
                Dispatcher.BeginInvoke(() => SetSidebarVisible(!_sidebarWanted));
                return true;
            case >= '1' and <= '4' when ctrl && alt:
                var tab = virtualKey - '1';
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_sidebarWanted)
                    {
                        SetSidebarVisible(true);
                    }

                    SelectTab(TabOrder[tab]);
                });
                return true;
            case '0' when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("zoom-reset"));
                return true;
            case NativeMethods.VK_OEM_PLUS when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("zoom-in"));
                return true;
            case NativeMethods.VK_OEM_MINUS when ctrl && alt:
                Dispatcher.BeginInvoke(() => _ = RunActionAsync("zoom-out"));
                return true;
            default:
                return false;
        }
    }

    private bool OnOverlayPointer(int msg, IntPtr wParam) => _touchpad.HandlePointerMessage(msg, wParam);

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_POINTERDOWN or NativeMethods.WM_POINTERUPDATE or NativeMethods.WM_POINTERUP or NativeMethods.WM_POINTERCAPTURECHANGED)
        {
            if (_touchpad.HandlePointerMessage(msg, wParam))
            {
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    // ----- Actions -----

    public AndroidResult ApplyAppAction(string id)
    {
        switch (id)
        {
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
            default:
                return AndroidResult.Failure($"Unknown app action '{id}'.");
        }
    }

    public async Task RunActionAsync(string id)
    {
        var result = await _host.Session.RunActionAsync(id, ApplyAppAction);
        SetStatus(result.Ok ? (string.IsNullOrWhiteSpace(result.Text) ? MirrorActions.Find(id)?.Label ?? id : result.Text) : result.Text, !result.Ok);
        if (Host.HasChild)
        {
            Host.FocusChild();
        }
    }

    private async Task SaveScreenshotAsync()
    {
        try { await SaveScreenshotCoreAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Could not save screenshot: " + ex.Message, isError: true);
        }
    }

    private async Task SaveScreenshotCoreAsync()
    {
        var (ok, text) = await _host.Session.SaveScreenshotAsync();
        SetStatus(ok ? "Screenshot saved: " + Path.GetFileName(text) : text, !ok);
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

    /// <summary>Runs "rex usb repair" elevated; Windows shows the UAC prompt, the exit code tells the outcome.</summary>
    public async Task RepairUsbAsync()
    {
        var cli = Path.Combine(AppContext.BaseDirectory, "rex.exe");
        if (!File.Exists(cli))
        {
            SetStatus("rex.exe was not found next to the app, so the repair cannot run.", isError: true);
            return;
        }

        EmptyRepair.IsEnabled = false;
        SetStatus("Waiting for administrator approval…");
        try
        {
            using var repair = Process.Start(new ProcessStartInfo(cli, "usb repair") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })
                ?? throw new InvalidOperationException("Windows did not start the repair.");
            await repair.WaitForExitAsync();
            SetStatus(repair.ExitCode == 0
                ? "USB driver registered. The phone should appear in a moment; unplug and plug it in again if it does not."
                : "The repair did not finish. Run 'rex usb repair' in an administrator terminal to see why.", repair.ExitCode != 0);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // ERROR_CANCELLED (1223) is the user declining the UAC prompt.
            SetStatus(ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 } ? "Repair cancelled." : "Could not start the repair: " + ex.Message, isError: true);
        }
        finally
        {
            EmptyRepair.IsEnabled = true;
        }
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (ControlsPanel is null)
        {
            return;
        }

        var tab = CurrentTab();
        ControlsPanel.Visibility = tab == "controls" ? Visibility.Visible : Visibility.Collapsed;
        PhonePanel.Visibility = tab == "phone" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = tab == "info" ? Visibility.Visible : Visibility.Collapsed;
        if (tab == "controls")
        {
            _ = ControlsPanel.RefreshRotationAsync();
        }
        else if (tab == "phone")
        {
            PhonePanel.Refresh();
        }
        else if (tab == "settings")
        {
            SettingsPanel.Refresh();
        }
        else if (tab == "info")
        {
            InfoPanel.Refresh();
        }
    }

    private string CurrentTab() =>
        TabPhone.IsChecked == true ? "phone" : TabSettings.IsChecked == true ? "settings" : TabInfo.IsChecked == true ? "info" : "controls";

    /// <summary>The tabs in the order Ctrl+Alt+1 to 4 reach them.</summary>
    private static readonly string[] TabOrder = ["controls", "phone", "settings", "info"];

    private void SelectTab(string tab)
    {
        (tab switch
        {
            "phone" => TabPhone,
            "settings" => TabSettings,
            "info" => TabInfo,
            _ => TabControls,
        }).IsChecked = true;
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
