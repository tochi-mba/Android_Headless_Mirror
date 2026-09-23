using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror;

/// <summary>
/// The shape of the window: where it opens, how wide the side panel is, and what fullscreen does
/// to both. None of it is about mirroring, and all of it is remembered between launches.
/// </summary>
public partial class MainWindow
{
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

    private void OnToggleSidebar(object sender, RoutedEventArgs e) => SetSidebarVisible(Sidebar.Visibility != Visibility.Visible);

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    /// <summary>The side panel's width when it has never been dragged.</summary>
    private const double DefaultSidebarWidth = 330;

    private void SetSidebarVisible(bool visible)
    {
        _sidebarWanted = visible;
        var shown = visible && !_fullscreen;
        Sidebar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = shown ? new GridLength(SidebarWidth()) : new GridLength(0);
        SidebarColumn.MinWidth = shown ? 280 : 0;
    }

    private double SidebarWidth()
    {
        var saved = _host.State.Ui.SidebarWidth;
        return saved >= 280 && saved <= 560 ? saved : DefaultSidebarWidth;
    }

    private void OnSidebarResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (Sidebar.Visibility != Visibility.Visible)
        {
            return;
        }

        _host.State.SetUi(_host.State.Ui with { SidebarWidth = Math.Round(SidebarColumn.ActualWidth) });
    }

    private void OnSidebarSplitterDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SidebarColumn.Width = new GridLength(DefaultSidebarWidth);
        _host.State.SetUi(_host.State.Ui with { SidebarWidth = 0 });
        e.Handled = true;
    }

    public void ToggleFullscreen()
    {
        _fullscreenTransition = true;
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
            // Through the same path as everywhere else: the column has a minimum width of its own
            // now, and zeroing only its width would leave that minimum holding the mirror back.
            SetSidebarVisible(_sidebarWanted);
            Host.ResetZoom();
            _overlay.RevealHud(MarkTipSeen(Tips.FirstFullscreen) && Tips.Find(Tips.FirstFullscreen) is { } firstTime
                ? firstTime.Text
                : "Top edge shows controls · Esc exits fullscreen");
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
        _fullscreenTransition = false;
        TrackOverlay();

        if (Host.HasChild)
        {
            Host.FocusChild();
        }
    }
}
