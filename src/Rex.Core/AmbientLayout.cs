namespace Rex.Core;

/// <summary>Geometry for the soft background: where the capture goes and which margins show it.</summary>
public static class AmbientLayout
{
    /// <summary>The capture's on-screen size for the configured scaling and size multiplier.</summary>
    public static (double Width, double Height) ImageSize(AmbientSettings settings, double sourceWidth, double sourceHeight, double areaWidth, double areaHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
        {
            return (0, 0);
        }

        var (width, height) = settings.Scaling switch
        {
            "fit" => Scaled(Math.Min(areaWidth / sourceWidth, areaHeight / sourceHeight)),
            "stretch" => (areaWidth, areaHeight),
            _ => Scaled(Math.Max(areaWidth / sourceWidth, areaHeight / sourceHeight)),
        };
        return (width * settings.Size, height * settings.Size);

        (double, double) Scaled(double scale) => (sourceWidth * scale, sourceHeight * scale);
    }

    /// <summary>An opaque colour for a hue on the fully saturated wheel (used for the tint wash).</summary>
    public static (byte R, byte G, byte B) HueToRgb(double hue)
    {
        var h = ((hue % 360) + 360) % 360 / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch
        {
            0 => (1.0, x, 0.0),
            1 => (x, 1.0, 0.0),
            2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0),
            4 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    /// <summary>Top-left of a navigator of the given size for a corner name, inside the viewport with a margin.</summary>
    public static (double Left, double Top) NavigatorPosition(string corner, double width, double height, double viewportWidth, double viewportHeight, double margin)
    {
        var left = corner.EndsWith("left", StringComparison.Ordinal) ? margin : viewportWidth - width - margin;
        var top = corner.StartsWith("top", StringComparison.Ordinal) ? margin : viewportHeight - height - margin;
        return (Math.Max(0, left), Math.Max(0, top));
    }

    /// <summary>The part of the viewport that may show the background for a placement; the phone is cut out separately.</summary>
    public static RectD Region(string placement, RectD phone, double width, double height)
    {
        var left = Math.Clamp(phone.X, 0, width);
        var right = Math.Clamp(phone.X + phone.Width, 0, width);
        var top = Math.Clamp(phone.Y, 0, height);
        var bottom = Math.Clamp(phone.Y + phone.Height, 0, height);
        return placement switch
        {
            "left" => new RectD(0, 0, left, height),
            "right" => new RectD(right, 0, width - right, height),
            "top" => new RectD(0, 0, width, top),
            "bottom" => new RectD(0, bottom, width, height - bottom),
            _ => new RectD(0, 0, width, height),
        };
    }
}
