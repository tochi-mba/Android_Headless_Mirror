using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// A transparent, click-through layer that sits exactly over the mirror viewport. WPF cannot
/// draw over an embedded window (airspace), so this owned window carries the pattern guide,
/// the zoom navigator and the touchpad contact receiver. It never takes focus: the navigator
/// and Alt-drag panning are the only parts that accept the mouse.
/// </summary>
public sealed class OverlayWindow : Window
{
    private const double NavigatorWidth = 150;
    private const double NavigatorMargin = 12;

    private readonly Canvas _canvas = new();
    private readonly Canvas _pattern = new() { IsHitTestVisible = false };
    private readonly Polyline _trail = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x4D)),
        StrokeThickness = 3,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };
    private readonly TextBlock _label = new()
    {
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
        IsHitTestVisible = false,
        Opacity = 0.85,
    };
    private readonly Border _navigator = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x08, 0x0A, 0x09)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x54, 0x4B)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(6),
        Cursor = Cursors.Cross,
        Visibility = Visibility.Collapsed,
    };
    private readonly Canvas _navigatorCanvas = new() { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x15, 0x11)) };
    private readonly Rectangle _navigatorViewport = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xD7, 0xFF, 0x3F)),
        IsHitTestVisible = false,
    };

    private HwndSource? _source;
    private bool _navigatorDragging;
    private bool _panning;
    private Point _panLast;
    private double _dpiScale = 1.0;
    private Rect _navigatorScreenRect;

    private readonly Window _owner;
    private readonly FullscreenHudWindow _hudWindow;
    private RECT _screenPixels;

    public event Action<string>? HudActionRequested;
    public bool HudVisible => IsVisible && _hudWindow.HudVisible;
    public void RevealHud(string? message = null) => _hudWindow.Reveal(message);

    public void UpdateHud(bool fullscreen, double zoom)
    {
        var atTop = false;
        if (fullscreen && Handle != IntPtr.Zero && NativeMethods.GetCursorPos(out var cursor))
        {
            NativeMethods.ScreenToClient(Handle, ref cursor);
            atTop = cursor.Y >= 0 && cursor.Y <= 12 * _dpiScale &&
                cursor.X >= 0 && cursor.X < Width * _dpiScale;
        }

        _hudWindow.Update(fullscreen && IsVisible, atTop, zoom, _screenPixels, _dpiScale);
    }

    public OverlayWindow(Window owner)
    {
        _owner = owner;
        _hudWindow = new FullscreenHudWindow(this);
        _hudWindow.ActionRequested += id => HudActionRequested?.Invoke(id);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = false;
        Width = 10;
        Height = 10;

        _canvas.Children.Add(_pattern);
        _canvas.Children.Add(_trail);
        _canvas.Children.Add(_label);
        Canvas.SetLeft(_label, 12);
        Canvas.SetTop(_label, 10);

        _navigatorCanvas.Children.Add(_navigatorViewport);
        _navigator.Child = _navigatorCanvas;
        _canvas.Children.Add(_navigator);
        _navigator.MouseLeftButtonDown += OnNavigatorDown;
        _navigator.MouseMove += OnNavigatorMove;
        _navigator.MouseLeftButtonUp += OnNavigatorUp;

        _canvas.MouseLeftButtonDown += OnPanDown;
        _canvas.MouseMove += OnPanMove;
        _canvas.MouseLeftButtonUp += OnPanUp;
        _canvas.Background = Brushes.Transparent;
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            var exStyle = NativeMethods.GetStyle(_source.Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetStyle(_source.Handle, NativeMethods.GWL_EXSTYLE,
                (exStyle & ~NativeMethods.WS_EX_TRANSPARENT) | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            _source.AddHook(WndProcHook);
            _dpiScale = _source.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            TouchpadRegistered = NativeMethods.TryRegisterTouchpadWindow(_source.Handle, true);
        };
        Loaded += (_, _) =>
        {
            _source?.RemoveHook(WndProcHook);
            _source?.AddHook(WndProcHook);
        };
    }

    public IntPtr Handle => _source?.Handle ?? IntPtr.Zero;
    public bool TouchpadRegistered { get; private set; }

    /// <summary>Raised for WM_POINTER* messages so the touchpad bridge can inspect contacts.</summary>
    public event Func<int, IntPtr, bool>? PointerMessage;

    /// <summary>Raised while Alt-dragging with pixel deltas.</summary>
    public event Action<double, double>? PanDelta;

    /// <summary>Raised when the navigator is clicked/dragged, with a normalized surface point.</summary>
    public event Action<double, double>? NavigatorTarget;

    /// <summary>True while the host may pan: Alt is down and the view is zoomed.</summary>
    public Func<bool>? PanAllowed { get; set; }

    // ----- Positioning -----

    /// <summary>Places the overlay exactly over a screen-pixel rectangle.</summary>
    public void Track(RECT screenPixels, bool visible)
    {
        if (_source is null)
        {
            if (!visible || !_owner.IsVisible)
            {
                return;
            }

            Owner = _owner;
            new WindowInteropHelper(this).EnsureHandle();
        }

        var source = _source;
        if (source is null)
        {
            return;
        }

        if (!visible || screenPixels.Width <= 0 || screenPixels.Height <= 0)
        {
            _screenPixels = default;
            _hudWindow.Update(false, false, 1, default, _dpiScale);
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (!IsVisible)
        {
            Owner ??= _owner;
            Show();
        }

        var style = NativeMethods.GetStyle(source.Handle, NativeMethods.GWL_EXSTYLE);
        if ((style & NativeMethods.WS_EX_TRANSPARENT) != 0)
            NativeMethods.SetStyle(source.Handle, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_TRANSPARENT);

        NativeMethods.SetWindowPos(source.Handle, IntPtr.Zero, screenPixels.Left, screenPixels.Top, screenPixels.Width, screenPixels.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSENDCHANGING);
        _screenPixels = screenPixels;
        _dpiScale = source.CompositionTarget?.TransformToDevice.M11 ?? _dpiScale;
        Width = screenPixels.Width / _dpiScale;
        Height = screenPixels.Height / _dpiScale;
        _canvas.Width = Width;
        _canvas.Height = Height;
    }

    public double DpiScale => _dpiScale;

    // ----- Pattern guide drawing -----

    public void ClearPattern()
    {
        _pattern.Children.Clear();
        _label.Text = string.Empty;
        _trail.Points.Clear();
    }

    /// <summary>Draws nine dots (in overlay pixel coordinates) plus an optional calibration box.</summary>
    public void DrawPattern(IReadOnlyList<PointD> pointsPixels, double radiusPixels, double opacity, string label, bool calibrating)
    {
        _pattern.Children.Clear();
        var scale = 1.0 / _dpiScale;
        var radius = Math.Max(5, radiusPixels * scale);
        var signal = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F));
        var live = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x4D));

        if (calibrating && pointsPixels.Count == 9)
        {
            var left = pointsPixels.Min(p => p.X) * scale;
            var top = pointsPixels.Min(p => p.Y) * scale;
            var box = new Rectangle
            {
                Width = Math.Max(1, pointsPixels.Max(p => p.X) * scale - left),
                Height = Math.Max(1, pointsPixels.Max(p => p.Y) * scale - top),
                Stroke = live,
                StrokeThickness = 1.5,
                StrokeDashArray = [5, 4],
                Opacity = 0.9,
            };
            Canvas.SetLeft(box, left);
            Canvas.SetTop(box, top);
            _pattern.Children.Add(box);
        }

        foreach (var point in pointsPixels)
        {
            var dot = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = signal,
                StrokeThickness = Math.Max(2, radius * 0.22),
                Fill = Brushes.Transparent,
                Opacity = opacity,
            };
            Canvas.SetLeft(dot, point.X * scale - radius);
            Canvas.SetTop(dot, point.Y * scale - radius);
            _pattern.Children.Add(dot);
        }

        _label.Text = label;
        _label.Foreground = calibrating ? live : signal;
    }

    public void AddTrailPoint(double xPixels, double yPixels) =>
        _trail.Points.Add(new Point(xPixels / _dpiScale, yPixels / _dpiScale));

    public void ClearTrail() => _trail.Points.Clear();

    // ----- Navigator -----

    public void UpdateNavigator(bool show, double surfaceAspect, RectD visibleFraction)
    {
        if (!show)
        {
            _navigator.Visibility = Visibility.Collapsed;
            _navigatorScreenRect = Rect.Empty;
            return;
        }

        var innerWidth = NavigatorWidth - 12;
        var innerHeight = Math.Clamp(innerWidth / Math.Max(0.1, surfaceAspect), 40, 220);
        _navigatorCanvas.Width = innerWidth;
        _navigatorCanvas.Height = innerHeight;
        _navigator.Width = NavigatorWidth;
        _navigator.Visibility = Visibility.Visible;

        Canvas.SetLeft(_navigatorViewport, visibleFraction.X * innerWidth);
        Canvas.SetTop(_navigatorViewport, visibleFraction.Y * innerHeight);
        _navigatorViewport.Width = Math.Max(4, visibleFraction.Width * innerWidth);
        _navigatorViewport.Height = Math.Max(4, visibleFraction.Height * innerHeight);

        var left = _canvas.Width - NavigatorWidth - NavigatorMargin;
        var top = _canvas.Height - innerHeight - 12 - NavigatorMargin;
        Canvas.SetLeft(_navigator, Math.Max(0, left));
        Canvas.SetTop(_navigator, Math.Max(0, top));
        _navigatorScreenRect = new Rect(Math.Max(0, left) * _dpiScale, Math.Max(0, top) * _dpiScale, NavigatorWidth * _dpiScale, (innerHeight + 12) * _dpiScale);
    }

    private void OnNavigatorDown(object sender, MouseButtonEventArgs e)
    {
        _navigatorDragging = true;
        _navigator.CaptureMouse();
        RaiseNavigator(e.GetPosition(_navigatorCanvas));
        e.Handled = true;
    }

    private void OnNavigatorMove(object sender, MouseEventArgs e)
    {
        if (_navigatorDragging)
        {
            RaiseNavigator(e.GetPosition(_navigatorCanvas));
            e.Handled = true;
        }
    }

    private void OnNavigatorUp(object sender, MouseButtonEventArgs e)
    {
        if (_navigatorDragging)
        {
            _navigatorDragging = false;
            _navigator.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void RaiseNavigator(Point point)
    {
        var fx = Math.Clamp(point.X / Math.Max(1, _navigatorCanvas.Width), 0, 1);
        var fy = Math.Clamp(point.Y / Math.Max(1, _navigatorCanvas.Height), 0, 1);
        NavigatorTarget?.Invoke(fx, fy);
    }

    // ----- Alt-drag panning -----

    private void OnPanDown(object sender, MouseButtonEventArgs e)
    {
        if (_navigatorDragging || PanAllowed?.Invoke() != true)
        {
            return;
        }

        _panning = true;
        _panLast = e.GetPosition(_canvas);
        _canvas.CaptureMouse();
        _canvas.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (!_panning)
        {
            return;
        }

        var current = e.GetPosition(_canvas);
        PanDelta?.Invoke((current.X - _panLast.X) * _dpiScale, (current.Y - _panLast.Y) * _dpiScale);
        _panLast = current;
        e.Handled = true;
    }

    private void OnPanUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            _canvas.ReleaseMouseCapture();
            _canvas.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    // ----- Win32 plumbing -----

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
            {
                handled = true;
                return new IntPtr(HitTest(lParam) ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
            }

            case NativeMethods.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_POINTERDOWN:
            case NativeMethods.WM_POINTERUPDATE:
            case NativeMethods.WM_POINTERUP:
            case NativeMethods.WM_POINTERCAPTURECHANGED:
                if (PointerMessage?.Invoke(msg, wParam) == true)
                {
                    handled = true;
                    return IntPtr.Zero;
                }

                break;
        }

        return IntPtr.Zero;
    }

    private bool HitTest(IntPtr lParam)
    {
        if (_panning || _navigatorDragging)
        {
            return true;
        }

        var x = (short)(lParam.ToInt64() & 0xFFFF);
        var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var point = new POINT { X = x, Y = y };
        NativeMethods.ScreenToClient(Handle, ref point);

        if (_navigator.Visibility == Visibility.Visible && _navigatorScreenRect.Contains(point.X, point.Y))
        {
            return true;
        }

        return PanAllowed?.Invoke() == true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hudWindow.Close();
        if (_source is not null)
        {
            if (TouchpadRegistered)
            {
                NativeMethods.TryRegisterTouchpadWindow(_source.Handle, false);
            }

            _source.RemoveHook(WndProcHook);
        }

        base.OnClosed(e);
    }
}
