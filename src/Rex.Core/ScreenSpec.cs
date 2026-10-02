using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>A second screen size as the command line names it: follow, phone, 720p, 1080p, 1440p or WIDTHxHEIGHT.</summary>
public sealed partial record ScreenSize(string Size, int Width, int Height)
{
    public const string Usage = "A size is follow, phone, 720p, 1080p, 1440p, or a width and height such as 1600x900.";

    [GeneratedRegex(@"^(?<w>\d{3,4})x(?<h>\d{3,4})$")]
    private static partial Regex Custom();

    /// <summary>The size, or null when the words are not one.</summary>
    public static ScreenSize? Parse(string? text)
    {
        var words = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (words is "follow" or "phone" or "720p" or "1080p" or "1440p")
        {
            return new ScreenSize(words, 0, 0);
        }

        return Custom().Match(words) is { Success: true } match
            ? new ScreenSize("custom", int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture))
            : null;
    }
}

/// <summary>
/// The display the second screen asks the phone for: its size in pixels, its density, whether it
/// follows the view's size, and the app that opens on it.
/// </summary>
/// <param name="Follows">The display is resized to match the view (scrcpy's flex display), so it never letterboxes.</param>
/// <param name="Dpi">0 leaves the density to the phone.</param>
public sealed partial record ScreenSpec(int Width, int Height, int Dpi, bool Follows, string App, bool Fresh)
{
    /// <summary>scrcpy's --new-display value: "1920x1080", "1920x1080/240".</summary>
    public string NewDisplay => Dpi > 0
        ? $"{Width.ToString(CultureInfo.InvariantCulture)}x{Height.ToString(CultureInfo.InvariantCulture)}/{Dpi.ToString(CultureInfo.InvariantCulture)}"
        : $"{Width.ToString(CultureInfo.InvariantCulture)}x{Height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>scrcpy's --start-app value: a "+" in front closes the app first.</summary>
    public string StartApp => (Fresh ? "+" : string.Empty) + App;

    /// <summary>Its size in words, for the status line: "1920 x 1080", or "fits the window" when it follows.</summary>
    public string Describe() => Follows
        ? "fits the window"
        : $"{Width.ToString(CultureInfo.InvariantCulture)} x {Height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The display the settings ask for. Fixed sizes lie on their side unless upright is asked for;
    /// the phone's own size likewise. Following the view takes its pixels as they are. Every side is
    /// even and at least <see cref="SecondScreenSettings.SmallestSide"/>, which encoders need.
    /// </summary>
    public static ScreenSpec For(SecondScreenSettings settings, (int Width, int Height) view, (int Width, int Height) phone, string app, bool fresh)
    {
        (int, int) Lying(int a, int b) => settings.Portrait ? (Math.Min(a, b), Math.Max(a, b)) : (Math.Max(a, b), Math.Min(a, b));
        var (width, height) = settings.Size switch
        {
            "phone" when phone.Width > 0 && phone.Height > 0 => Lying(phone.Width, phone.Height),
            "720p" => Lying(1280, 720),
            "1080p" or "phone" => Lying(1920, 1080),
            "1440p" => Lying(2560, 1440),
            "custom" => (settings.CustomWidth, settings.CustomHeight),
            _ => view,
        };

        return new ScreenSpec(Even(width), Even(height), settings.Dpi, settings.Size == "follow", app, fresh);
    }

    private static int Even(int side) => Math.Clamp(side, SecondScreenSettings.SmallestSide, SecondScreenSettings.LargestSide) & ~1;

    [GeneratedRegex(@"New display:\s*(?<w>\d{2,5})x(?<h>\d{2,5})(?:/(?<dpi>\d{2,4}))?\s*\(id=(?<id>\d{1,9})\)")]
    private static partial Regex NewDisplayLine();

    /// <summary>What scrcpy says when the phone made the display: "[server] INFO: New display: 1280x720/240 (id=7)".</summary>
    public static (int Width, int Height, int Dpi, int Id)? ParseNewDisplay(string? line)
    {
        if (line is null || NewDisplayLine().Match(line) is not { Success: true } match)
        {
            return null;
        }

        static int Number(Group group) => group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 0;
        return (Number(match.Groups["w"]), Number(match.Groups["h"]), Number(match.Groups["dpi"]), Number(match.Groups["id"]));
    }
}
