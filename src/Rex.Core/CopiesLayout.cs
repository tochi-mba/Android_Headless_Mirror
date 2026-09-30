namespace Rex.Core;

/// <summary>
/// Where copies of the phone go in the mirror area, and how many fit.
///
/// A copy is a second live view of the same phone, fully controllable, shown beside the first.
/// They are for a phone held upright: side by side at full height, as many as the width holds, with
/// the same gap between each. A phone on its side fills the width on its own, so its copies wait
/// out of sight until it is upright again. All of it is plain geometry in whatever unit the caller
/// uses, so it is tested here rather than on a screen.
/// </summary>
public static class CopiesLayout
{
    /// <summary>The fewest pixels a copy may be wide before it is too small to use.</summary>
    public const double MinimumCellWidth = 120;

    /// <summary>True for a picture taller than it is wide: the only shape copies are shown for.</summary>
    public static bool IsUpright(double aspect) => aspect is > 0 and < 1;

    /// <summary>
    /// How many views (the phone and its copies) fit side by side at the full height of the area,
    /// never more than <paramref name="most"/> and never fewer than one. A phone on its side gets one.
    /// </summary>
    public static int Capacity(double areaWidth, double areaHeight, double aspect, double gap, int most)
    {
        if (!IsUpright(aspect) || areaWidth <= 0 || areaHeight <= 0)
        {
            return 1;
        }

        var cell = areaHeight * aspect;
        var fit = (int)Math.Floor((areaWidth + gap) / (cell + gap));
        return Math.Clamp(fit, 1, Math.Max(1, most));
    }

    /// <summary>
    /// The rectangles for <paramref name="count"/> views of the phone: one fills the area (so zoom
    /// has all of it to work with), several share it as equal cells, centred, the same gap apart,
    /// shrunk together if the area has become too narrow for them at full height.
    /// </summary>
    public static IReadOnlyList<RectD> Cells(double areaWidth, double areaHeight, double aspect, int count, double gap)
    {
        if (areaWidth <= 0 || areaHeight <= 0)
        {
            return [new RectD(0, 0, 0, 0)];
        }

        if (count <= 1 || !IsUpright(aspect))
        {
            return [new RectD(0, 0, areaWidth, areaHeight)];
        }

        var spare = Math.Max(0, gap) * (count - 1);
        var height = Math.Min(areaHeight, (areaWidth - spare) / (count * aspect));
        var width = height * aspect;
        var total = width * count + spare;
        var left = (areaWidth - total) / 2;
        var top = (areaHeight - height) / 2;
        var cells = new RectD[count];
        for (var i = 0; i < count; i++)
        {
            cells[i] = new RectD(left + i * (width + Math.Max(0, gap)), top, width, height);
        }

        return cells;
    }

    /// <summary>
    /// How many of <paramref name="views"/> can be shown when the area has become too narrow for
    /// all of them at the minimum usable width: the rest keep running out of sight until there is
    /// room again, rather than every copy being squeezed into a sliver.
    /// </summary>
    public static int ShownCount(int views, double areaWidth, double gap)
    {
        if (views <= 1)
        {
            return 1;
        }

        var fit = (int)Math.Floor((Math.Max(0, areaWidth) + Math.Max(0, gap)) / (MinimumCellWidth + Math.Max(0, gap)));
        return Math.Clamp(fit, 1, views);
    }

    /// <summary>
    /// Whether another copy can be added: the phone is upright and one more view still fits at
    /// full height. The answer to "why not" is <see cref="WhyNoMore"/>.
    /// </summary>
    public static bool CanAdd(int views, double areaWidth, double areaHeight, double aspect, double gap, int most) =>
        views < Capacity(areaWidth, areaHeight, aspect, gap, most);

    /// <summary>
    /// The point in one view that matches a point in another: the same place relative to each
    /// view's own rectangle. Every copy shows the same phone at the same zoom as the main view, so a
    /// gesture over a copy lands at the same spot on the phone when it is played on the main view.
    /// </summary>
    public static (int X, int Y) MapPoint(int x, int y, int fromLeft, int fromTop, int fromWidth, int fromHeight, int toLeft, int toTop, int toWidth, int toHeight)
    {
        var fractionX = fromWidth > 0 ? Math.Clamp((double)(x - fromLeft) / fromWidth, 0, 1) : 0.5;
        var fractionY = fromHeight > 0 ? Math.Clamp((double)(y - fromTop) / fromHeight, 0, 1) : 0.5;
        return (toLeft + (int)Math.Round(fractionX * toWidth), toTop + (int)Math.Round(fractionY * toHeight));
    }

    /// <summary>What to tell someone who cannot add another copy, or null when they can.</summary>
    public static string? WhyNoMore(int views, double areaWidth, double areaHeight, double aspect, double gap, int most)
    {
        if (!IsUpright(aspect))
        {
            return "Copies are for an upright phone. Turn it back to portrait to add one.";
        }

        if (views >= Math.Max(1, most))
        {
            return $"That is the most copies set in Settings ({Math.Max(1, most) - 1}).";
        }

        return CanAdd(views, areaWidth, areaHeight, aspect, gap, most)
            ? null
            : "No room for another copy at full height. Make the window wider, or go fullscreen.";
    }
}
