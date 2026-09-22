using System.IO;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Native;
using Rex.Mirror.Services;

namespace Rex.Mirror.Mirror;

/// <summary>
/// Draws a nine-dot guide over the mirror when a pattern-locked phone renders its secure lock
/// screen as black. Geometry comes from Android's own UI hierarchy when it exposes the pattern
/// view, from a saved per-device calibration, or from an estimate. The unlock path is never read,
/// stored or replayed: input still goes straight to scrcpy.
/// </summary>
public sealed class PatternGuide : IDisposable
{
    private const int ManualShowSeconds = 20;
    private const double StepPixels = 8;
    private const double FineStepPixels = 1;

    private readonly AppHost _host;
    private readonly MirrorHost _mirror;
    private readonly OverlayWindow _overlay;
    private readonly AdbClient _adb;
    private readonly string _serial;
    private readonly DeviceIdentity _identity;
    private readonly DispatcherTimer _frame;
    private readonly DispatcherTimer _keyguardTimer;
    private readonly DispatcherTimer _discoveryTimer;

    private KeyguardState _keyguard = KeyguardState.Unknown;
    private bool? _manualOverride;
    private DateTime _manualUntil;
    private PatternGeometryInfo? _discovered;
    private PatternGeometryInfo? _calibration;
    private PatternBounds? _draft;
    private PatternLayout? _lastLayout;
    private bool _pollingKeyguard;
    private bool _discovering;
    private bool _leftWasDown;
    private DateTime _trailClearAt;
    private bool _disposed;

    public PatternGuide(AppHost host, MirrorHost mirror, OverlayWindow overlay, AdbClient adb, string serial, DeviceIdentity identity)
    {
        _host = host;
        _mirror = mirror;
        _overlay = overlay;
        _adb = adb;
        _serial = serial;
        _identity = identity;
        _calibration = PatternGeometry.FromCalibration(host.State.GetDevice(serial)?.Calibration);

        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _frame.Tick += (_, _) => Frame();
        _keyguardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _keyguardTimer.Tick += async (_, _) => await PollKeyguardAsync().ConfigureAwait(true);
        _discoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _discoveryTimer.Tick += async (_, _) => await DiscoverAsync().ConfigureAwait(true);
    }

    public bool IsVisible { get; private set; }
    public bool IsCalibrating => _draft is not null;
    public string Source => _lastLayout?.Source ?? PatternGeometry.SourceUnavailable;

    public event Action? Changed;

    public void Start()
    {
        _frame.Start();
        if (_host.Config.PatternGuide.AutoShowOnKeyguard)
        {
            _keyguardTimer.Start();
            _ = PollKeyguardAsync();
        }

        _discoveryTimer.Start();
    }

    public void Toggle()
    {
        if (IsCalibrating)
        {
            return;
        }

        _manualOverride = !IsVisible;
        _manualUntil = DateTime.UtcNow.AddSeconds(ManualShowSeconds);
        Render();
    }

    public void StartCalibration()
    {
        if (!_host.Config.PatternGuide.CalibrationEnabled)
        {
            return;
        }

        if (IsCalibrating)
        {
            SaveCalibration();
            return;
        }

        Render();
        var layout = _lastLayout;
        if (layout is null || layout.Points.Count != 9)
        {
            return;
        }

        var bounds = PatternGeometry.BoundsFromPoints(layout.Points, layout.ContentRect);
        if (bounds is null)
        {
            return;
        }

        _draft = bounds;
        _manualOverride = true;
        _manualUntil = DateTime.MaxValue;
        Render();
    }

    /// <summary>Handles a calibration key. Returns true when consumed.</summary>
    public bool HandleCalibrationKey(int virtualKey, bool ctrl, bool shift)
    {
        if (_draft is null || _lastLayout is null)
        {
            return false;
        }

        var step = ctrl ? FineStepPixels : StepPixels;
        var content = _lastLayout.ContentRect;
        var stepX = step / Math.Max(1, content.Width);
        var stepY = step / Math.Max(1, content.Height);

        switch (virtualKey)
        {
            case NativeMethods.VK_LEFT:
                _draft = PatternGeometry.Adjust(_draft, shift ? "shrink-width" : "move-left", stepX, stepY);
                break;
            case NativeMethods.VK_RIGHT:
                _draft = PatternGeometry.Adjust(_draft, shift ? "grow-width" : "move-right", stepX, stepY);
                break;
            case NativeMethods.VK_UP:
                _draft = PatternGeometry.Adjust(_draft, shift ? "shrink-height" : "move-up", stepX, stepY);
                break;
            case NativeMethods.VK_DOWN:
                _draft = PatternGeometry.Adjust(_draft, shift ? "grow-height" : "move-down", stepX, stepY);
                break;
            case NativeMethods.VK_RETURN:
                SaveCalibration();
                return true;
            case NativeMethods.VK_ESCAPE:
                CancelCalibration();
                return true;
            case 'R':
                ResetCalibration();
                return true;
            default:
                return false;
        }

        Render();
        return true;
    }

    public void SaveCalibration()
    {
        if (_draft is not null)
        {
            var calibration = new PatternCalibration(_draft.Left, _draft.Top, _draft.Right, _draft.Bottom);
            _host.State.SetCalibration(_serial, calibration);
            _calibration = PatternGeometry.FromCalibration(calibration);
            _host.Log.Info($"Pattern calibration saved for {_serial}.");
        }

        CancelCalibration();
    }

