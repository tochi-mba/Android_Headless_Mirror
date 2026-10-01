using System.Globalization;

namespace Rex.Core;

/// <summary>
/// The tile an app is shown with: the first letter or digit of its name on one of a few tints of
/// the palette. An app's icon lives inside its package on the phone, so it is not fetched; the
/// tint comes from the package, so the same app always looks the same.
/// </summary>
public static class LetterTile
{
    /// <summary>How many tints there are (the theme's AppTile0 to AppTile5).</summary>
    public const int Tints = 6;

    /// <summary>The first letter or digit of the name, in upper case, or "?" when it has none.</summary>
    public static string Letter(string name)
    {
        var text = StringInfo.GetTextElementEnumerator(name ?? string.Empty);
        while (text.MoveNext())
        {
            var element = (string)text.Current;
            if (char.IsLetterOrDigit(element, 0))
            {
                return element.ToUpper(CultureInfo.InvariantCulture);
            }
        }

        return "?";
    }

    /// <summary>The tint, 0 to <see cref="Tints"/> - 1, from a hash of the package that never changes between runs.</summary>
    public static int Tint(string package)
    {
        // FNV-1a: string.GetHashCode is randomised per process, and the tile must not change colour.
        var hash = 2166136261u;
        foreach (var c in package ?? string.Empty)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return (int)(hash % Tints);
    }
}
