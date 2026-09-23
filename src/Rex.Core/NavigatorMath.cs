namespace Rex.Core;

public static class NavigatorMath
{
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
