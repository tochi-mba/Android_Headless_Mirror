namespace Rex.Core;

/// <summary>
/// Where the fullscreen HUD sits and which strip of the screen brings it back. The zone always
/// touches the same edge or corner the HUD is pinned to, so a user reaches for the place they put
/// it and it appears.
/// </summary>
public static class HudLayout
{
    /// <summary>How deep the hover strip reaches into the picture, in device-independent pixels.</summary>
    public const double ZoneDepth = 14;

    /// <summary>How wide a corner zone is along each edge it touches.</summary>
    public const double CornerReach = 220;

    public static bool IsTop(string position) => position.StartsWith("top", StringComparison.Ordinal);
    public static bool IsBottom(string position) => position.StartsWith("bottom", StringComparison.Ordinal);
    public static bool IsLeft(string position) => position.EndsWith("left", StringComparison.Ordinal);
    public static bool IsRight(string position) => position.EndsWith("right", StringComparison.Ordinal);

    /// <summary>The HUD's top-left corner for a viewport of this size.</summary>
    public static (double Left, double Top) Anchor(string position, double hudWidth, double hudHeight, double viewportWidth, double viewportHeight, double margin)
    {
        var left = position switch
        {
            _ when IsLeft(position) => margin,
            _ when IsRight(position) => viewportWidth - hudWidth - margin,
            _ => (viewportWidth - hudWidth) / 2,
        };

        var top = position switch
        {
            _ when IsTop(position) => margin,
            _ when IsBottom(position) => viewportHeight - hudHeight - margin,
            _ => (viewportHeight - hudHeight) / 2,
        };

        return (Math.Max(0, left), Math.Max(0, top));
    }

    /// <summary>The area that reveals the HUD when the pointer enters it.</summary>
    public static RectD HoverZone(string position, double viewportWidth, double viewportHeight)
    {
        var depth = Math.Min(ZoneDepth, Math.Min(viewportWidth, viewportHeight) / 2);
        var reach = Math.Min(CornerReach, viewportWidth);
        var vertical = Math.Min(CornerReach, viewportHeight);

        // A corner only listens near that corner; an edge listens along its whole length.
        return position switch
        {
            "top-left" => new RectD(0, 0, reach, vertical),
            "top-right" => new RectD(viewportWidth - reach, 0, reach, vertical),
            "bottom-left" => new RectD(0, viewportHeight - vertical, reach, vertical),
            "bottom-right" => new RectD(viewportWidth - reach, viewportHeight - vertical, reach, vertical),
            "bottom" => new RectD(0, viewportHeight - depth, viewportWidth, depth),
            "left" => new RectD(0, 0, depth, viewportHeight),
            "right" => new RectD(viewportWidth - depth, 0, depth, viewportHeight),
            _ => new RectD(0, 0, viewportWidth, depth),
        };
    }

    public static bool Contains(RectD zone, double x, double y) =>
        x >= zone.X && x < zone.X + zone.Width && y >= zone.Y && y < zone.Y + zone.Height;

    /// <summary>A corner or side HUD stacks its buttons vertically; a top or bottom bar lays them out in a row.</summary>
    public static bool IsVertical(string position) => position is "left" or "right";
}
