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
    private PatternBounds? _draftOrigin;
    private string _draftOriginSource = string.Empty;
    private PatternLayout? _lastLayout;
    private IReadOnlyList<PointD> _patternPoints = [];
    private readonly List<int> _traceNodes = [];
    private PointD? _tracePointer;
    private double _patternRadius;
    private bool _initialDiscoveryComplete;
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
        _initialDiscoveryComplete = _calibration is not null || !host.Config.PatternGuide.AutoDiscoverGeometry;

        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _frame.Tick += (_, _) => Frame();
        _keyguardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _keyguardTimer.Tick += async (_, _) => await PollKeyguardAsync().ConfigureAwait(true);
        _discoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _discoveryTimer.Tick += async (_, _) => await DiscoverAsync().ConfigureAwait(true);
    }

    public bool IsVisible { get; private set; }
    public bool IsCalibrating => _draft is not null;
    public bool IsResolving => IsVisible && NeedsInitialDiscovery();
    public string Source => _lastLayout?.Source ?? PatternGeometry.SourceUnavailable;

    /// <summary>Where the nine dots sit as fractions of the picture, or null while nothing is drawn.</summary>
    public PatternBounds? NormalizedBounds { get; private set; }

    private bool IsLandscape => _mirror.SurfaceRect.Width > _mirror.SurfaceRect.Height;

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
        if (_manualOverride == true)
        {
            _ = DiscoverAsync(initial: NeedsInitialDiscovery());
        }
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
        _draftOrigin = bounds;
        _draftOriginSource = layout.Source;
        _manualOverride = true;
        _manualUntil = DateTime.MaxValue;
        Render();
    }

    /// <summary>
    /// Cheap classification used by the low-level keyboard hook. Actual calibration work
    /// is dispatched to the UI thread after the hook has returned.
    /// </summary>
    public bool CanHandleCalibrationKey(int virtualKey) =>
        _draft is not null &&
        _lastLayout is not null &&
        virtualKey is NativeMethods.VK_LEFT or NativeMethods.VK_RIGHT or NativeMethods.VK_UP or
            NativeMethods.VK_DOWN or NativeMethods.VK_RETURN or NativeMethods.VK_ESCAPE or 'R';

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
            if (_draftOrigin is not null && _draftOriginSource != PatternGeometry.SourceCalibration &&
                PatternGeometry.SameBounds(_draft, _draftOrigin))
            {
                // Nothing was moved, so there is nothing to remember. Saving the automatic
                // placement as a calibration would freeze today's guess and stop the guide
                // following Android the next time the lock screen comes up.
                _host.State.SetCalibration(_serial, null);
                _calibration = null;
                _host.Log.Info($"Pattern guide for {_serial} stays automatic: calibration was saved without moving it.");
            }
            else
            {
                var calibration = new PatternCalibration(_draft.Left, _draft.Top, _draft.Right, _draft.Bottom, IsLandscape);
                _host.State.SetCalibration(_serial, calibration);
                _calibration = PatternGeometry.FromCalibration(calibration);
                _host.Log.Info($"Pattern calibration saved for {_serial} ({(IsLandscape ? "landscape" : "portrait")}).");
            }
        }

        CancelCalibration();
    }

    public void CancelCalibration()
    {
        _draft = null;
        _draftOrigin = null;
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

        UpdateTrail();
        Render();
        DrawTrace();
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
            var previous = _keyguard;
            if (state == KeyguardState.Unlocked && previous != KeyguardState.Unlocked)
            {
                _manualOverride = null;
                _discovered = null;
                _initialDiscoveryComplete = _calibration is not null || !_host.Config.PatternGuide.AutoDiscoverGeometry;
            }

            _keyguard = state;
            if (state == KeyguardState.Locked && previous != KeyguardState.Locked)
            {
                _initialDiscoveryComplete = _calibration is not null || !_host.Config.PatternGuide.AutoDiscoverGeometry;
                Render();
                // Ask Android where the pattern is every time the lock screen comes up, whether or
                // not a calibration is saved: exact dot bounds beat any calibration, and the
                // three-second timer would otherwise leave the old placement showing until it ran.
                _ = DiscoverAsync(initial: NeedsInitialDiscovery());
            }
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

    private async Task DiscoverAsync(bool initial = false)
    {
        var wanted = _host.Config.PatternGuide.AutoDiscoverGeometry && !IsCalibrating && ShouldShow() && !NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);
        if (!wanted || _discovering || _disposed)
        {
            return;
        }

        _discovering = true;
        if (initial)
        {
            Render();
        }

        try
        {
            var attempts = initial ? 3 : 1;
            for (var attempt = 0; attempt < attempts && !_disposed; attempt++)
            {
                var xml = await _adb.DumpUiHierarchyAsync(_serial).ConfigureAwait(true);
                var geometry = PatternGeometry.FromUiHierarchy(xml);
                if (geometry is not null)
                {
                    _discovered = geometry;
                    break;
                }

                if (attempt + 1 < attempts)
                {
                    await Task.Delay(350).ConfigureAwait(true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // Discovery is best effort; the estimate or calibration stays in place.
        }
        finally
        {
            if (initial)
            {
                _initialDiscoveryComplete = true;
            }

            _discovering = false;
            Render();
        }
    }

    private bool NeedsInitialDiscovery() =>
        _host.Config.PatternGuide.AutoDiscoverGeometry &&
        !IsCalibrating &&
        _calibration is null &&
        _discovered is null &&
        !_initialDiscoveryComplete;

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
                NormalizedBounds = null;
                _overlay.ClearPattern();
                ResetTrace();
                Changed?.Invoke();
            }

            return;
        }

        if (NeedsInitialDiscovery())
        {
            _lastLayout = null;
            NormalizedBounds = null;
            _patternPoints = [];
            _overlay.ShowPatternLoading("Finding the pattern position…");
            if (!IsVisible)
            {
                IsVisible = true;
                Changed?.Invoke();
            }

            return;
        }

        // The overlay covers the whole mirror area; the main view may be one cell of it when the
        // phone has copies beside it, so the dots are placed on the main view's picture there.
        var surface = _mirror.AreaSurfaceRect;
        var geometry = _draft is not null
            ? new PatternGeometryInfo("calibration-draft", 0, 0, false, _draft)
            : PatternGeometry.Effective(_discovered, _calibration, IsLandscape);

        var layout = PatternGeometry.Layout(geometry, surface.Width, surface.Height, _identity.DisplayWidth, _identity.DisplayHeight, allowEstimate: true);
        _lastLayout = layout;
        NormalizedBounds = PatternGeometry.BoundsFromPoints(layout.Points, new RectD(0, 0, surface.Width, surface.Height));

        var points = layout.Points.Select(p => new PointD(p.X + surface.X, p.Y + surface.Y)).ToArray();
        var radius = Math.Max(5, layout.ContentRect.Width * PatternGeometry.DotRadiusRelativeToWidth * _host.Config.PatternGuide.DotSize);
        _patternPoints = points;
        _patternRadius = radius;
        var label = IsCalibrating
            ? "CALIBRATE  ·  arrows move  ·  Shift+arrows resize  ·  Ctrl = fine  ·  Enter save  ·  Esc cancel  ·  R reset"
            : $"PATTERN GUIDE  ·  {SourceLabel(layout.Source)}  ·  {Shortcuts.Gesture("pattern-guide")} hide  ·  {Shortcuts.Gesture("pattern-calibrate")} calibrate";

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
            ResetTrace();
            return;
        }

        var leftDown = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);
        if (leftDown)
        {
            if (!_leftWasDown)
            {
                _traceNodes.Clear();
                _tracePointer = null;
            }

            var viewport = _mirror.ViewportScreenRect;
            if (NativeMethods.GetCursorPos(out var cursor) &&
                cursor.X >= viewport.Left && cursor.X < viewport.Right && cursor.Y >= viewport.Top && cursor.Y < viewport.Bottom)
            {
                var area = _overlay.ScreenPixels;
                var pointer = new PointD(cursor.X - area.Left, cursor.Y - area.Top);
                var hit = PatternPath.HitTest(_patternPoints, pointer, Math.Max(18, _patternRadius * 2.4));
                if (hit is { } index)
                {
                    var updated = PatternPath.AddNode(_traceNodes, index);
                    _traceNodes.Clear();
                    _traceNodes.AddRange(updated);
                }

                _tracePointer = _traceNodes.Count > 0 ? pointer : null;
            }
        }
        else if (_leftWasDown)
        {
            _tracePointer = null;
            _trailClearAt = DateTime.UtcNow.AddMilliseconds(350);
        }
        else if (DateTime.UtcNow >= _trailClearAt)
        {
            ResetTrace();
        }

        _leftWasDown = leftDown;
    }

    private void DrawTrace() =>
        _overlay.UpdatePatternTrace(_patternPoints, _traceNodes, _tracePointer, _host.Config.PatternGuide.Opacity);

    private void ResetTrace()
    {
        _traceNodes.Clear();
        _tracePointer = null;
        _overlay.ClearTrail();
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
