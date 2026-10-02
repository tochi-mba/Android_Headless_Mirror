namespace Rex.Core;

/// <summary>Where the phone's view and the second screen go: a rectangle each, or none for one that is not shown.</summary>
/// <param name="Stacked">One above the other rather than side by side.</param>
/// <param name="Squeezed">Beside was asked for, but there was no room for both, so only the second screen shows.</param>
public sealed record ViewsArrangement(RectD? Phone, RectD Screen, bool Stacked, bool Squeezed);

/// <summary>
/// How the phone and a second screen share the mirror area. The phone keeps its own shape at the
/// full height (side by side) or the full width (one above the other); the second screen takes the
/// rest. A split from the splitter moves the line between them. When there is too little room for
/// the second screen's minimum, the phone gives way first, down to the narrowest usable view, and
/// below that the second screen shows alone until there is room again. Plain geometry, in
/// whatever unit the caller uses.
/// </summary>
public static class ViewsLayout
{
    /// <summary>The narrowest the phone's view is made beside a second screen.</summary>
    public const double NarrowestPhone = CopiesLayout.MinimumCellWidth;

    public static ViewsArrangement Arrange(
        double width,
        double height,
        double phoneAspect,
        SecondScreenSettings screen,
        ViewsSettings views,
        double gap,
        double split = 0)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        gap = Math.Max(0, gap);
        var whole = new RectD(0, 0, width, height);
        if (screen.Placement == "instead" || width <= 0 || height <= 0 || phoneAspect <= 0)
        {
            return new ViewsArrangement(null, whole, false, false);
        }

        var stacked = views.Arrangement == "stack" || views.Arrangement == "auto" && phoneAspect >= 1;
        return stacked
            ? Stack(width, height, phoneAspect, screen, gap, split)
            : Side(width, height, phoneAspect, screen, gap, split);
    }

    private static ViewsArrangement Side(double width, double height, double aspect, SecondScreenSettings screen, double gap, double split)
    {
        var minimum = screen.MinWidth;
        if (width < NarrowestPhone + gap + minimum)
        {
            return new ViewsArrangement(null, new RectD(0, 0, width, height), false, true);
        }

        var natural = Math.Min(height * aspect, width - gap - minimum);
        var phoneWidth = split is > 0 and < 1 ? width * split : natural;
        phoneWidth = Math.Clamp(phoneWidth, NarrowestPhone, width - gap - minimum);
        var screenWidth = width - gap - phoneWidth;
        return screen.Side == "left"
            ? new ViewsArrangement(new RectD(screenWidth + gap, 0, phoneWidth, height), new RectD(0, 0, screenWidth, height), false, false)
            : new ViewsArrangement(new RectD(0, 0, phoneWidth, height), new RectD(phoneWidth + gap, 0, screenWidth, height), false, false);
    }

    private static ViewsArrangement Stack(double width, double height, double aspect, SecondScreenSettings screen, double gap, double split)
    {
        // One above the other, the second screen's minimum is the least height it keeps.
        double minimum = screen.MinWidth;
        const double smallest = NarrowestPhone;
        if (height < smallest + gap + minimum)
        {
            return new ViewsArrangement(null, new RectD(0, 0, width, height), true, true);
        }

        var natural = Math.Min(width / aspect, height - gap - minimum);
        var phoneHeight = split is > 0 and < 1 ? height * split : natural;
        phoneHeight = Math.Clamp(phoneHeight, smallest, height - gap - minimum);
        var screenHeight = height - gap - phoneHeight;
        return screen.Side == "left"
            ? new ViewsArrangement(new RectD(0, screenHeight + gap, width, phoneHeight), new RectD(0, 0, width, screenHeight), true, false)
            : new ViewsArrangement(new RectD(0, 0, width, phoneHeight), new RectD(0, phoneHeight + gap, width, screenHeight), true, false);
    }

    /// <summary>The split a splitter dragged to this point leaves: its share of the width (or the height when stacked).</summary>
    public static double SplitAt(double position, double length) => length <= 0 ? 0 : Math.Clamp(position / length, 0.05, 0.95);
}
