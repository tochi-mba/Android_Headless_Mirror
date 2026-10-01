using System.Text;
using System.Text.RegularExpressions;

namespace Rex.Tests.Site;

/// <summary>
/// The Markdown CHANGELOG.md is written in, and no more: headings, paragraphs, bullets nested by two
/// spaces, inline code, bold and links. Anything else is shown as the text it is, escaped.
/// </summary>
internal static partial class MarkdownSubset
{
    /// <summary>Renders the lines; <paramref name="headingId"/> gives a heading its id, or null for none.</summary>
    public static string Render(IEnumerable<string> lines, Func<int, string, string?> headingId)
    {
        var html = new StringBuilder();
        var paragraph = new List<string>();
        var depth = 0;

        void Flush()
        {
            if (paragraph.Count > 0)
            {
                html.Append("<p>").Append(Inline(string.Join(' ', paragraph))).Append("</p>\n");
                paragraph.Clear();
            }
        }

        void CloseLists(int to)
        {
            for (; depth > to; depth--)
            {
                html.Append("</li>\n</ul>\n");
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            var heading = Heading().Match(line);
            if (heading.Success)
            {
                Flush();
                CloseLists(0);
                var level = heading.Groups[1].Length;
                var text = heading.Groups[2].Value;
                var id = headingId(level, text);
                html.Append(id is null ? $"<h{level}>" : $"<h{level} id=\"{SiteHtml.Escape(id)}\">")
                    .Append(Inline(text)).Append($"</h{level}>\n");
                continue;
            }

            var bullet = Bullet().Match(line);
            if (bullet.Success)
            {
                Flush();
                var level = bullet.Groups[1].Length / 2 + 1;
                if (level > depth)
                {
                    for (; depth < level; depth++)
                    {
                        html.Append(depth == 0 ? "<ul>\n<li>" : "\n<ul>\n<li>");
                    }
                }
                else
                {
                    CloseLists(level);
                    html.Append("</li>\n<li>");
                }

                html.Append(Inline(bullet.Groups[2].Value));
                continue;
            }

            if (depth > 0 && raw.StartsWith("  ", StringComparison.Ordinal))
            {
                // A bullet's text carried on to the next line.
                html.Append(' ').Append(Inline(line.Trim()));
                continue;
            }

            CloseLists(0);
            paragraph.Add(line.Trim());
        }

        Flush();
        CloseLists(0);
        return html.ToString();
    }

    /// <summary>Escapes the text, then turns `code`, **bold** and [links](url) into their tags.</summary>
    public static string Inline(string text)
    {
        var parts = Code().Split(text);
        var html = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 1)
            {
                html.Append("<code>").Append(SiteHtml.Escape(parts[i])).Append("</code>");
                continue;
            }

            var escaped = SiteHtml.Escape(parts[i]);
            escaped = Bold().Replace(escaped, "<strong>$1</strong>");
            escaped = Link().Replace(escaped, match =>
            {
                var url = match.Groups[2].Value;
                var external = url.StartsWith("https://", StringComparison.Ordinal);
                return external
                    ? $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{match.Groups[1].Value}</a>"
                    : $"<a href=\"{url}\">{match.Groups[1].Value}</a>";
            });
            html.Append(escaped);
        }

        return html.ToString();
    }

    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^( *)[-*]\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();
}
