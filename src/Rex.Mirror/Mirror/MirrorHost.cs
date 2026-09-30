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
    private bool _holdingKeyboard;
    private RECT? _clip;
    private bool _recheckShape;

    /// <summary>The size last given to scrcpy's window; null until a layout has reached it (see <see cref="ChildShape"/>).</summary>
    private (int Width, int Height)? _applied;

    /// <summary>Whether scrcpy has reported the video's size for this window, which then decides its shape alone.</summary>
    private bool _videoReported;

    public event Action<ZoomView>? ViewChanged;
    public event Action? ChildFocused;

    /// <summary>
    /// Where this viewport sits inside the mirror area, in physical pixels. With one view it is
    /// the whole area and this is zero; with copies of the phone beside it, the overlay and the
    /// soft background still cover the whole area, and use this to find this view inside it.
    /// </summary>
    public Point AreaOffset { get; set; }

    /// <summary>The picture's rectangle in mirror-area pixels (see <see cref="AreaOffset"/>).</summary>
    public RectD AreaSurfaceRect => new(AreaOffset.X + _view.OffsetX, AreaOffset.Y + _view.OffsetY, _view.SurfaceWidth, _view.SurfaceHeight);

    /// <summary>The viewport's rectangle in mirror-area pixels.</summary>
    public RectD AreaViewportRect
    {
        get
        {
            var (width, height) = ViewportPixels;
            return new RectD(AreaOffset.X, AreaOffset.Y, width, height);
        }
    }

    public bool HasChild => _child != IntPtr.Zero && NativeMethods.IsWindow(_child);
    public IntPtr ViewportHandle => _viewport;
    public double Zoom => _zoom;

    /// <summary>The picture's width over its height, as last reported by scrcpy or its window.</summary>
    public double VideoAspect => _videoAspect;
    public ZoomView View => _view;
    public double MaxZoom { get; set; } = 4.0;

    /// <summary>Zoom back out to the whole picture when it turns between upright and on its side.</summary>
    public bool ResetZoomOnTurn { get; set; }

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

    /// <summary>
    /// The video's size. <paramref name="reported"/> is false for a guess made before scrcpy has
    /// said (the phone's display size, which is in its natural orientation whatever way it is
    /// held); scrcpy's own report is the word on the picture's shape from then on.
    /// </summary>
    public void SetVideoSize(int width, int height, bool reported = true)
    {
        if (width > 0 && height > 0)
        {
            _videoReported |= reported;
            SetAspect((double)width / height);
        }
    }

    /// <summary>Adopts a new video shape and re-fits the picture to the viewport.</summary>
    private void SetAspect(double aspect)
    {
        if (Math.Abs(aspect - _videoAspect) < 0.001)
        {
            Relayout();
            return;
        }

        var turned = aspect < 1 != _videoAspect < 1;
        _videoAspect = aspect;
        if (turned && ResetZoomOnTurn)
        {
            _zoom = 1.0;
        }

        var (width, height) = ViewportPixels;
        if (width > 0 && height > 0)
        {
            _view = ZoomMath.Refit(width, height, _videoAspect, _zoom);
            Apply();
        }
    }

    public void Attach(IntPtr childHwnd, uint threadId, uint processId)
    {
        Detach();
        _child = childHwnd;
        _applied = null;
        _videoReported = false;

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
        _applied = null;
        _videoReported = false;
        _holdingKeyboard = false;
        _zoom = 1.0;
        _view = ZoomView.Identity;
        ClipToPicture();
        ViewChanged?.Invoke(_view);
    }

    /// <summary>
    /// The viewport rectangle the viewport window keeps, in its own client pixels: the part the
    /// picture covers. Null means the whole viewport (no picture yet, or the picture fills it).
    /// </summary>
    internal static RECT? ClipFor(int viewportWidth, int viewportHeight, ZoomView view, bool hasChild)
    {
        if (!hasChild || viewportWidth <= 0 || viewportHeight <= 0 || view.SurfaceWidth <= 0 || view.SurfaceHeight <= 0)
        {
            return null;
        }

        var clip = new RECT
        {
            Left = Math.Max(0, (int)Math.Floor(view.OffsetX)),
            Top = Math.Max(0, (int)Math.Floor(view.OffsetY)),
            Right = Math.Min(viewportWidth, (int)Math.Ceiling(view.OffsetX + view.SurfaceWidth)),
            Bottom = Math.Min(viewportHeight, (int)Math.Ceiling(view.OffsetY + view.SurfaceHeight)),
        };
        if (clip.Left == 0 && clip.Top == 0 && clip.Right == viewportWidth && clip.Bottom == viewportHeight)
        {
            return null;
        }

        return clip.Right > clip.Left && clip.Bottom > clip.Top ? clip : null;
    }

    /// <summary>
    /// Cuts the viewport window down to the picture, so the main window shows through the margins
    /// around the phone. That is where the soft background is drawn (<see cref="AmbientView"/>):
    /// as ordinary window content on the GPU, instead of in the transparent overlay, which Windows
    /// redraws on the CPU.
    /// </summary>
    private void ClipToPicture()
    {
        if (_viewport == IntPtr.Zero)
        {
            return;
        }

        var (width, height) = ViewportPixels;
        var clip = ClipFor(width, height, _view, HasChild);
        if (Same(clip, _clip))
        {
            return;
        }

        _clip = clip;
        var region = clip is { } r ? NativeMethods.CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom) : IntPtr.Zero;
        if (NativeMethods.SetWindowRgn(_viewport, region, true) == 0 && region != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(region);
            _clip = null;
        }
    }

    private static bool Same(RECT? a, RECT? b) =>
        a is { } x ? b is { } y && x.Left == y.Left && x.Top == y.Top && x.Right == y.Right && x.Bottom == y.Bottom : b is null;

    /// <summary>Whether the native viewport is showing on screen right now.</summary>
    public bool IsShown => _viewport != IntPtr.Zero && NativeMethods.IsWindowVisible(_viewport);

    /// <summary>Hides the native viewport so WPF content (empty state, setup) can show in its place.</summary>
    public void SetShown(bool shown)
    {
        if (_viewport != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_viewport, shown ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
        }
    }

    /// <summary>True while left Alt is held for the PC and the phone is kept from seeing it.</summary>
    public bool HoldingKeyboard => _holdingKeyboard;

    /// <summary>
    /// Takes keyboard focus away from the phone while left Alt is held for the PC view.
    ///
    /// The phone is a real keyboard to Android now, so it sees a held Alt the way it would from a
    /// plugged-in keyboard, and Android answers a held modifier by showing its keyboard shortcut
    /// list, right in the middle of an Alt + pinch or Alt + wheel zoom. Called from the keyboard
    /// hook before Windows routes the key, this moves focus to the viewport so the Alt, and
    /// anything typed with it, lands here instead of on the phone.
    /// </summary>
    public void HoldKeyboard()
    {
        if (_holdingKeyboard || !HasChild || _viewport == IntPtr.Zero || NativeMethods.GetFocus() != _child)
        {
            return;
        }

        _holdingKeyboard = true;
        NativeMethods.SetFocus(_viewport);
    }

    /// <summary>Hands the keyboard back to the phone once Alt is released, if nothing else took it meanwhile.</summary>
    public void ReleaseKeyboard()
    {
        if (!_holdingKeyboard)
        {
            return;
        }

        _holdingKeyboard = false;
        if (NativeMethods.GetFocus() == _viewport)
        {
            FocusChild();
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

    /// <summary>
    /// Takes the same zoom and the same part of the phone as <paramref name="leader"/>, so a copy
    /// of the phone always shows what the main view shows. Views of different sizes keep the same
    /// proportions: the picture is scaled with the viewport.
    /// </summary>
    public void Follow(MirrorHost leader)
    {
        if (ReferenceEquals(leader, this))
        {
            return;
        }

        var (width, height) = ViewportPixels;
        var (leaderWidth, leaderHeight) = leader.ViewportPixels;
        if (width <= 0 || height <= 0 || leaderWidth <= 0 || leaderHeight <= 0)
        {
            return;
        }

        var view = FollowedView(leader._view, leaderWidth, leaderHeight, width, height);
        if (Math.Abs(leader._zoom - _zoom) < 1e-9 && view == _view)
        {
            return;
        }

        _zoom = leader._zoom;
        _view = view;
        Apply();
    }

    /// <summary>A leader's view carried over to a viewport of another size, in proportion.</summary>
    internal static ZoomView FollowedView(ZoomView leader, int leaderWidth, int leaderHeight, int width, int height)
    {
        var scaleX = (double)width / leaderWidth;
        var scaleY = (double)height / leaderHeight;
        return leader with
        {
            OffsetX = leader.OffsetX * scaleX,
            OffsetY = leader.OffsetY * scaleY,
            SurfaceWidth = leader.SurfaceWidth * scaleX,
            SurfaceHeight = leader.SurfaceHeight * scaleY,
        };
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
                _applied = ((int)_view.SurfaceWidth, (int)_view.SurfaceHeight);
            }
            finally
            {
                _applying = false;
            }
        }

        ClipToPicture();
        ViewChanged?.Invoke(_view);
        if (_recheckShape)
        {
            // scrcpy changed its own size while ours was being applied; look at what it ended up as.
            _recheckShape = false;
            Dispatcher.BeginInvoke(AdoptChildShape);
        }
    }

    private void OnChildWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd != _child || idObject != NativeMethods.OBJID_WINDOW || !HasChild)
        {
            return;
        }

        if (_applying)
        {
            // Setting the child's position pumps messages, so a resize of scrcpy's own (the phone
            // turning) can be reported in the middle of ours. Ignoring it outright is how a
            // landscape picture was occasionally left at the portrait width.
            _recheckShape = true;
            return;
        }

        AdoptChildShape();
    }

    /// <summary>
    /// Makes sure the picture is the shape and size the view says it is. scrcpy resizes its own
    /// window when the phone turns (keeping the old width, so a landscape picture arrives small);
    /// that is followed by window events, which can be missed, so the window calls this on its
    /// regular tick as well and a missed event corrects itself within a frame or two.
    /// </summary>
    public void SyncChildShape()
    {
        if (!_applying && HasChild)
        {
            AdoptChildShape();
        }
    }

    private void AdoptChildShape()
    {
        if (_applying || !HasChild)
        {
            return;
        }

        NativeMethods.GetWindowRect(_child, out var rect);
        NativeMethods.GetWindowRect(_viewport, out var viewport);
        var positioned = rect.Left - viewport.Left == (int)_view.OffsetX && rect.Top - viewport.Top == (int)_view.OffsetY;
        switch (ChildShape.Decide((rect.Width, rect.Height), _applied, positioned, _videoReported))
        {
            case ChildShapeAction.Reposition:
            case ChildShapeAction.Reassert:
                // SDL likes to re-assert its own geometry; the view is right, so it goes back.
                Apply();
                break;
            case ChildShapeAction.Adopt:
                // A size change we did not ask for, from a scrcpy that has not said what the video
                // is: its new shape is the only sign the phone turned. Re-fit, so a landscape
                // picture fills the viewport instead of sitting in the old rectangle.
                SetAspect((double)rect.Width / rect.Height);
                break;
        }
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
                    // A click is a clear sign of where the person wants the keyboard.
                    _holdingKeyboard = false;
                    FocusChild();
                }

                break;
            case NativeMethods.WM_SETFOCUS:
                if (!_holdingKeyboard)
                {
                    FocusChild();
                }

                break;
            case NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP or NativeMethods.WM_SYSCHAR
                or NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_CHAR when _holdingKeyboard:
                // Keys that arrive while Alt is held for the PC are swallowed here. Left to the
                // default handling, releasing Alt would open the window menu and Alt + letter would beep.
                handled = true;
                return IntPtr.Zero;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override bool TabIntoCore(System.Windows.Input.TraversalRequest request)
    {
        FocusChild();
        return HasChild;
    }
}
