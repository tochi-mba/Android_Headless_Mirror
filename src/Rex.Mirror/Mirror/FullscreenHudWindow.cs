using System.Windows;
using System.Windows.Interop;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// A compact, non-activating host for the fullscreen HUD.
///
/// The general mirror overlay deliberately stays transparent to mouse input so Android receives
/// normal clicks directly. Hosting fullscreen controls in their own tiny HWND avoids DPI-sensitive
/// hit testing across the whole mirror surface and means only the visible HUD can intercept input.
/// </summary>
public sealed class FullscreenHudWindow : Window
{
    private const double TopMargin = 12;

    private readonly Window _owner;
    private readonly FullscreenHud _hud = new();
    private HwndSource? _source;

    public FullscreenHudWindow(Window owner)
    {
        _owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = false;
        SizeToContent = SizeToContent.Manual;
        Width = 1;
        Height = 1;
        Content = _hud;

        _hud.ActionRequested += id => ActionRequested?.Invoke(id);

        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            var exStyle = NativeMethods.GetStyle(_source.Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetStyle(
                _source.Handle,
                NativeMethods.GWL_EXSTYLE,
                (exStyle & ~NativeMethods.WS_EX_TRANSPARENT) |
                NativeMethods.WS_EX_NOACTIVATE |
                NativeMethods.WS_EX_TOOLWINDOW);
            _source.AddHook(WndProcHook);
        };
    }

    public event Action<string>? ActionRequested;

    /// <summary>
    /// Raised while the bar is dragged, with its new middle as a fraction of the mirror area, or
    /// with nothing at all when it is sent back to its pinned position.
    /// </summary>
    public event Action<double?, double?>? MovedTo;

    /// <summary>Raised when the bar is dropped, so the position can be saved once rather than per frame.</summary>
    public event Action? MoveFinished;

    private RECT _mirrorPixels;
    private bool _dragging;
    private Point _dragOrigin;
    private (double X, double Y) _dragCentre;

    private void OnDragStarted(Point pointer)
    {
        var scale = Dpi();
        _dragOrigin = pointer;
        _dragCentre = (_barLeft + (Width * scale / 2), _barTop + (Height * scale / 2));
    }

    private void OnDragMoved(Point pointer)
    {
        if (_mirrorPixels.Width <= 0 || _mirrorPixels.Height <= 0)
        {
            return;
        }

        var centreX = _dragCentre.X + (pointer.X - _dragOrigin.X);
        var centreY = _dragCentre.Y + (pointer.Y - _dragOrigin.Y);
        var (x, y) = HudLayout.Fraction(
            centreX - _mirrorPixels.Left,
            centreY - _mirrorPixels.Top,
            _mirrorPixels.Width,
            _mirrorPixels.Height);
        MovedTo?.Invoke(x, y);
    }

    private double Dpi() => _source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

    /// <summary>The point a mouse message carries, in the bar's own units.</summary>
    private Point ClientPoint(IntPtr lParam)
    {
        var packed = lParam.ToInt64();
        var scale = Dpi();
        return new Point((short)(packed & 0xFFFF) / scale, (short)((packed >> 16) & 0xFFFF) / scale);
    }

    private static Point PointerOnScreen() =>
        NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : new Point();

    private double _barLeft;
    private double _barTop;

    public bool HudVisible => IsVisible && _hud.IsShown;

    /// <summary>The bar's size in device-independent pixels, for working out where it can be reached.</summary>
    public double BarWidth => Width;

    public double BarHeight => Height;

    /// <summary>Where the bar is on screen, in physical pixels.</summary>
    public RectD BarRect
    {
        get
        {
            if (Handle == IntPtr.Zero)
            {
                return default;
            }

            var rect = NativeMethods.GetClientScreenRect(Handle);
            return new RectD(rect.Left, rect.Top, rect.Width, rect.Height);
        }
    }

    private IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    public void Reveal(string? message = null) => _hud.Reveal(message);

