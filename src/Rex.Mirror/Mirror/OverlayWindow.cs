using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
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
    private const double NavigatorMargin = 12;

    private readonly Canvas _canvas = new();
    private readonly Image _ambient = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private WriteableBitmap? _ambientBitmap;
    private readonly Canvas _ambientContainer = new() { IsHitTestVisible = false, ClipToBounds = true };
    private readonly Rectangle _tint = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    public bool AmbientVisible => _ambientContainer.Visibility == Visibility.Visible && _ambient.Source is not null;
    public bool NavigatorVisible => _navigator.Visibility == Visibility.Visible;

    /// <summary>
    /// Everything the soft background's layout depends on. The window redraws whenever any of its
    /// content changes, and this is a transparent window, so a redraw costs a full-window blit.
    /// Rebuilding the same geometry every tick paid that cost thirty times a second for a picture
    /// that had not moved; comparing this first means the work happens only when something changed.
    /// </summary>
    private readonly record struct AmbientLayoutKey(
        bool Enabled, RectD Surface, double Width, double Height, double SourceWidth, double SourceHeight,
        double Opacity, string Placement, string Scaling, double Size, double OffsetX, double OffsetY,
        bool FlipHorizontal, double EdgeFade, double TintStrength, double TintHue);

    private AmbientLayoutKey? _ambientKey;
    private bool _trailEmpty = true;

    /// <summary>
    /// Draws the soft background exactly as configured: the capture is scaled (cover / fit /
    /// stretch, times Size), shifted by the offsets, clipped to the chosen margins, and the phone
    /// surface itself is always cut out so the live video is never covered.
    /// </summary>
    public void UpdateAmbient(bool enabled, RectD surface, AmbientSettings settings)
    {
        var width = Finite(_canvas.Width);
        var height = Finite(_canvas.Height);
        var source = _ambient.Source;
        var key = new AmbientLayoutKey(
            enabled, surface, width, height, source?.Width ?? 0, source?.Height ?? 0,
            settings.Opacity, settings.Placement, settings.Scaling, settings.Size, settings.OffsetX,
            settings.OffsetY, settings.FlipHorizontal, settings.EdgeFade, settings.TintStrength, settings.TintHue);
        if (key == _ambientKey)
        {
            return;
        }

        _ambientKey = key;
        _ambientContainer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            return;
        }

        _ambient.Opacity = settings.Opacity;
        _ambientContainer.Width = width;
        _ambientContainer.Height = height;

        if (source is { Width: > 0, Height: > 0 } && width > 0 && height > 0)
        {
            var (imageWidth, imageHeight) = AmbientLayout.ImageSize(settings, source.Width, source.Height, width, height);
            _ambient.Width = imageWidth;
            _ambient.Height = imageHeight;
            Canvas.SetLeft(_ambient, (width - imageWidth) / 2 + settings.OffsetX * width / 2);
            Canvas.SetTop(_ambient, (height - imageHeight) / 2 + settings.OffsetY * height / 2);
            _ambient.RenderTransformOrigin = new Point(0.5, 0.5);
            _ambient.RenderTransform = Frozen(new ScaleTransform(settings.FlipHorizontal ? -1 : 1, 1));
        }

        _tint.Width = width;
        _tint.Height = height;
        _tint.Visibility = settings.TintStrength > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (settings.TintStrength > 0)
        {
            var (r, g, b) = AmbientLayout.HueToRgb(settings.TintHue);
            _tint.Fill = Frozen(new SolidColorBrush(Color.FromRgb(r, g, b)));
            _tint.Opacity = settings.TintStrength * settings.Opacity;
        }

        _ambientContainer.OpacityMask = settings.EdgeFade > 0
            ? Frozen(new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.75,
                RadiusY = 0.75,
                GradientStops =
                {
                    new GradientStop(Colors.White, 0),
                    new GradientStop(Colors.White, 1 - settings.EdgeFade),
                    new GradientStop(Colors.Transparent, 1),
                },
            })
            : null;

        var phone = new RectD(surface.X / _dpiScale, surface.Y / _dpiScale, Math.Max(0, surface.Width / _dpiScale), Math.Max(0, surface.Height / _dpiScale));
        var region = AmbientLayout.Region(settings.Placement, phone, width, height);
        _ambientContainer.Clip = Frozen(new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(region.X, region.Y, region.Width, region.Height)),
            new RectangleGeometry(new Rect(phone.X, phone.Y, phone.Width, phone.Height))));
    }

    /// <summary>A freezable the render thread can share instead of copying it on every frame.</summary>
    private static T Frozen<T>(T value) where T : Freezable
    {
        if (value.CanFreeze)
        {
            value.Freeze();
        }

        return value;
    }

    private static double Finite(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private readonly Canvas _pattern = new() { IsHitTestVisible = false };
    private readonly List<Ellipse> _patternDots = [];
    private readonly Polyline _trail = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
        StrokeThickness = 4.5,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };
    private readonly Line _trailTail = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
        StrokeThickness = 4.5,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
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
    private readonly List<Rectangle> _handles = [];
    private RectD _visibleFraction;
    private Point _dragStart;
    private RectD _dragView;
    private int _resizeCorner = -1;
    public event Action<double, double, double>? NavigatorResize;
    public event Action? NavigatorDragStarted;
    public double NavigatorMinScale { get; set; } = 0.1;
    public double NavigatorMaxScale { get; set; } = 10;
    /// <summary>
    /// Shows one captured frame. The pixels are already blurred, so the picture is simply scaled
    /// up: no render-time effect, which is what made a large soft background expensive.
    /// </summary>
    public void SetAmbientFrame(AmbientFrame? frame)
    {
        if (frame is null)
        {
            _ambient.Source = null;
            return;
        }

        if (_ambientBitmap is null || _ambientBitmap.PixelWidth != frame.Width || _ambientBitmap.PixelHeight != frame.Height)
        {
            _ambientBitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Pbgra32, null);
        }

        _ambientBitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);

        // Switching the soft background off clears the picture but keeps the bitmap, so this has to
        // be set on the way back in and not only when a new bitmap is made.
        _ambient.Source = _ambientBitmap;
    }

    public bool AmbientFrameAvailable => _ambient.Source is not null;
    private readonly Rectangle _navigatorViewport = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xD7, 0xFF, 0x3F)),
        IsHitTestVisible = false,
    };
    private readonly Border _patternLoading;
    private readonly TextBlock _patternLoadingText;

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

    /// <summary>Raised while the fullscreen controls are dragged, or with nothing when they are pinned back.</summary>
    public event Action<double?, double?>? HudMovedTo;

    /// <summary>Raised when the fullscreen controls are dropped.</summary>
    public event Action? HudMoveFinished;
    public bool HudVisible => IsVisible && _hudWindow.HudVisible;
    public RectD HudBarRect => _hudWindow.BarRect;
    public void RevealHud(string? message = null) => _hudWindow.Reveal(message);

    /// <summary>Reveals the HUD when the pointer reaches the strip it is pinned to, wherever that is.</summary>
    public void UpdateHud(bool fullscreen, double zoom, HudSettings settings)
    {
        var inZone = false;
        if (fullscreen && Handle != IntPtr.Zero && NativeMethods.GetCursorPos(out var cursor))
        {
            NativeMethods.ScreenToClient(Handle, ref cursor);
            var zone = HudLayout.HoverZone(
                settings.X, settings.Y, _hudWindow.BarWidth, _hudWindow.BarHeight, Width, Height, settings.Position, 12);
            inZone = HudLayout.Contains(zone, cursor.X / _dpiScale, cursor.Y / _dpiScale);
        }

        _hudWindow.Update(fullscreen && IsVisible && settings.Enabled, inZone, zoom, _screenPixels, _dpiScale, settings);
    }

    public OverlayWindow(Window owner)
    {
        _owner = owner;
        _hudWindow = new FullscreenHudWindow(this);
        _hudWindow.ActionRequested += id => HudActionRequested?.Invoke(id);
        _hudWindow.MovedTo += (x, y) => HudMovedTo?.Invoke(x, y);
        _hudWindow.MoveFinished += () => HudMoveFinished?.Invoke();
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

        // The frame is a couple of hundred pixels wide; a cheap linear upscale is exactly right.
        RenderOptions.SetBitmapScalingMode(_ambient, BitmapScalingMode.LowQuality);
        _ambientContainer.Children.Add(_ambient);
        _ambientContainer.Children.Add(_tint);
        _canvas.Children.Add(_ambientContainer);
        _canvas.Children.Add(_trail);
        _canvas.Children.Add(_trailTail);
        _canvas.Children.Add(_pattern);
        _canvas.Children.Add(_label);
        Canvas.SetLeft(_label, 12);
        Canvas.SetTop(_label, 10);

        (_patternLoading, _patternLoadingText) = CreatePatternLoading();
        _canvas.Children.Add(_patternLoading);

        _navigatorCanvas.Children.Add(_navigatorViewport);
        for (var i = 0; i < 4; i++)
        {
            var handle = new Rectangle { Width = 10, Height = 10, Fill = Brushes.White,
                Stroke = Brushes.Black, StrokeThickness = 1, Tag = i,
                Cursor = i is 0 or 3 ? Cursors.SizeNWSE : Cursors.SizeNESW };
            _handles.Add(handle);
            _navigatorCanvas.Children.Add(handle);
        }
        _navigator.ToolTip = "Drag to pan · Drag a corner to zoom";
        _navigator.LostMouseCapture += (_, _) => { _navigatorDragging = false; _resizeCorner = -1; };
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
            _hudWindow.Update(false, false, 1, default, _dpiScale, new HudSettings());
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

        // Moving a window to where it already is still costs a repaint, and this runs thirty times
        // a second while the mirror sits perfectly still.
        if (screenPixels.Left == _screenPixels.Left && screenPixels.Top == _screenPixels.Top
            && screenPixels.Width == _screenPixels.Width && screenPixels.Height == _screenPixels.Height)
        {
            return;
        }

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
        _patternDots.Clear();
        _patternLoading.Visibility = Visibility.Collapsed;
        _label.Text = string.Empty;
        ClearTrail();
    }

    public void ShowPatternLoading(string label)
    {
        _pattern.Children.Clear();
        _patternDots.Clear();
        ClearTrail();
        _label.Text = "PATTERN GUIDE  ·  aligning with the phone…";
        _label.Foreground = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F));
        _patternLoadingText.Text = label;
        _patternLoading.Visibility = Visibility.Visible;
        Canvas.SetLeft(_patternLoading, Math.Max(12, (_canvas.Width - _patternLoading.Width) / 2));
        Canvas.SetTop(_patternLoading, Math.Max(48, (_canvas.Height - _patternLoading.Height) / 2));
    }

    /// <summary>Draws nine dots (in overlay pixel coordinates) plus an optional calibration box.</summary>
    public void DrawPattern(IReadOnlyList<PointD> pointsPixels, double radiusPixels, double opacity, string label, bool calibrating)
    {
        _patternLoading.Visibility = Visibility.Collapsed;
        _pattern.Children.Clear();
        _patternDots.Clear();
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
            _patternDots.Add(dot);
        }

        _label.Text = label;
        _label.Foreground = calibrating ? live : signal;
    }

    public void UpdatePatternTrace(
        IReadOnlyList<PointD> pointsPixels,
        IReadOnlyList<int> selected,
        PointD? pointerPixels,
        double opacity)
    {
        if (selected.Count == 0 && pointerPixels is null)
        {
            ClearTrail();
            return;
        }

        _trailEmpty = false;
        _trail.Points.Clear();
        foreach (var index in selected)
        {
            if (index >= 0 && index < pointsPixels.Count)
            {
                _trail.Points.Add(new Point(pointsPixels[index].X / _dpiScale, pointsPixels[index].Y / _dpiScale));
            }
        }

        _trail.Opacity = opacity;
        for (var i = 0; i < _patternDots.Count; i++)
        {
            _patternDots[i].Fill = selected.Contains(i) ? _patternDots[i].Stroke : Brushes.Transparent;
        }

        if (selected.Count > 0 && pointerPixels is { } pointer && selected[^1] < pointsPixels.Count)
        {
            var last = pointsPixels[selected[^1]];
            _trailTail.X1 = last.X / _dpiScale;
            _trailTail.Y1 = last.Y / _dpiScale;
            _trailTail.X2 = pointer.X / _dpiScale;
            _trailTail.Y2 = pointer.Y / _dpiScale;
            _trailTail.Opacity = opacity * 0.8;
            _trailTail.Visibility = Visibility.Visible;
        }
        else
        {
            _trailTail.Visibility = Visibility.Collapsed;
        }
    }

    public void ClearTrail()
    {
        // Clearing what is already clear still marks the window as changed, and this runs twenty
        // times a second whenever the pattern guide is off, which is nearly always.
        if (_trailEmpty)
        {
            return;
        }

        _trailEmpty = true;
        _trail.Points.Clear();
        _trailTail.Visibility = Visibility.Collapsed;
        foreach (var dot in _patternDots)
        {
            dot.Fill = Brushes.Transparent;
        }
    }

    private static (Border Border, TextBlock Text) CreatePatternLoading()
    {
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 70, Height = 70, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 9; i++)
        {
            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(6),
                Fill = new SolidColorBrush(Color.FromRgb(0xD7, 0xFF, 0x3F)),
                Opacity = 0.2,
            };
            dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(i * 55),
            });
            grid.Children.Add(dot);
        }

        var text = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.White,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var content = new StackPanel();
        content.Children.Add(grid);
        content.Children.Add(text);
        return (new Border
        {
            Width = 210,
            Height = 126,
            Padding = new Thickness(16, 12, 16, 10),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x08, 0x0A, 0x09)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xD7, 0xFF, 0x3F)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = content,
        }, text);
    }

    // ----- Navigator -----

    private (bool Show, double Aspect, RectD Visible, double Width, string Corner, double CanvasWidth, double CanvasHeight)? _navigatorKey;

    public void UpdateNavigator(bool show, double surfaceAspect, RectD visibleFraction, ZoomSettings zoom)
    {
        var key = (show, surfaceAspect, visibleFraction, zoom.NavigatorWidth, zoom.NavigatorCorner, Finite(_canvas.Width), Finite(_canvas.Height));
        if (_navigatorKey is { } previous && previous == key)
        {
            return;
        }

        _navigatorKey = key;
        if (!show)
        {
            if (_navigatorDragging) _navigator.ReleaseMouseCapture();
            _navigator.Visibility = Visibility.Collapsed;
            _navigatorScreenRect = Rect.Empty;
            return;
        }

        var navigatorWidth = zoom.NavigatorWidth;
        var innerWidth = Math.Min(navigatorWidth - 12, (navigatorWidth + 70) * Math.Max(0.1, surfaceAspect));
        var innerHeight = innerWidth / Math.Max(0.1, surfaceAspect);
        _visibleFraction = visibleFraction;
        _navigatorCanvas.Width = innerWidth;
        _navigatorCanvas.Height = innerHeight;
        _navigator.Width = navigatorWidth;
        _navigator.Visibility = Visibility.Visible;

        Canvas.SetLeft(_navigatorViewport, visibleFraction.X * innerWidth);
        Canvas.SetTop(_navigatorViewport, visibleFraction.Y * innerHeight);
        _navigatorViewport.Width = Math.Max(4, visibleFraction.Width * innerWidth);
        _navigatorViewport.Height = Math.Max(4, visibleFraction.Height * innerHeight);
        for (var i = 0; i < 4; i++)
        {
            Canvas.SetLeft(_handles[i], (visibleFraction.X + (i % 2) * visibleFraction.Width) * innerWidth - 5);
            Canvas.SetTop(_handles[i], (visibleFraction.Y + (i / 2) * visibleFraction.Height) * innerHeight - 5);
        }

        var (left, top) = AmbientLayout.NavigatorPosition(zoom.NavigatorCorner, navigatorWidth, innerHeight + 12, _canvas.Width, _canvas.Height, NavigatorMargin);
        Canvas.SetLeft(_navigator, left);
        Canvas.SetTop(_navigator, top);
        _navigatorScreenRect = new Rect(left * _dpiScale, top * _dpiScale, navigatorWidth * _dpiScale, (innerHeight + 12) * _dpiScale);
    }

    private void OnNavigatorDown(object sender, MouseButtonEventArgs e)
    {
        _navigatorDragging = true;
        NavigatorDragStarted?.Invoke();
        _dragStart = e.GetPosition(_navigatorCanvas);
        _dragView = _visibleFraction;
        _resizeCorner = e.OriginalSource is Rectangle { Tag: int corner } ? corner : -1;
        _navigator.CaptureMouse();
        if (_resizeCorner < 0 && !new Rect(_dragView.X * _navigatorCanvas.Width,
            _dragView.Y * _navigatorCanvas.Height, _dragView.Width * _navigatorCanvas.Width,
            _dragView.Height * _navigatorCanvas.Height).Contains(_dragStart))
        {
            RaiseNavigator(_dragStart);
            _dragView = _visibleFraction;
        }
        e.Handled = true;
    }

    private void OnNavigatorMove(object sender, MouseEventArgs e)
    {
        if (_navigatorDragging)
        {
            var point = e.GetPosition(_navigatorCanvas);
            var dx = (point.X - _dragStart.X) / _navigatorCanvas.Width;
            var dy = (point.Y - _dragStart.Y) / _navigatorCanvas.Height;
            if (_resizeCorner >= 0)
            {
                var resized = NavigatorMath.Resize(_dragView, _resizeCorner, dx, dy, NavigatorMinScale, NavigatorMaxScale);
                NavigatorResize?.Invoke(resized.Scale, resized.X, resized.Y);
            }
            else NavigatorTarget?.Invoke(_dragView.X + _dragView.Width / 2 + dx, _dragView.Y + _dragView.Height / 2 + dy);
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
