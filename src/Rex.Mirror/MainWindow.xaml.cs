using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
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
    private PatternGuide? _guide;
    private HwndSource? _source;
    private bool _fullscreen;
    private bool _sidebarWanted = true;
    private bool _quitting;
    private bool _trayHintShown;
    private double _navigatorStartZoom;
    private bool _previewBusy;
    private DateTime _nextPreview;
    private WindowState _restoreState = WindowState.Normal;
    private Rect _windowedBounds;
    public bool IsFullscreen => _fullscreen;
    public double SidebarScrollOffset => SidebarScroll.VerticalOffset;
    public bool PreviewAvailable => _overlay.PreviewAvailable;
    public string SidebarTab => CurrentTab();
    private Rect _sidebarWheelBounds = Rect.Empty;
    public bool HudVisible => _overlay.HudVisible;
    public bool OnboardingVisible => Onboarding.Visibility == Visibility.Visible;
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
        _touchpad = new TouchpadBridge(Host, _injector, () => _host.Config, host.Log.Warn);

        _overlayTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _overlayTimer.Tick += (_, _) => TrackOverlay();

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

    private void ApplyPlacement()
    {
        var placement = _host.State.Ui;
        if (placement.Width >= 600 && placement.Height >= 400)
        {
            Width = placement.Width;
            Height = placement.Height;
            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (virtualScreen.Contains(new Point(placement.Left + 40, placement.Top + 40)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = placement.Left;
                Top = placement.Top;
            }

            if (placement.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
    }

    private void SavePlacement()
    {
        if (_fullscreen)
        {
            return;
        }

        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _host.State.SetUi(_host.State.Ui with
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
            SidebarVisible = _sidebarWanted,
            SidebarTab = CurrentTab(),
        });
    }

    // ----- Session events -----

    private void OnSessionChanged()
    {
        var session = _host.Session;
        SettingsPanel.RefreshRestartNotice();
        var mirroring = session.IsMirroring;
        var needsSetup = session.Phase == SessionPhase.NeedsSetup;
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
            DeviceName.Text = DeviceStateText.Header(needsSetup, session.Devices);
            DeviceMeta.Text = string.Empty;
        }

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
                _ => "Connect your phone",
            };
            EmptyText.Text = session.Phase == SessionPhase.Waiting && session.Devices.Count == 0
                ? "Plug in an Android phone with USB debugging turned on. It appears here automatically."
                : session.Message;
            EmptyPrimary.Visibility = session.Phase == SessionPhase.Stopped ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var button in new[] { QuickHome, QuickBack, QuickRecents, QuickScreenshot })
        {
            button.IsEnabled = session.Devices.Any(d => d.IsReady);
        }

        QuickSleep.IsEnabled = mirroring;

        var question = session.PendingLockQuestionSerial is not null && _host.Config.PatternGuide.Enabled;
        NoticeBar.Visibility = question && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
        if (question && session.Identity is not null)
        {
            NoticeTitle.Text = $"How does {session.Identity.DisplayName} unlock?";
        }

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
        _overlay.Track(visible ? Host.ViewportScreenRect : default, visible);
        _overlay.UpdateHud(_fullscreen && visible, Host.Zoom);
        if (visible)
        {
            UpdateNavigator();
        }
    }

    private void OnViewChanged()
    {
        var zoomed = Host.View.IsZoomed;
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
        _overlay.UpdateNavigator(show, aspect, Host.VisibleFraction());
        var ambient = _host.Config.App.AmbientBackground && Host.HasChild;
        _overlay.UpdateAmbient(ambient, Host.SurfaceRect, _host.Config.App);
        if ((show || ambient) && IsVisible && WindowState != WindowState.Minimized && !_previewBusy && DateTime.UtcNow >= _nextPreview)
            _ = RefreshNavigatorPreviewAsync();
        if (!show && !ambient) _overlay.SetNavigatorPreview(null);
    }

    private async Task RefreshNavigatorPreviewAsync()
    {
        var device = _host.Session.ActiveDevice;
        var adb = _host.Session.Adb;
        if (device is null || adb is null) return;
        _previewBusy = true;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var bytes = await adb.ScreencapAsync(device.Serial, deadline.Token);
            if (bytes is null || _quitting || !_host.Session.IsMirroring || _host.Session.ActiveDevice?.Serial != device.Serial) return;
            using var stream = new MemoryStream(bytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 180;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            _overlay.SetNavigatorPreview(bitmap);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException or OperationCanceledException)
        {
            _overlay.SetNavigatorPreview(null);
        }
        finally { _previewBusy = false; _nextPreview = DateTime.UtcNow.AddSeconds(_host.Config.App.PreviewIntervalSeconds); }
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

    private void OnToggleSidebar(object sender, RoutedEventArgs e) => SetSidebarVisible(Sidebar.Visibility != Visibility.Visible);

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void SetSidebarVisible(bool visible)
    {
        _sidebarWanted = visible;
        Sidebar.Visibility = visible && !_fullscreen ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = visible && !_fullscreen ? new GridLength(330) : new GridLength(0);
    }

    public void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            _restoreState = WindowState;
            _windowedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            // Use the monitor bounds, including the taskbar area, in physical pixels.
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HWND_TOP,
                screen.Left, screen.Top, screen.Width, screen.Height, NativeMethods.SWP_FRAMECHANGED);
            TopBar.Visibility = Visibility.Collapsed;
            NoticeBar.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            Sidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            Host.ResetZoom();
            _overlay.RevealHud("Top edge shows controls · Esc exits fullscreen");
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Left = _windowedBounds.Left;
            Top = _windowedBounds.Top;
            Width = _windowedBounds.Width;
            Height = _windowedBounds.Height;
            WindowState = _restoreState == WindowState.Minimized ? WindowState.Normal : _restoreState;
            TopBar.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            SetSidebarVisible(_sidebarWanted);
            OnSessionChanged();
        }

        UpdateLayout();
        TrackOverlay();

        if (Host.HasChild)
        {
            Host.FocusChild();
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
        if (tab == "phone")
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