    private (int Left, int Top, int Width, int Height, double Dpi, string Position, double Scale, string Buttons, double X, double Y)? _placement;

    public void Update(bool enabled, bool pointerInZone, double zoom, RECT mirrorPixels, double dpiScale, HudSettings settings)
    {
        _hud.Apply(settings);
        Opacity = settings.Opacity;
        _hud.Update(enabled, pointerInZone, zoom);

        if (!enabled || mirrorPixels.Width <= 0 || mirrorPixels.Height <= 0)
        {
            if (IsVisible)
            {
                Hide();
            }

            _placement = null;
            return;
        }

        // Measuring the bar and moving its window costs the same whether or not anything moved,
        // and this is called on every frame while fullscreen.
        _mirrorPixels = mirrorPixels;
        var placement = (mirrorPixels.Left, mirrorPixels.Top, mirrorPixels.Width, mirrorPixels.Height,
            dpiScale, settings.Position, settings.Scale, string.Join('|', settings.Buttons),
            settings.X ?? -1, settings.Y ?? -1);
        if (_placement is { } previous && previous == placement)
        {
            return;
        }

        _placement = placement;
        EnsureWindow();

        var scale = Math.Max(0.5, dpiScale);
        var maxWidthDip = Math.Max(1, mirrorPixels.Width / scale - (TopMargin * 2));
        _hud.Measure(new Size(maxWidthDip, double.PositiveInfinity));

        var widthDip = Math.Max(1, Math.Min(maxWidthDip, _hud.DesiredSize.Width));
        var heightDip = Math.Max(1, _hud.DesiredSize.Height);
        Width = widthDip;
        Height = heightDip;

        var widthPx = Math.Max(1, (int)Math.Ceiling(widthDip * scale));
        var heightPx = Math.Max(1, (int)Math.Ceiling(heightDip * scale));
        var (left, top) = HudLayout.Place(
            settings.Position, settings.X, settings.Y, widthPx, heightPx,
            mirrorPixels.Width, mirrorPixels.Height, TopMargin * scale);
        var leftPx = mirrorPixels.Left + (int)Math.Round(left);
        var topPx = mirrorPixels.Top + (int)Math.Round(top);
        _barLeft = leftPx;
        _barTop = topPx;

        NativeMethods.SetWindowPos(
            Handle,
            IntPtr.Zero,
            leftPx,
            topPx,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_NOZORDER |
            NativeMethods.SWP_NOSENDCHANGING |
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void EnsureWindow()
    {
        if (_source is null)
        {
            Owner ??= _owner;
            new WindowInteropHelper(this).EnsureHandle();
        }

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>
    /// Dragging is driven from the window's own messages rather than from WPF's mouse events. This
    /// window never takes activation, and a window that is never active does not get WPF input
    /// routed through its element tree reliably; its messages, however, always arrive here.
    /// </summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_LBUTTONDOWN when !_hud.HitsButton(ClientPoint(lParam)):
                _dragging = true;
                NativeMethods.SetCapture(hwnd);
                OnDragStarted(PointerOnScreen());
                handled = true;
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONDBLCLK when !_hud.HitsButton(ClientPoint(lParam)):
                MovedTo?.Invoke(null, null);
                MoveFinished?.Invoke();
                handled = true;
                return IntPtr.Zero;

            case NativeMethods.WM_MOUSEMOVE when _dragging:
                _hud.Reveal();
                OnDragMoved(PointerOnScreen());
                handled = true;
                return IntPtr.Zero;

            case NativeMethods.WM_LBUTTONUP when _dragging:
                _dragging = false;
                NativeMethods.ReleaseCapture();
                MoveFinished?.Invoke();
                handled = true;
                return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_NCHITTEST)
        {
            handled = true;
            return new IntPtr(_hud.IsShown ? NativeMethods.HTCLIENT : NativeMethods.HTTRANSPARENT);
        }

        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProcHook);
        base.OnClosed(e);
    }
}
