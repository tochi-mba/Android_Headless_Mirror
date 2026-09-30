using Rex.Core;
using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// A small, predictable vocabulary of touch gestures for driving the phone without a mouse: the
/// swipes that move through a feed or a set of stories, a tap, and the double-tap that likes.
///
/// While the picture is on screen the gestures are injected as a single finger over the visible
/// scrcpy surface, so they follow rotation, zoom and window placement automatically and land the
/// moment the key goes down. When there is nothing to touch (the window is in the tray, or Windows
/// refuses touch injection) the same gesture is played by Android itself over ADB instead, so a
/// keyboard, the HUD and the command line all get the same result. Ordinary keys are never
/// involved: typing remains typing.
/// </summary>
public sealed class KeyboardTouch
{
    private const int SwipeSteps = 7;
    private const int TapMilliseconds = 55;
    private const int BetweenStrokesMilliseconds = 70;

    /// <summary>How far and how fast swipes go (<see cref="InputSettings.SwipeLength"/>, <see cref="InputSettings.SwipeMilliseconds"/>).</summary>
    public Func<InputSettings>? Settings { get; set; }

    private readonly MirrorHost _host;
    private readonly TouchInjector _injector;
    private readonly Action<string> _log;
    private bool _running;

    public KeyboardTouch(MirrorHost host, TouchInjector injector, Action<string> log)
    {
        _host = host;
        _injector = injector;
        _log = log;
    }

    /// <summary>Plays the gesture over ADB when it cannot be touched in; null means there is no fallback.</summary>
    public Func<string, Task<AndroidResult>>? Fallback { get; set; }

    /// <summary>
    /// Whether the picture is actually on screen. A window hidden in the tray keeps its geometry,
    /// so the surface rectangle alone cannot tell that a touch there would land on nothing.
    /// </summary>
    public Func<bool>? SurfaceOnScreen { get; set; }

    public bool IsRunning => _running;

    public async Task<AndroidResult> RunAsync(string action)
    {
        if (!MirrorActions.IsGesture(action))
        {
            return AndroidResult.Failure($"'{action}' is not a gesture.");
        }

        if (_running || _injector.AnyDown)
        {
            return AndroidResult.Failure("Finish the current touch gesture first.");
        }

        var surface = _host.HasChild
            ? TouchpadBridge.VisibleSurface(_host.SurfaceScreenRect, _host.ViewportScreenRect)
            : default;
        var settings = Settings?.Invoke() ?? new InputSettings();
        var strokes = Strokes(action, surface, settings.SwipeLength);
        if (strokes.Count == 0 || !_injector.IsAvailable || SurfaceOnScreen?.Invoke() == false)
        {
            if (Fallback is null)
            {
                return AndroidResult.Failure(_host.HasChild ? "The phone picture is not visible." : "The mirror is not running.");
            }

            return await Fallback(action).ConfigureAwait(true);
        }

        _running = true;
        try
        {
            _host.FocusChild();
            for (var i = 0; i < strokes.Count; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(BetweenStrokesMilliseconds).ConfigureAwait(true);
                }

                if (!await PlayStrokeAsync(strokes[i], action, StepMilliseconds(settings.SwipeMilliseconds)).ConfigureAwait(true))
                {
                    _log($"Keyboard touch failed (error {_injector.LastError}).");
                    if (i == 0 && Fallback is not null)
                    {
                        // Nothing reached the phone yet, so Android can play the whole gesture itself.
                        return await Fallback(action).ConfigureAwait(true);
                    }

                    return AndroidResult.Failure("Windows could not send that touch to the phone.");
                }
            }

            return AndroidResult.Success(Outcome(action));
        }
        finally
        {
            _running = false;
        }
    }

    /// <summary>The pause between the points of a swipe, so the whole swipe takes about as long as asked.</summary>
    internal static int StepMilliseconds(int swipeMilliseconds) => Math.Max(1, (int)Math.Round((double)swipeMilliseconds / SwipeSteps));

    private async Task<bool> PlayStrokeAsync(IReadOnlyList<(int X, int Y)> stroke, string action, int stepMilliseconds)
    {
        var last = stroke[0];
        try
        {
            foreach (var point in stroke)
            {
                last = point;
                if (!_injector.MoveOne(point))
                {
                    return false;
                }

                await Task.Delay(stroke.Count == 1 ? TapMilliseconds : stepMilliseconds).ConfigureAwait(true);
            }

            return true;
        }
        finally
        {
            if (!_injector.ReleaseOne(last))
            {
                _log($"Keyboard touch release failed (error {_injector.LastError}) after {action}; local contact state was reset.");
            }
        }
    }

    /// <summary>What the status bar says once the gesture has landed.</summary>
    public static string Outcome(string action) => action switch
    {
        "swipe-up" => "Next",
        "swipe-down" => "Previous",
        "swipe-left" => "Next page",
        "swipe-right" => "Previous page",
        "tap" => "Tapped the centre",
        "like" => "Liked",
        _ => action,
    };

    /// <summary>
    /// The finger paths for a gesture inside a rectangle, in that rectangle's pixels: one stroke
    /// per touch, each a list of points from first contact to release. A rectangle too small to
    /// swipe across yields nothing. The same paths serve the visible surface (screen pixels) and
    /// the ADB fallback (phone pixels). <paramref name="length"/> scales how far a swipe travels:
    /// at 1 it crosses 44% of the height, or 56% of the width, through the middle.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<(int X, int Y)>> Strokes(string action, RECT surface, double length = 1)
    {
        if (surface.Width < 4 || surface.Height < 4)
        {
            return [];
        }

        var scale = Math.Clamp(double.IsFinite(length) ? length : 1, InputSettings.SwipeLengthMin, InputSettings.SwipeLengthMax);
        var halfHeight = Math.Min(0.45, 0.22 * scale);
        var halfWidth = Math.Min(0.45, 0.28 * scale);
        var centreX = (surface.Left + surface.Right) / 2;
        var centreY = (surface.Top + surface.Bottom) / 2;
        var top = surface.Top + (int)Math.Round(surface.Height * (0.5 - halfHeight));
        var bottom = surface.Top + (int)Math.Round(surface.Height * (0.5 + halfHeight));
        var left = surface.Left + (int)Math.Round(surface.Width * (0.5 - halfWidth));
        var right = surface.Left + (int)Math.Round(surface.Width * (0.5 + halfWidth));

        var up = Line((centreX, bottom), (centreX, top));
        var leftwards = Line((right, centreY), (left, centreY));
        return action switch
        {
            "swipe-up" => [up],
            "swipe-down" => [up.Reverse().ToArray()],
            "swipe-left" => [leftwards],
            "swipe-right" => [leftwards.Reverse().ToArray()],
            "tap" => [[(centreX, centreY)]],
            "like" => [[(centreX, centreY)], [(centreX, centreY)]],
            _ => [],
        };
    }

    private static IReadOnlyList<(int X, int Y)> Line((int X, int Y) from, (int X, int Y) to) =>
        Enumerable.Range(0, SwipeSteps + 1)
            .Select(step => (from.X + (to.X - from.X) * step / SwipeSteps, from.Y + (to.Y - from.Y) * step / SwipeSteps))
            .ToArray();
}
