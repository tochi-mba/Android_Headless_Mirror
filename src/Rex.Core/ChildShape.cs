namespace Rex.Core;

/// <summary>What to do about the size scrcpy's window has, compared with what the app gave it.</summary>
public enum ChildShapeAction
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>The size is ours but the window moved: put it back where it belongs.</summary>
    Reposition,

    /// <summary>The window changed size by itself, but the video's shape is known: give it ours back.</summary>
    Reassert,

    /// <summary>The window changed size by itself and nothing else says why: take its shape as the video's.</summary>
    Adopt,
}

/// <summary>
/// Reads scrcpy's window size as a signal about the picture, carefully.
///
/// scrcpy resizes its own window when the video changes shape, so a size nobody asked for used to
/// be taken as the phone turning. That misread two ordinary cases: a window that the app has not
/// laid out yet still has the size it opened with (from the launch rectangle, or scrcpy's own
/// default), and that shape was adopted as the video's, leaving the picture stretched or
/// letterboxed for the rest of the session. So a size only counts against the size the app last
/// applied, and once scrcpy has reported the video's size ("INFO: Texture: WxH"), that report is
/// the only word on the shape and a window that drifts is simply given its size back.
/// </summary>
public static class ChildShape
{
    /// <param name="actual">The window's size now.</param>
    /// <param name="applied">The size the app last gave the window, or null before its first layout reached it.</param>
    /// <param name="positioned">Whether the window is where the app put it.</param>
    /// <param name="videoReported">Whether scrcpy has reported the video's size for this window.</param>
    public static ChildShapeAction Decide((int Width, int Height) actual, (int Width, int Height)? applied, bool positioned, bool videoReported)
    {
        if (actual.Width <= 0 || actual.Height <= 0 || applied is not { } ours)
        {
            return ChildShapeAction.None;
        }

        if (Math.Abs(actual.Width - ours.Width) <= 1 && Math.Abs(actual.Height - ours.Height) <= 1)
        {
            return positioned ? ChildShapeAction.None : ChildShapeAction.Reposition;
        }

        return videoReported ? ChildShapeAction.Reassert : ChildShapeAction.Adopt;
    }
}
