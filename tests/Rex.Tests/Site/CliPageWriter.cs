using System.Globalization;
using System.Text;
using Rex.Cli;

namespace Rex.Tests.Site;

/// <summary>Writes the middle of docs/cli.html from <see cref="CliReference.All"/>.</summary>
internal static class CliPageWriter
{
    public const string Key = "cli";

    public static string Write()
    {
        var html = new StringBuilder();
        html.Append("<section class=\"ref-group\" id=\"commands\" aria-labelledby=\"commands-title\">\n");
        html.Append("  <h2 id=\"commands-title\">Every command</h2>\n");
        html.Append("  <p class=\"ref-intro\">Commands marked <em>needs the app</em> act on the open window; start it with <code>rex open</code> first. The rest work on their own, over ADB.</p>\n");
        foreach (var command in CliReference.All)
        {
            var id = "cmd-" + SiteHtml.Slug(command.Usage.Replace("rex agent ", "rex ", StringComparison.Ordinal)[4..]);
            html.Append(CultureInfo.InvariantCulture, $"  <article class=\"ref-row cli-row\" id=\"{id}\">\n");
            html.Append(CultureInfo.InvariantCulture, $"    <h3><code>{SiteHtml.Escape(command.Usage)}</code></h3>\n");
            html.Append(CultureInfo.InvariantCulture, $"    <p class=\"ref-hint\">{SiteHtml.Escape(command.Summary)}</p>\n");
            var modes = (command.Human, command.Machine) switch
            {
                (true, true) => "Both: plain text, or JSON with <code>--json</code>",
                (true, false) => "Plain text only",
                _ => "JSON only",
            };
            html.Append("    <dl class=\"ref-facts\">\n");
            html.Append(CultureInfo.InvariantCulture, $"      <div><dt>Output</dt><dd>{modes}</dd></div>\n");
            html.Append(CultureInfo.InvariantCulture, $"      <div><dt>Needs the app</dt><dd>{(command.NeedsApp ? "Yes" : "No")}</dd></div>\n");
            html.Append("    </dl>\n");
            html.Append(CultureInfo.InvariantCulture,
                $"    <div class=\"command-row\"><code>{SiteHtml.Escape(command.Example)}</code><button class=\"copy-button\" type=\"button\" data-copy=\"{SiteHtml.Escape(command.Example)}\">Copy</button></div>\n");
            html.Append("  </article>\n");
        }

        html.Append("</section>");
        return html.ToString();
    }
}
