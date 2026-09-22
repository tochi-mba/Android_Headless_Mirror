using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Rex.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

public readonly record struct PointD(double X, double Y);

public sealed record PatternBounds(double Left, double Top, double Right, double Bottom);

/// <summary>Where the nine pattern dots are, normalized to the Android screen (0..1).</summary>
public sealed record PatternGeometryInfo(string Source, double ScreenWidth, double ScreenHeight, bool ExactDots, PatternBounds Bounds);

public sealed record PatternLayout(string Source, RectD ContentRect, IReadOnlyList<PointD> Points);

/// <summary>
/// Pure geometry for the pattern-lock guide: where the video is inside the mirror, where
/// Android says the pattern widget is, and how to map one onto the other. No I/O.
/// </summary>
public static partial class PatternGeometry
{
    public const string SourceUiDots = "ui-dots";
    public const string SourceUiView = "ui-view";
    public const string SourceCalibration = "calibration";
    public const string SourceEstimated = "estimated";
    public const string SourceUnavailable = "unavailable";

    // Estimated fallback grid (fraction of the fitted content rect).
    public const double EstimatedCenterX = 0.5;
    public const double EstimatedCenterY = 0.62;
    public const double EstimatedSizeRelativeToWidth = 0.62;
    public const double DotRadiusRelativeToWidth = 0.026;
    public const double MinimumCalibrationSpan = 0.08;

