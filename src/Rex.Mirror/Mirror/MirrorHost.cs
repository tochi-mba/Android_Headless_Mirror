using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// The viewport that embeds scrcpy's window as a child. Zoom is real: the scrcpy surface is
/// resized to (fit × zoom) and offset inside this clipping viewport, so input lands exactly
/// where the pixels are and nothing needs a magnifier. All geometry here is in physical pixels.
/// </summary>
public sealed class MirrorHost : HwndHost
{
    private IntPtr _viewport;
    private IntPtr _child;
    private IntPtr _winEventHook;
    private WinEventProc? _winEventProc;
    private double _videoAspect = 1080.0 / 2400.0;
    private ZoomView _view = ZoomView.Identity;
    private double _zoom = 1.0;
    private bool _applying;

    public event Action<ZoomView>? ViewChanged;
    public event Action? ChildFocused;

    public bool HasChild => _child != IntPtr.Zero && NativeMethods.IsWindow(_child);
    public IntPtr ViewportHandle => _viewport;
    public IntPtr ChildHandle => _child;
    public double Zoom => _zoom;
    public ZoomView View => _view;
    public double MaxZoom { get; set; } = 4.0;

    /// <summary>Viewport size in physical pixels.</summary>
    public (int Width, int Height) ViewportPixels
    {
        get
        {
            if (_viewport == IntPtr.Zero)
            {
                return (0, 0);
            }

            NativeMethods.GetClientRect(_viewport, out var rect);
            return (rect.Width, rect.Height);
        }
    }

    public RECT ViewportScreenRect => _viewport == IntPtr.Zero ? default : NativeMethods.GetClientScreenRect(_viewport);

    /// <summary>The scrcpy surface rectangle relative to the viewport (may extend beyond it when zoomed).</summary>
    public RectD SurfaceRect => new(_view.OffsetX, _view.OffsetY, _view.SurfaceWidth, _view.SurfaceHeight);

    public RECT SurfaceScreenRect
    {
        get
        {
            var viewport = ViewportScreenRect;
            return new RECT
            {
                Left = viewport.Left + (int)_view.OffsetX,
                Top = viewport.Top + (int)_view.OffsetY,
                Right = viewport.Left + (int)(_view.OffsetX + _view.SurfaceWidth),
                Bottom = viewport.Top + (int)(_view.OffsetY + _view.SurfaceHeight),
            };
        }
    }

    public void SetVideoSize(int width, int height)
    {
        if (width > 0 && height > 0)
        {
            _videoAspect = (double)width / height;
            Relayout();
        }
    }

    public void Attach(IntPtr childHwnd, uint threadId, uint processId)
    {
        Detach();
        _child = childHwnd;

        var style = NativeMethods.GetStyle(childHwnd, NativeMethods.GWL_STYLE);
        style &= ~(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                   NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_SYSMENU);
        style |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS;
        NativeMethods.SetStyle(childHwnd, NativeMethods.GWL_STYLE, style);

        var exStyle = NativeMethods.GetStyle(childHwnd, NativeMethods.GWL_EXSTYLE);
        exStyle &= ~(NativeMethods.WS_EX_APPWINDOW | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetStyle(childHwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        NativeMethods.SetParent(childHwnd, _viewport);
        NativeMethods.SetWindowPos(childHwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

        // scrcpy resizes its own window when the phone rotates; we follow the new aspect and re-fit.
        _winEventProc = OnChildWinEvent;
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, _winEventProc, processId, threadId, NativeMethods.WINEVENT_OUTOFCONTEXT);

        _zoom = 1.0;
        _view = ZoomView.Identity;
        Relayout();
        FocusChild();
    }

    public void Detach()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
            _winEventProc = null;
        }

        _child = IntPtr.Zero;
        _zoom = 1.0;
        _view = ZoomView.Identity;
        ViewChanged?.Invoke(_view);
    }

    /// <summary>Hides the native viewport so WPF content (empty state, setup) can show in its place.</summary>
    public void SetShown(bool shown)
    {
        if (_viewport != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_viewport, shown ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
        }
    }

    public void FocusChild()
    {
        if (HasChild)
        {
            NativeMethods.SetFocus(_child);
            ChildFocused?.Invoke();
        }
    }

    // ----- Zoom API (viewport pixel coordinates) -----

    public void SetZoom(double zoom, double anchorX, double anchorY)
    {
        var clamped = ZoomMath.ClampZoom(zoom, MaxZoom);
        var (width, height) = ViewportPixels;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var fit = ZoomMath.FitRect(width, height, _videoAspect, 1.0);
        _zoom = clamped;
        _view = ZoomMath.Compute(width, height, fit, clamped, anchorX, anchorY, _view.SurfaceWidth > 0 ? _view : null);
        Apply();
    }

