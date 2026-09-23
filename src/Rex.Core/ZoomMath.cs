namespace Rex.Core;

/// <summary>
/// The zoomed mirror is the scrcpy surface itself scaled inside a clipping viewport. A view is
/// fully described by the fitted base size, the zoom factor and the surface offset.
/// </summary>
public sealed record ZoomView(double Zoom, double OffsetX, double OffsetY, double SurfaceWidth, double SurfaceHeight)
{
    public static readonly ZoomView Identity = new(1, 0, 0, 0, 0);
    public bool IsZoomed => Zoom > 1.001;
}

public static class ZoomMath
{
    public const double MinZoom = 1.0;
    public const double SnapEpsilon = 0.02;

    public static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    /// <summary>Exponential sensitivity keeps zoom-in and zoom-out symmetric: f(1/x) == 1/f(x).</summary>
    public static double SensitivityAdjustedScale(double rawScale, double sensitivity)
    {
        if (rawScale <= 0 || sensitivity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawScale), "Scale and sensitivity must be positive.");
        }

        return Math.Exp(Math.Log(rawScale) * sensitivity);
    }

    public static double ClampZoom(double zoom, double maxZoom)
    {
        var clamped = Clamp(zoom, MinZoom, Math.Max(MinZoom, maxZoom));
        return Math.Abs(clamped - 1) < SnapEpsilon ? 1 : clamped;
    }

    /// <summary>Base fit of the video in the viewport (zoom 1), centered and aspect-preserving.</summary>
    public static RectD FitRect(double viewportWidth, double viewportHeight, double videoWidth, double videoHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return new RectD(0, 0, 0, 0);
        }

        if (videoWidth <= 0 || videoHeight <= 0)
        {
            return new RectD(0, 0, viewportWidth, viewportHeight);
        }

        var scale = Math.Min(viewportWidth / videoWidth, viewportHeight / videoHeight);
        var width = Math.Max(1, Math.Round(videoWidth * scale));
        var height = Math.Max(1, Math.Round(videoHeight * scale));
        return new RectD(Math.Floor((viewportWidth - width) / 2), Math.Floor((viewportHeight - height) / 2), width, height);
    }

    /// <summary>
    /// Produces a valid view for the requested zoom, keeping the surface point that was under
    /// (anchorX, anchorY) in viewport coordinates fixed when possible, then clamping so the
    /// zoomed surface always covers the viewport (or is centered when smaller).
    /// </summary>
    public static ZoomView Compute(double viewportWidth, double viewportHeight, RectD fit, double zoom, double anchorX, double anchorY, ZoomView? previous)
    {
        var surfaceWidth = Math.Max(1, Math.Round(fit.Width * zoom));
        var surfaceHeight = Math.Max(1, Math.Round(fit.Height * zoom));

        double offsetX, offsetY;
        if (previous is { SurfaceWidth: > 0, SurfaceHeight: > 0 } p)
        {
            var u = (anchorX - p.OffsetX) / p.SurfaceWidth;
            var v = (anchorY - p.OffsetY) / p.SurfaceHeight;
            offsetX = anchorX - u * surfaceWidth;
            offsetY = anchorY - v * surfaceHeight;
        }
        else
        {
            offsetX = fit.X - (surfaceWidth - fit.Width) * ((anchorX - fit.X) / Math.Max(1, fit.Width));
            offsetY = fit.Y - (surfaceHeight - fit.Height) * ((anchorY - fit.Y) / Math.Max(1, fit.Height));
        }

        return ClampView(viewportWidth, viewportHeight, new ZoomView(zoom, offsetX, offsetY, surfaceWidth, surfaceHeight));
    }

    /// <summary>
    /// A view centred on a freshly fitted picture. Used when the video's shape changes: carrying
    /// the old view forward would keep a landscape picture in the portrait rectangle it replaced.
    /// </summary>
    public static ZoomView Refit(double viewportWidth, double viewportHeight, double aspect, double zoom)
    {
        var fit = FitRect(viewportWidth, viewportHeight, aspect, 1.0);
        return Compute(viewportWidth, viewportHeight, fit, zoom, viewportWidth / 2, viewportHeight / 2, null);
    }

    public static ZoomView Pan(double viewportWidth, double viewportHeight, ZoomView view, double deltaX, double deltaY) =>
        ClampView(viewportWidth, viewportHeight, view with { OffsetX = view.OffsetX + deltaX, OffsetY = view.OffsetY + deltaY });

    public static ZoomView ClampView(double viewportWidth, double viewportHeight, ZoomView view)
    {
        var offsetX = view.SurfaceWidth <= viewportWidth
            ? Math.Floor((viewportWidth - view.SurfaceWidth) / 2)
            : Clamp(view.OffsetX, viewportWidth - view.SurfaceWidth, 0);
        var offsetY = view.SurfaceHeight <= viewportHeight
            ? Math.Floor((viewportHeight - view.SurfaceHeight) / 2)
            : Clamp(view.OffsetY, viewportHeight - view.SurfaceHeight, 0);
        return view with { OffsetX = Math.Round(offsetX), OffsetY = Math.Round(offsetY) };
    }

    /// <summary>The visible part of the surface, normalized 0..1, for the navigator rectangle.</summary>
    public static RectD VisibleFraction(double viewportWidth, double viewportHeight, ZoomView view)
    {
        if (view.SurfaceWidth <= 0 || view.SurfaceHeight <= 0)
        {
            return new RectD(0, 0, 1, 1);
        }

        var left = Clamp(-view.OffsetX / view.SurfaceWidth, 0, 1);
        var top = Clamp(-view.OffsetY / view.SurfaceHeight, 0, 1);
        var width = Clamp(viewportWidth / view.SurfaceWidth, 0, 1 - left);
        var height = Clamp(viewportHeight / view.SurfaceHeight, 0, 1 - top);
        return new RectD(left, top, width, height);
    }

    /// <summary>Centers the viewport on a normalized surface point (navigator click).</summary>
    public static ZoomView CenterOn(double viewportWidth, double viewportHeight, ZoomView view, double fractionX, double fractionY)
    {
        var offsetX = viewportWidth / 2 - Clamp(fractionX, 0, 1) * view.SurfaceWidth;
        var offsetY = viewportHeight / 2 - Clamp(fractionY, 0, 1) * view.SurfaceHeight;
        return ClampView(viewportWidth, viewportHeight, view with { OffsetX = offsetX, OffsetY = offsetY });
    }
}