    public void CancelCalibration()
    {
        _draft = null;
        _manualOverride = true;
        _manualUntil = DateTime.UtcNow.AddSeconds(ManualShowSeconds);
        Render();
    }

    public void ResetCalibration()
    {
        _host.State.SetCalibration(_serial, null);
        _calibration = null;
        CancelCalibration();
    }

    // ----- Timers -----

    private void Frame()
    {
        if (_disposed)
        {
            return;
        }

        if (_manualOverride is not null && DateTime.UtcNow > _manualUntil)
        {
            _manualOverride = null;
        }

        Render();
        UpdateTrail();
    }

    private async Task PollKeyguardAsync()
    {
        if (_pollingKeyguard || _disposed || !_mirror.HasChild)
        {
            return;
        }

        _pollingKeyguard = true;
        try
        {
            var state = await _adb.GetKeyguardStateAsync(_serial).ConfigureAwait(true);
            if (state == KeyguardState.Unlocked && _keyguard != KeyguardState.Unlocked)
            {
                _manualOverride = null;
                _discovered = null;
            }

            _keyguard = state;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _keyguard = KeyguardState.Unknown;
        }
        finally
        {
            _pollingKeyguard = false;
        }
    }

    private async Task DiscoverAsync()
    {
        var wanted = _host.Config.PatternGuide.AutoDiscoverGeometry && !IsCalibrating && ShouldShow() && !NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);
        if (!wanted || _discovering || _disposed)
        {
            return;
        }

        _discovering = true;
        try
        {
            var xml = await _adb.DumpUiHierarchyAsync(_serial).ConfigureAwait(true);
            var geometry = PatternGeometry.FromUiHierarchy(xml);
            if (geometry is not null)
            {
                _discovered = geometry;
                Render();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Discovery is best effort; the estimate or calibration stays in place.
        }
        finally
        {
            _discovering = false;
        }
    }

    private bool ShouldShow()
    {
        if (_manualOverride is { } manual)
        {
            return manual;
        }

        return _host.Config.PatternGuide.AutoShowOnKeyguard && _keyguard == KeyguardState.Locked;
    }

    private void Render()
    {
        var show = ShouldShow() && _mirror.HasChild;
        if (!show)
        {
            if (IsVisible)
            {
                IsVisible = false;
                _overlay.ClearPattern();
                Changed?.Invoke();
            }

            return;
        }

        var surface = _mirror.SurfaceRect;
        var geometry = _draft is not null
            ? new PatternGeometryInfo("calibration-draft", 0, 0, false, _draft)
            : PatternGeometry.Effective(_discovered, _calibration);

        var layout = PatternGeometry.Layout(geometry, surface.Width, surface.Height, _identity.DisplayWidth, _identity.DisplayHeight, allowEstimate: true);
        _lastLayout = layout;

        var points = layout.Points.Select(p => new PointD(p.X + surface.X, p.Y + surface.Y)).ToArray();
        var radius = Math.Max(5, layout.ContentRect.Width * PatternGeometry.DotRadiusRelativeToWidth);
        var label = IsCalibrating
            ? "CALIBRATE  ·  arrows move  ·  Shift+arrows resize  ·  Ctrl = fine  ·  Enter save  ·  Esc cancel  ·  R reset"
            : $"PATTERN GUIDE  ·  {SourceLabel(layout.Source)}  ·  Ctrl+Alt+P hide  ·  Ctrl+Alt+C calibrate";

        _overlay.DrawPattern(points, radius, _host.Config.PatternGuide.Opacity, label, IsCalibrating);
        if (!IsVisible)
        {
            IsVisible = true;
            Changed?.Invoke();
        }
    }

    private void UpdateTrail()
    {
        if (!IsVisible || !_host.Config.PatternGuide.ShowCursorTrail)
        {
            return;
        }

        var leftDown = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);
        if (leftDown)
        {
            var viewport = _mirror.ViewportScreenRect;
            if (NativeMethods.GetCursorPos(out var cursor) &&
                cursor.X >= viewport.Left && cursor.X < viewport.Right && cursor.Y >= viewport.Top && cursor.Y < viewport.Bottom)
            {
                _overlay.AddTrailPoint(cursor.X - viewport.Left, cursor.Y - viewport.Top);
            }
        }
        else if (_leftWasDown)
        {
            _trailClearAt = DateTime.UtcNow.AddMilliseconds(350);
        }
        else if (DateTime.UtcNow >= _trailClearAt)
        {
            _overlay.ClearTrail();
        }

        _leftWasDown = leftDown;
    }

    private static string SourceLabel(string source) => source switch
    {
        PatternGeometry.SourceUiDots => "Android dot bounds",
        PatternGeometry.SourceUiView => "Android pattern view",
        PatternGeometry.SourceCalibration => "saved calibration",
        "calibration-draft" => "calibration",
        _ => "estimated",
    };

    public void Dispose()
    {
        _disposed = true;
        _frame.Stop();
        _keyguardTimer.Stop();
        _discoveryTimer.Stop();
        _overlay.ClearPattern();
    }
}