    [GeneratedRegex(@"\b(mShowingLockscreen|mKeyguardShowing|isStatusBarKeyguard|keyguardShowing|deviceLocked|showing)\s*[=:]\s*(true|1)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LockedPattern();

    [GeneratedRegex(@"\b(mShowingLockscreen|mKeyguardShowing|isStatusBarKeyguard|keyguardShowing|deviceLocked|showing)\s*[=:]\s*(false|0)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnlockedPattern();

    [GeneratedRegex(@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$")]
    private static partial Regex BoundsPattern();

    public static KeyguardState ParseKeyguardState(string text)
    {
        if (LockedPattern().IsMatch(text))
        {
            return KeyguardState.Locked;
        }

        return UnlockedPattern().IsMatch(text) ? KeyguardState.Unlocked : KeyguardState.Unknown;
    }

    /// <summary>The rectangle the video occupies inside a client area (aspect-fitted, centered, orientation-aware).</summary>
    public static RectD FittedContentRect(double clientWidth, double clientHeight, double deviceWidth, double deviceHeight)
    {
        if (clientWidth <= 0 || clientHeight <= 0)
        {
            return new RectD(0, 0, 0, 0);
        }

        if (deviceWidth <= 0 || deviceHeight <= 0)
        {
            return new RectD(0, 0, clientWidth, clientHeight);
        }

        var clientRatio = clientWidth / clientHeight;
        var portraitRatio = deviceWidth / deviceHeight;
        var landscapeRatio = deviceHeight / deviceWidth;
        var portraitError = Math.Abs(Math.Log(clientRatio / portraitRatio));
        var landscapeError = Math.Abs(Math.Log(clientRatio / landscapeRatio));

        var (sourceWidth, sourceHeight) = landscapeError < portraitError
            ? (deviceHeight, deviceWidth)
            : (deviceWidth, deviceHeight);

        var scale = Math.Min(clientWidth / sourceWidth, clientHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new RectD((clientWidth - width) / 2, (clientHeight - height) / 2, width, height);
    }

    public static IReadOnlyList<PointD> EstimatedGridPoints(RectD content)
    {
        var span = Math.Min(content.Width * EstimatedSizeRelativeToWidth, content.Height * 0.62);
        var centerX = content.X + content.Width * EstimatedCenterX;
        var centerY = content.Y + content.Height * EstimatedCenterY;
        return GridPoints(centerX - span / 2, centerY - span / 2, centerX + span / 2, centerY + span / 2);
    }

    private static IReadOnlyList<PointD> GridPoints(double left, double top, double right, double bottom)
    {
        double[] xs = [left, (left + right) / 2, right];
        double[] ys = [top, (top + bottom) / 2, bottom];
        var points = new List<PointD>(9);
        foreach (var y in ys)
        {
            foreach (var x in xs)
            {
                points.Add(new PointD(x, y));
            }
        }

        return points;
    }

    public static RectD? ParseBounds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = BoundsPattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var left = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var top = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var right = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var bottom = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        return right <= left || bottom <= top ? null : new RectD(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Finds the pattern widget in a uiautomator dump. Exact virtual cell bounds (AOSP exposes nine
    /// while a pattern is in progress) win over the parent view; the parent view uses the AOSP
    /// 1/6 and 5/6 cell centers.
    /// </summary>
    public static PatternGeometryInfo? FromUiHierarchy(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var document = new XmlDocument();
        try
        {
            document.LoadXml(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        var nodes = document.SelectNodes("//node")?.Cast<XmlElement>().ToArray() ?? [];
        if (nodes.Length == 0)
        {
            return null;
        }

        double screenRight = 0, screenBottom = 0;
        var candidates = new List<(XmlElement Node, RectD Bounds, int Score)>();

        foreach (var node in nodes)
        {
            var bounds = ParseBounds(node.GetAttribute("bounds"));
            if (bounds is null)
            {
                continue;
            }

            screenRight = Math.Max(screenRight, bounds.Value.Right);
            screenBottom = Math.Max(screenBottom, bounds.Value.Bottom);

            var className = node.GetAttribute("class");
            var resourceId = node.GetAttribute("resource-id");
            var description = node.GetAttribute("content-desc");
            var text = node.GetAttribute("text");

            var score = 0;
            if (Regex.IsMatch(className, @"(?i)(^|\.)LockPatternView$")) score += 120;
            else if (Regex.IsMatch(className, "(?i)Pattern")) score += 35;
            if (Regex.IsMatch(resourceId, "(?i)(lock.?pattern|pattern.?lock|lockPatternView)")) score += 100;
            else if (Regex.IsMatch(resourceId, "(?i)pattern")) score += 45;
            if (Regex.IsMatch(description, @"(?i)pattern\s*(area|lock|grid)?")) score += 55;
            if (Regex.IsMatch(text, @"(?i)pattern\s*(area|lock|grid)?")) score += 20;

            var ratio = bounds.Value.Width / bounds.Value.Height;
            if (ratio is >= 0.72 and <= 1.38) score += 20;
            if (bounds.Value.Width >= 180 && bounds.Value.Height >= 180) score += 10;

            candidates.Add((node, bounds.Value, score));
        }

        if (screenRight <= 0 || screenBottom <= 0)
        {
            return null;
        }

        var best = candidates
            .Where(x => x.Score >= 50)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Bounds.Width * x.Bounds.Height)
            .FirstOrDefault();

        if (best.Node is null)
        {
            return null;
        }

        var dots = new List<RectD>();
        foreach (var child in best.Node.SelectNodes(".//node")?.Cast<XmlElement>() ?? [])
        {
            var bounds = ParseBounds(child.GetAttribute("bounds"));
            if (bounds is null)
            {
                continue;
            }

            var b = bounds.Value;
            if (b.X < best.Bounds.X || b.Y < best.Bounds.Y || b.Right > best.Bounds.Right || b.Bottom > best.Bounds.Bottom)
            {
                continue;
            }

            var childText = string.Join(' ', child.GetAttribute("class"), child.GetAttribute("resource-id"), child.GetAttribute("content-desc"), child.GetAttribute("text"));
            if (!Regex.IsMatch(childText, @"(?i)(pattern.*cell|cell.*pattern|pattern\s*cell)"))
            {
                continue;
            }

            if (b.Width > best.Bounds.Width * 0.45 || b.Height > best.Bounds.Height * 0.45)
            {
                continue;
            }

            dots.Add(b);
        }

        if (dots.Count >= 9)
        {
            var ordered = dots.OrderBy(x => x.CenterY).ThenBy(x => x.CenterX).Take(9).ToArray();
            var geometry = Normalize(SourceUiDots, screenRight, screenBottom,
                ordered.Min(x => x.CenterX), ordered.Min(x => x.CenterY),
                ordered.Max(x => x.CenterX), ordered.Max(x => x.CenterY), exactDots: true);
            if (geometry is not null)
            {
                return geometry;
            }
        }

        var view = best.Bounds;
        return Normalize(SourceUiView, screenRight, screenBottom,
            view.X + view.Width / 6, view.Y + view.Height / 6,
            view.X + view.Width * 5 / 6, view.Y + view.Height * 5 / 6, exactDots: false);
    }

    private static PatternGeometryInfo? Normalize(string source, double screenWidth, double screenHeight, double left, double top, double right, double bottom, bool exactDots)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || right <= left || bottom <= top)
        {
            return null;
        }

        return new PatternGeometryInfo(source, screenWidth, screenHeight, exactDots,
            new PatternBounds(left / screenWidth, top / screenHeight, right / screenWidth, bottom / screenHeight));
    }

    public static PatternGeometryInfo? FromCalibration(PatternCalibration? calibration) =>
        calibration is { IsValid: true }
            ? new PatternGeometryInfo(SourceCalibration, 0, 0, false, new PatternBounds(calibration.Left, calibration.Top, calibration.Right, calibration.Bottom))
            : null;

    /// <summary>Exact dots beat calibration, calibration beats the parent view, then nothing.</summary>
    public static PatternGeometryInfo? Effective(PatternGeometryInfo? discovered, PatternGeometryInfo? calibration)
    {
        if (discovered is { Source: SourceUiDots })
        {
            return discovered;
        }

        return calibration ?? discovered;
    }

    public static PatternLayout Layout(PatternGeometryInfo? geometry, double clientWidth, double clientHeight, double fallbackDeviceWidth, double fallbackDeviceHeight, bool allowEstimate)
    {
        if (geometry is null)
        {
            var content = FittedContentRect(clientWidth, clientHeight, fallbackDeviceWidth, fallbackDeviceHeight);
            return allowEstimate
                ? new PatternLayout(SourceEstimated, content, EstimatedGridPoints(content))
                : new PatternLayout(SourceUnavailable, content, []);
        }

        var screenWidth = geometry.ScreenWidth > 0 ? geometry.ScreenWidth : fallbackDeviceWidth;
        var screenHeight = geometry.ScreenHeight > 0 ? geometry.ScreenHeight : fallbackDeviceHeight;
        var rect = FittedContentRect(clientWidth, clientHeight, screenWidth, screenHeight);
        var b = geometry.Bounds;
        return new PatternLayout(
            geometry.Source,
            rect,
            GridPoints(rect.X + b.Left * rect.Width, rect.Y + b.Top * rect.Height, rect.X + b.Right * rect.Width, rect.Y + b.Bottom * rect.Height));
    }

    public static PatternBounds? BoundsFromPoints(IReadOnlyList<PointD> points, RectD content)
    {
        if (points.Count != 9 || content.Width <= 0 || content.Height <= 0)
        {
            return null;
        }

        return new PatternBounds(
            Math.Clamp((points.Min(p => p.X) - content.X) / content.Width, 0, 1),
            Math.Clamp((points.Min(p => p.Y) - content.Y) / content.Height, 0, 1),
            Math.Clamp((points.Max(p => p.X) - content.X) / content.Width, 0, 1),
            Math.Clamp((points.Max(p => p.Y) - content.Y) / content.Height, 0, 1));
    }

    /// <summary>Keeps calibration bounds inside 0..1 with a minimum span, sliding rather than collapsing.</summary>
    public static PatternBounds ClampCalibration(PatternBounds bounds)
    {
        var left = Math.Clamp(bounds.Left, 0, 1);
        var top = Math.Clamp(bounds.Top, 0, 1);
        var right = Math.Clamp(bounds.Right, 0, 1);
        var bottom = Math.Clamp(bounds.Bottom, 0, 1);

        if (right - left < MinimumCalibrationSpan)
        {
            var center = (left + right) / 2;
            left = center - MinimumCalibrationSpan / 2;
            right = center + MinimumCalibrationSpan / 2;
        }

        if (bottom - top < MinimumCalibrationSpan)
        {
            var center = (top + bottom) / 2;
            top = center - MinimumCalibrationSpan / 2;
            bottom = center + MinimumCalibrationSpan / 2;
        }

        if (left < 0) { right -= left; left = 0; }
        if (right > 1) { left -= right - 1; right = 1; }
        if (top < 0) { bottom -= top; top = 0; }
        if (bottom > 1) { top -= bottom - 1; bottom = 1; }

        return new PatternBounds(Math.Max(0, left), Math.Max(0, top), Math.Min(1, right), Math.Min(1, bottom));
    }

    public static PatternBounds Adjust(PatternBounds b, string action, double stepX, double stepY)
    {
        var next = action switch
        {
            "move-left" => b with { Left = b.Left - stepX, Right = b.Right - stepX },
            "move-right" => b with { Left = b.Left + stepX, Right = b.Right + stepX },
            "move-up" => b with { Top = b.Top - stepY, Bottom = b.Bottom - stepY },
            "move-down" => b with { Top = b.Top + stepY, Bottom = b.Bottom + stepY },
            "shrink-width" => b with { Left = b.Left + stepX / 2, Right = b.Right - stepX / 2 },
            "grow-width" => b with { Left = b.Left - stepX / 2, Right = b.Right + stepX / 2 },
            "shrink-height" => b with { Top = b.Top + stepY / 2, Bottom = b.Bottom - stepY / 2 },
            "grow-height" => b with { Top = b.Top - stepY / 2, Bottom = b.Bottom + stepY / 2 },
            _ => b,
        };
        return ClampCalibration(next);
    }
}
