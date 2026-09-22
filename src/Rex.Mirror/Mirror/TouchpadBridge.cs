using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Turns raw Precision Touchpad contacts into phone touch (two fingers → two fingers on the
/// phone) or, while Alt is held, into PC-side zoom and pan. Single-finger input is untouched:
/// Windows keeps moving the mouse as usual.
/// </summary>
public sealed class TouchpadBridge
{
    private enum GestureKind { None, Android, Host }

    private const double PixelsPerMillimetre = 6.0;
    private const double HostZoomSensitivity = 0.7;

    private readonly MirrorHost _host;
    private readonly TouchInjector _injector;
    private readonly Func<RexConfig> _config;
    private readonly Action<string> _log;
    private readonly Dictionary<uint, (double X, double Y)> _contacts = [];

    private GestureKind _kind = GestureKind.None;
    private (double X, double Y) _startCentroid;
    private double _startDistance;
    private double _startZoom;
    private (double X, double Y) _lastCentroid;
    private (int X, int Y) _touchCenter;
    private (int X, int Y) _firstScreen, _secondScreen;
    private POINT _savedCursor;
    private bool _cursorSaved;

    public TouchpadBridge(MirrorHost host, TouchInjector injector, Func<RexConfig> config, Action<string> log)
    {
        _host = host;
        _injector = injector;
        _config = config;
        _log = log;
    }

    public bool IsGestureActive => _kind != GestureKind.None;

    /// <summary>Handles a WM_POINTER* message. Returns true when the message was consumed.</summary>
    public bool HandlePointerMessage(int msg, IntPtr wParam)
    {
        var config = _config();
        if (!config.Touchpad.Enabled || !_host.HasChild)
        {
            return false;
        }

        var pointerId = (uint)(wParam.ToInt64() & 0xFFFF);
        if (msg == NativeMethods.WM_POINTERCAPTURECHANGED)
        {
            EndGesture();
            _contacts.Clear();
            return false;
        }

        if (!NativeMethods.TryGetTouchpadInfo(pointerId, out var info) || info.pointerInfo.pointerType != NativeMethods.PT_TOUCHPAD)
        {
            return false;
        }

        if (msg == NativeMethods.WM_POINTERUP)
        {
            _contacts.Remove(pointerId);
            if (_contacts.Count < 2)
            {
                var wasActive = IsGestureActive;
                EndGesture();
                return wasActive;
            }

            return IsGestureActive;
        }

        _contacts[pointerId] = (info.pointerInfo.ptHimetricLocation.X, info.pointerInfo.ptHimetricLocation.Y);
        if (_contacts.Count < 2)
        {
            return false;
        }

        return UpdateGesture(config);
    }

    private bool UpdateGesture(RexConfig config)
    {
        var pair = _contacts.OrderBy(x => x.Key).Take(2).Select(x => x.Value).ToArray();
        var centroid = ((pair[0].X + pair[1].X) / 2, (pair[0].Y + pair[1].Y) / 2);
        var distance = Math.Max(1, Math.Sqrt(Math.Pow(pair[1].X - pair[0].X, 2) + Math.Pow(pair[1].Y - pair[0].Y, 2)));

        if (_kind == GestureKind.None)
        {
            _startCentroid = centroid;
            _lastCentroid = centroid;
            _startDistance = distance;
            _startZoom = _host.Zoom;

            var altDown = NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
            if (altDown && config.Zoom.Enabled && config.Zoom.PinchZoom)
            {
                _kind = GestureKind.Host;
                return true;
            }

            if (!altDown && config.Touchpad.TwoFingerToAndroid && _injector.IsAvailable)
            {
                _kind = GestureKind.Android;
                BeginAndroid();
                return true;
            }

            return false;
        }

        if (_kind == GestureKind.Host)
        {
            var scale = ZoomMath.SensitivityAdjustedScale(distance / _startDistance, HostZoomSensitivity);
            var viewport = _host.ViewportScreenRect;
            NativeMethods.GetCursorPos(out var cursor);
            var anchorX = Math.Clamp(cursor.X - viewport.Left, 0, Math.Max(0, viewport.Width));
            var anchorY = Math.Clamp(cursor.Y - viewport.Top, 0, Math.Max(0, viewport.Height));
            _host.SetZoom(_startZoom * scale, anchorX, anchorY);

            var pan = ToPixels(centroid.Item1 - _lastCentroid.X, centroid.Item2 - _lastCentroid.Y, config.Touchpad.Sensitivity);
            _host.Pan(pan.X, pan.Y);
            _lastCentroid = centroid;
            return true;
        }

        // Android: place two phone fingers around the gesture origin and move them with the real ones.
        var scaleFactor = PixelsPerHimetric(config.Touchpad.Sensitivity);
        var offsets = pair.Select(p => ((p.X - _startCentroid.X) * scaleFactor, (p.Y - _startCentroid.Y) * scaleFactor)).ToArray();
        var drift = ((centroid.Item1 - _startCentroid.X) * scaleFactor, (centroid.Item2 - _startCentroid.Y) * scaleFactor);
        var surface = _host.SurfaceScreenRect;

        var firstScreen = Clamp(surface, _touchCenter.X + drift.Item1 + offsets[0].Item1, _touchCenter.Y + drift.Item2 + offsets[0].Item2);
        var secondScreen = Clamp(surface, _touchCenter.X + drift.Item1 + offsets[1].Item1, _touchCenter.Y + drift.Item2 + offsets[1].Item2);
        if (!_injector.Move(firstScreen, secondScreen))
        {
            _log($"Touch injection failed (error {_injector.LastError}); releasing synthetic contacts and falling back to plain mouse.");
            EndGesture();
            return false;
        }

        _firstScreen = firstScreen;
        _secondScreen = secondScreen;
        return true;
    }

    private void BeginAndroid()
    {
        var surface = _host.SurfaceScreenRect;
        _cursorSaved = NativeMethods.GetCursorPos(out _savedCursor);

        // Start the phone fingers where the mouse is (inside the mirror), else at the mirror centre.
        var centerX = (surface.Left + surface.Right) / 2;
        var centerY = (surface.Top + surface.Bottom) / 2;
        if (_cursorSaved && _savedCursor.X > surface.Left && _savedCursor.X < surface.Right && _savedCursor.Y > surface.Top && _savedCursor.Y < surface.Bottom)
        {
            centerX = _savedCursor.X;
            centerY = _savedCursor.Y;
        }

        _touchCenter = (centerX, centerY);
        _host.FocusChild();
    }

    private void EndGesture()
    {
        if (_kind == GestureKind.Android)
        {
            if (!_injector.Release(_firstScreen, _secondScreen))
            {
                _log($"Touch release failed (error {_injector.LastError}); local contact state was reset.");
            }

            if (_cursorSaved)
            {
                NativeMethods.SetCursorPos(_savedCursor.X, _savedCursor.Y);
            }
        }

        _kind = GestureKind.None;
        _cursorSaved = false;
    }

    private static (int X, int Y) Clamp(RECT rect, double x, double y) =>
        ((int)Math.Clamp(x, rect.Left + 1, Math.Max(rect.Left + 1, rect.Right - 2)),
         (int)Math.Clamp(y, rect.Top + 1, Math.Max(rect.Top + 1, rect.Bottom - 2)));

    private double PixelsPerHimetric(double sensitivity) =>
        PixelsPerMillimetre * sensitivity * DpiScale() / 100.0;

    private (double X, double Y) ToPixels(double dxHimetric, double dyHimetric, double sensitivity)
    {
        var factor = PixelsPerHimetric(sensitivity);
        return (dxHimetric * factor, dyHimetric * factor);
    }

    private double DpiScale()
    {
        var dpi = _host.ViewportHandle == IntPtr.Zero ? 96 : NativeMethods.GetDpiForWindow(_host.ViewportHandle);
        return dpi <= 0 ? 1.0 : dpi / 96.0;
    }
}
