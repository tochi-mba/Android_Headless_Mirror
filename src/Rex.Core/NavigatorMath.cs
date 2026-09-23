namespace Rex.Core;

public static class NavigatorMath
{
    /// <summary>
    /// Whether a drag of the view should still be following the pointer.
    ///
    /// The view moves only while the left button is genuinely held, and both the event and the
    /// mouse itself have to agree about that. The release that ends a drag can happen somewhere
    /// the window never hears about - the touchpad bridge consumes the pointer messages it
    /// handles, and a button let go over the phone or another window may never come back as a
    /// mouse event - which used to leave the view following the pointer around the screen with
    /// nothing short of resetting the zoom to get out of it.
    /// </summary>
    public static bool KeepsDragging(bool dragging, bool eventSaysHeld, bool buttonIsDown) =>
        dragging && eventSaysHeld && buttonIsDown;

    public static (double Scale, double X, double Y) Resize(RectD view, int corner, double dx, double dy,
        double minScale = 0.1, double maxScale = 10)
    {
        var sx = corner % 2 == 0 ? -1 : 1;
        var sy = corner / 2 == 0 ? -1 : 1;
        var scale = Math.Clamp(1 + (sx * dx / Math.Max(0.001, view.Width) +
            sy * dy / Math.Max(0.001, view.Height)) / 2, minScale, maxScale);
        return (scale, view.X + view.Width / 2 + sx * view.Width * (scale - 1) / 2,
            view.Y + view.Height / 2 + sy * view.Height * (scale - 1) / 2);
    }
}
