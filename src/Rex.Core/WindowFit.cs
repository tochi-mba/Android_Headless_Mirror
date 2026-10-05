namespace Rex.Core;

/// <summary>
/// The window size at which the mirror area takes exactly the shape of what it shows: the phone's
/// views side by side at the area's height. The window keeps its height and changes its width;
/// when that would not fit the screen, it is as wide as the screen allows and its height follows.
/// When it cannot be as narrow as that, it keeps its smallest width and grows taller instead.
/// </summary>
public static class WindowFit
{
    /// <param name="window">The window's size now.</param>
    /// <param name="area">The mirror area's size now; the rest of the window (bars, side panel) stays as it is.</param>
    /// <param name="aspects">Each view's width over its height, left to right.</param>
    /// <param name="gap">The space between two views.</param>
    /// <param name="largest">The most room the screen has for the window.</param>
    /// <param name="smallest">The window's own smallest size.</param>
    public static (double Width, double Height) Size(
        (double Width, double Height) window,
        (double Width, double Height) area,
        IReadOnlyList<double> aspects,
        double gap,
        (double Width, double Height) largest,
        (double Width, double Height) smallest)
    {
        var shape = aspects.Where(a => double.IsFinite(a) && a > 0).Sum();
        if (shape <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return window;
        }

        var aside = window.Width - area.Width + Math.Max(0, gap) * (aspects.Count - 1);
        var above = window.Height - area.Height;
        var width = aside + area.Height * shape;
        var height = window.Height;
        if (width > largest.Width)
        {
            width = largest.Width;
            height = above + (width - aside) / shape;
        }

        if (height > largest.Height)
        {
            height = largest.Height;
            width = aside + (height - above) * shape;
        }

        if (width < smallest.Width)
        {
            width = smallest.Width;
            height = Math.Min(largest.Height, above + (width - aside) / shape);
        }

        return (Math.Round(width), Math.Round(Math.Max(smallest.Height, height)));
    }
}
