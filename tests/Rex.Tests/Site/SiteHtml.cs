using System.Text;

namespace Rex.Tests.Site;

/// <summary>
/// The few things every generated page needs: escaping, and swapping the part of a hand-written page
/// that sits between its generated markers.
/// </summary>
internal static class SiteHtml
{
    public static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    public static string Open(string key) => $"<!-- generated:{key} -->";

    public static string Close(string key) => $"<!-- /generated:{key} -->";

    /// <summary>The page with everything between its two markers for <paramref name="key"/> replaced.</summary>
    public static string Replace(string page, string key, string middle)
    {
        var open = page.IndexOf(Open(key), StringComparison.Ordinal);
        var close = page.IndexOf(Close(key), StringComparison.Ordinal);
        if (open < 0 || close < open)
        {
            throw new InvalidOperationException($"The page has no {Open(key)} … {Close(key)} markers.");
        }

        var start = open + Open(key).Length;
        return page[..start] + "\n" + middle.TrimEnd('\n') + "\n" + page[close..];
    }

    /// <summary>An id made of lower-case letters, digits and dashes.</summary>
    public static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