    public void ZoomBy(double factor, double anchorX, double anchorY) => SetZoom(_zoom * factor, anchorX, anchorY);

    public void ZoomStep(int direction, double? anchorX = null, double? anchorY = null, double step = 0.1)
    {
        var (width, height) = ViewportPixels;
        var factor = direction > 0 ? 1 + step : 1 / (1 + step);
        SetZoom(_zoom * factor, anchorX ?? width / 2.0, anchorY ?? height / 2.0);
    }

    public void ResetZoom()
    {
        var (width, height) = ViewportPixels;
        SetZoom(1.0, width / 2.0, height / 2.0);
    }

    public void Pan(double deltaX, double deltaY)
    {
        if (!_view.IsZoomed)
        {
            return;
        }

        var (width, height) = ViewportPixels;
        _view = ZoomMath.Pan(width, height, _view, deltaX, deltaY);
        Apply();
    }

    public void CenterOn(double fractionX, double fractionY)
    {
        var (width, height) = ViewportPixels;
        _view = ZoomMath.CenterOn(width, height, _view, fractionX, fractionY);
        Apply();
    }

    public RectD VisibleFraction()
    {
        var (width, height) = ViewportPixels;
        return ZoomMath.VisibleFraction(width, height, _view);
    }

    private void Relayout()
    {
        var (width, height) = ViewportPixels;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var fit = ZoomMath.FitRect(width, height, _videoAspect, 1.0);
        var anchorX = _view.SurfaceWidth > 0 ? width / 2.0 : fit.X + fit.Width / 2;
        var anchorY = _view.SurfaceHeight > 0 ? height / 2.0 : fit.Y + fit.Height / 2;
        _view = ZoomMath.Compute(width, height, fit, _zoom, anchorX, anchorY, _view.SurfaceWidth > 0 ? _view : null);
        Apply();
    }

    private void Apply()
    {
        if (HasChild)
        {
            _applying = true;
            try
            {
                NativeMethods.SetWindowPos(_child, NativeMethods.HWND_TOP,
                    (int)_view.OffsetX, (int)_view.OffsetY, (int)_view.SurfaceWidth, (int)_view.SurfaceHeight,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
            }
            finally
            {
                _applying = false;
            }
        }

        ViewChanged?.Invoke(_view);
    }

    private void OnChildWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_applying || hwnd != _child || idObject != NativeMethods.OBJID_WINDOW || !HasChild)
        {
            return;
        }

        NativeMethods.GetWindowRect(_child, out var rect);
        var width = rect.Width;
        var height = rect.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var expectedWidth = (int)_view.SurfaceWidth;
        var expectedHeight = (int)_view.SurfaceHeight;
        if (Math.Abs(width - expectedWidth) <= 1 && Math.Abs(height - expectedHeight) <= 1)
        {
            // Only the position drifted (SDL likes to re-assert its own geometry); put it back.
            NativeMethods.GetWindowRect(_viewport, out var viewport);
            if (rect.Left - viewport.Left != (int)_view.OffsetX || rect.Top - viewport.Top != (int)_view.OffsetY)
            {
                Apply();
            }

            return;
        }

        // A size change we did not request: the video orientation changed. Adopt the new aspect.
        var aspect = (double)width / height;
        if (Math.Abs(aspect - _videoAspect) > 0.01)
        {
            _videoAspect = aspect;
        }

        Relayout();
    }

    // ----- HwndHost plumbing -----

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        NativeMethods.EnsureViewportClass();
        _viewport = NativeMethods.CreateWindowExW(
            0, NativeMethods.ViewportClassName, "RexMirrorViewport",
            (uint)(NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS),
            0, 0, 10, 10, hwndParent.Handle, IntPtr.Zero, NativeMethods.GetModuleHandleW(null), IntPtr.Zero);
        if (_viewport == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create the mirror viewport window: " + Marshal.GetLastWin32Error());
        }

        return new HandleRef(this, _viewport);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Detach();
        NativeMethods.DestroyWindow(hwnd.Handle);
        _viewport = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        Relayout();
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_PARENTNOTIFY:
                var evt = (int)(wParam.ToInt64() & 0xFFFF);
                if (evt is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
                {
                    FocusChild();
                }

                break;
            case NativeMethods.WM_SETFOCUS:
                FocusChild();
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request)
    {
        FocusChild();
        return HasChild;
    }
}
