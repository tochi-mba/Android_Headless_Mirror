namespace Rex.Core;

/// <summary>
/// Where a tour's spotlight and its callout go.
///
/// The window is whatever size the person left it, and the thing being pointed at can be anywhere
/// in it, so the callout has to choose a side that actually has room and then stay inside the
/// window whatever happens. Keeping that decision here, away from WPF, means every awkward case
/// (a target in the corner, a window barely bigger than the callout) is a test rather than a thing
/// to reproduce by hand.
/// </summary>
public static class TourLayout
{
    /// <summary>The gap between the spotlight and the callout.</summary>
    public const double Gap = 14;

    /// <summary>How close to the window edge anything may sit.</summary>
    public const double Margin = 16;

    /// <summary>The sides a callout may take, in the order they are tried.</summary>
    public static readonly string[] Sides = ["bottom", "top", "right", "left"];

    /// <summary>The lit area around a target: its rectangle, opened up a little so it breathes.</summary>
    public static RectD Spotlight(RectD target, double padding)
    {
        var pad = Math.Max(0, padding);
        return new RectD(target.X - pad, target.Y - pad, Math.Max(0, target.Width) + (pad * 2), Math.Max(0, target.Height) + (pad * 2));
    }

    /// <summary>
    /// True when a target is worth pointing at: it has to be big enough to see and actually be
    /// inside the window. A step whose target has been squeezed out by a small window is skipped
    /// rather than pointed at from across the screen.
    /// </summary>
    public static bool Fits(RectD target, double windowWidth, double windowHeight) =>
        target.Width >= 12 && target.Height >= 12 &&
        target.X < windowWidth && target.Y < windowHeight &&
        target.X + target.Width > 0 && target.Y + target.Height > 0;

    /// <summary>
    /// Places the callout beside the spotlight. The preferred side is taken when it fits; otherwise
    /// the other sides are tried in turn, and if none fit the preferred side is used and pulled
    /// back inside the window. The side that was actually used comes back with the rectangle so the
    /// callout can point the right way.
    /// </summary>
    public static (RectD Rect, string Side) Callout(
        RectD spotlight, double calloutWidth, double calloutHeight,
        double windowWidth, double windowHeight, string preferredSide)
    {
        var order = new List<string> { Normalize(preferredSide) };
        order.Add(Opposite(order[0]));
        order.AddRange(Sides.Where(side => !order.Contains(side, StringComparer.Ordinal)));

        foreach (var side in order)
        {
            var rect = Place(spotlight, calloutWidth, calloutHeight, side);
            if (Inside(rect, windowWidth, windowHeight))
            {
                return (Clamp(rect, calloutWidth, calloutHeight, windowWidth, windowHeight), side);
            }
        }

        var fallback = Place(spotlight, calloutWidth, calloutHeight, order[0]);
        return (Clamp(fallback, calloutWidth, calloutHeight, windowWidth, windowHeight), order[0]);
    }

    private static RectD Place(RectD spotlight, double width, double height, string side)
    {
        var centreX = spotlight.X + (spotlight.Width / 2) - (width / 2);
        var centreY = spotlight.Y + (spotlight.Height / 2) - (height / 2);
        return side switch
        {
            "top" => new RectD(centreX, spotlight.Y - Gap - height, width, height),
            "left" => new RectD(spotlight.X - Gap - width, centreY, width, height),
            "right" => new RectD(spotlight.X + spotlight.Width + Gap, centreY, width, height),
            _ => new RectD(centreX, spotlight.Y + spotlight.Height + Gap, width, height),
        };
    }

    private static bool Inside(RectD rect, double windowWidth, double windowHeight) =>
        rect.X >= Margin && rect.Y >= Margin &&
        rect.X + rect.Width <= windowWidth - Margin &&
        rect.Y + rect.Height <= windowHeight - Margin;

    private static RectD Clamp(RectD rect, double width, double height, double windowWidth, double windowHeight)
    {
        var maxX = Math.Max(Margin, windowWidth - width - Margin);
        var maxY = Math.Max(Margin, windowHeight - height - Margin);
        return new RectD(
            Math.Clamp(rect.X, Math.Min(Margin, maxX), maxX),
            Math.Clamp(rect.Y, Math.Min(Margin, maxY), maxY),
            width,
            height);
    }

    private static string Normalize(string side) =>
        Sides.Contains(side, StringComparer.Ordinal) ? side : "bottom";

    private static string Opposite(string side) => side switch
    {
        "top" => "bottom",
        "bottom" => "top",
        "left" => "right",
        _ => "left",
    };
}
