namespace Rex.Core;

/// <summary>Where two phones go: the main phone's view, the other's (none when there is no room), and how.</summary>
public sealed record PhonesArrangement(RectD Main, RectD? Other, bool Stacked)
{
    /// <summary>There was not enough room for both, so only the main phone shows.</summary>
    public bool Squeezed => Other is null;
}

/// <summary>
/// Two different phones sharing the mirror area, each in its own shape. Side by side they have
/// the same height; one above the other, the same width; together they are as large as the area
/// allows and sit in its middle. Auto takes whichever leaves the smaller phone larger. When even
/// that makes a phone narrower than a usable view, the other phone gets no room until there is.
/// Plain geometry, in whatever unit the caller uses.
/// </summary>
public static class TwoPhonesLayout
{
    /// <summary>The least either phone's shorter side is made.</summary>
    public const double Narrowest = CopiesLayout.MinimumCellWidth;

    public static PhonesArrangement Arrange(
        double width, double height, double mainAspect, double otherAspect, string side, string arrangement, double gap)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        gap = Math.Max(0, gap);
        var whole = new RectD(0, 0, width, height);
        if (width <= 0 || height <= 0 || mainAspect <= 0 || otherAspect <= 0)
        {
            return new PhonesArrangement(whole, null, false);
        }

        var otherFirst = side == "left";
        var beside = Beside(width, height, mainAspect, otherAspect, gap, otherFirst);
        var stacked = Stacked(width, height, mainAspect, otherAspect, gap, otherFirst);
        var candidates = arrangement switch
        {
            "side" => new[] { beside },
            "stack" => new[] { stacked },
            _ => new[] { beside, stacked },
        };

        var best = candidates
            .Where(Fits)
            .OrderByDescending(SmallerArea)
            .FirstOrDefault();
        return best ?? new PhonesArrangement(whole, null, arrangement == "stack");
    }

    /// <summary>The two phones taken together as one picture, for laying them out beside a second screen.</summary>
    public static double PairAspect(double mainAspect, double otherAspect, bool stacked) =>
        mainAspect <= 0 || otherAspect <= 0 ? Math.Max(mainAspect, 0)
        : stacked ? 1 / (1 / mainAspect + 1 / otherAspect)
        : mainAspect + otherAspect;

    private static PhonesArrangement Beside(double width, double height, double a, double b, double gap, bool otherFirst)
    {
        var h = Math.Min(height, Math.Max(0, width - gap) / (a + b));
        var left = (width - (h * a + gap + h * b)) / 2;
        var top = (height - h) / 2;
        var first = otherFirst ? b : a;
        var firstRect = new RectD(left, top, h * first, h);
        var secondRect = new RectD(left + h * first + gap, top, h * (otherFirst ? a : b), h);
        return otherFirst
            ? new PhonesArrangement(secondRect, firstRect, false)
            : new PhonesArrangement(firstRect, secondRect, false);
    }

    private static PhonesArrangement Stacked(double width, double height, double a, double b, double gap, bool otherFirst)
    {
        var w = Math.Min(width, Math.Max(0, height - gap) / (1 / a + 1 / b));
        var top = (height - (w / a + gap + w / b)) / 2;
        var left = (width - w) / 2;
        var first = otherFirst ? b : a;
        var firstRect = new RectD(left, top, w, w / first);
        var secondRect = new RectD(left, top + w / first + gap, w, w / (otherFirst ? a : b));
        return otherFirst
            ? new PhonesArrangement(secondRect, firstRect, true)
            : new PhonesArrangement(firstRect, secondRect, true);
    }

    private static bool Fits(PhonesArrangement arrangement) =>
        arrangement.Other is { } other &&
        Math.Min(arrangement.Main.Width, arrangement.Main.Height) >= Narrowest &&
        Math.Min(other.Width, other.Height) >= Narrowest;

    private static double SmallerArea(PhonesArrangement arrangement) =>
        Math.Min(arrangement.Main.Width * arrangement.Main.Height, arrangement.Other!.Value.Width * arrangement.Other.Value.Height);
}
