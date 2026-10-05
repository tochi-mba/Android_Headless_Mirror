using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rex.Core;

namespace Rex.Tests.Site;

/// <summary>
/// Writes the middle of docs/settings.html: every setting, group by group in the order the Settings
/// tab shows them, then the ones without a control yet and the ones set somewhere else.
/// </summary>
internal static partial class SettingsPageWriter
{
    public const string Key = "settings";

    public static string Write(string repoRoot)
    {
        var groups = SettingsReference.Read(repoRoot);
        var html = new StringBuilder();
        html.Append("<div class=\"doc-layout\">\n");
        html.Append("  <nav class=\"doc-contents\" aria-label=\"Groups of settings\">\n");
        html.Append("    <p class=\"doc-contents-title\">Groups</p>\n    <ol>\n");
        foreach (var group in groups)
        {
            html.Append(CultureInfo.InvariantCulture, $"      <li><a href=\"#{GroupId(group.Name)}\">{SiteHtml.Escape(group.Title)}</a></li>\n");
        }

        html.Append("      <li><a href=\"#group-elsewhere\">Set somewhere else</a></li>\n");
        html.Append("    </ol>\n  </nav>\n");
        html.Append("  <div class=\"doc-body\">\n");
        html.Append("    <div class=\"filter\">\n");
        html.Append("      <label for=\"settings-filter\">Find a setting</label>\n");
        html.Append("      <input id=\"settings-filter\" class=\"filter-input\" type=\"search\" autocomplete=\"off\" spellcheck=\"false\" placeholder=\"Words, or a path such as Mirror.MaxFps\" data-filter=\".ref-row\" data-filter-groups=\".ref-group\" data-filter-count=\"settings-filter-count\" data-filter-noun=\"setting\">\n");
        html.Append("      <p class=\"filter-count\" id=\"settings-filter-count\" role=\"status\" aria-live=\"polite\"></p>\n");
        html.Append("    </div>\n");

        foreach (var group in groups)
        {
            Group(html, GroupId(group.Name), group.Title, group.Intro, group.Rows.Select(row => Row(row)));
        }

        Group(html, "group-elsewhere", "Set somewhere else",
            "These are in config.json, but the app sets them for you where it makes sense to.",
            SettingsCatalogue.Elsewhere.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Row(pair.Key, pair.Value)));

        html.Append("  </div>\n</div>");
        return html.ToString();
    }

    public static string GroupId(string groupName) =>
        "group-" + CamelBreak().Replace(groupName.Replace("Group", string.Empty, StringComparison.Ordinal), "$1-$2").ToLowerInvariant();

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex CamelBreak();

    private static void Group(StringBuilder html, string id, string title, string intro, IEnumerable<string> rows)
    {
        html.Append(CultureInfo.InvariantCulture, $"    <section class=\"ref-group\" id=\"{id}\" aria-labelledby=\"{id}-title\">\n");
        html.Append(CultureInfo.InvariantCulture, $"      <h2 id=\"{id}-title\">{SiteHtml.Escape(title)}</h2>\n");
        if (intro.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"      <p class=\"ref-intro\">{SiteHtml.Escape(intro)}</p>\n");
        }

        foreach (var row in rows)
        {
            html.Append(row);
        }

        html.Append("    </section>\n");
    }

    private static string Row(SettingRowInfo row)
    {
        var choices = row.Kind switch
        {
            "choice" => string.Join(" · ", row.Choices.Select(c => c.Label)),
            "switch" => "On or off",
            "slider" => Range(row),
            "list" => "A list, chosen in the window",
            "key" => $"A key with Ctrl, such as {SettingsReference.ExampleKey}, or F13 to F24 on their own; empty turns it off",
            _ => "Text",
        };

        var applies = SettingsCatalogue.AppliesAtNextStart(row.Path) ? "The next time the mirror starts" : "At once";
        return Article(row.Path, row.Label, row.Hint, SettingsReference.DefaultText(row), choices, applies, Example(row));
    }

    /// <summary>A setting with no control: its path stands for its name.</summary>
    private static string Row(string path, string? reason)
    {
        var value = SettingsReference.Default(path);
        var kind = value?.GetValueKind();
        var choices = kind switch
        {
            JsonValueKind.True or JsonValueKind.False => "true or false",
            JsonValueKind.Number => "A number",
            JsonValueKind.Array => "A list",
            _ => "Text",
        };

        var example = reason is null && kind is JsonValueKind.True or JsonValueKind.False
            ? $"rex config set {path} {(kind == JsonValueKind.True ? "false" : "true")}"
            : null;
        var applies = SettingsCatalogue.AppliesAtNextStart(path) ? "The next time the mirror starts" : "At once";
        return Article(path, path, reason ?? string.Empty, SettingsReference.Describe(null, value), choices, applies, example);
    }

    /// <summary>One setting, on one line, so the page stays readable as a file however many settings there are.</summary>
    private static string Article(string path, string label, string hint, string defaultText, string choices, string applies, string? example)
    {
        var html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"      <article class=\"ref-row\" id=\"{SiteHtml.Escape(path)}\"><h3>{SiteHtml.Escape(label)}</h3>");
        if (hint.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"ref-hint\">{SiteHtml.Escape(hint)}</p>");
        }

        html.Append("<dl class=\"ref-facts\">");
        Fact(html, "Default", SiteHtml.Escape(defaultText));
        Fact(html, "Choices", SiteHtml.Escape(choices));
        Fact(html, "Applies", applies);
        Fact(html, "In config.json", $"<code>{SiteHtml.Escape(path)}</code>");
        html.Append("</dl>");
        if (example is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"ref-command\"><code>{SiteHtml.Escape(example)}</code></p>");
        }

        html.Append("</article>\n");
        return html.ToString();
    }

    private static void Fact(StringBuilder html, string term, string value) =>
        html.Append(CultureInfo.InvariantCulture, $"<div><dt>{term}</dt><dd>{value}</dd></div>");

    private static string Range(SettingRowInfo row)
    {
        var from = (row.Minimum ?? 0).ToString(CultureInfo.InvariantCulture);
        var to = (row.Maximum ?? 0).ToString(CultureInfo.InvariantCulture);
        return row.Step is { } step
            ? $"From {from} to {to}, in steps of {step.ToString(CultureInfo.InvariantCulture)}"
            : $"From {from} to {to}";
    }

    /// <summary>The command that sets this to something other than its default, or null when a command is no help.</summary>
    private static string? Example(SettingRowInfo row)
    {
        var other = SettingsReference.OtherValues(row).FirstOrDefault();
        if (row.Kind == "slider")
        {
            other = row.Maximum?.ToString(CultureInfo.InvariantCulture);
        }

        return other is null ? null : $"rex config set {row.Path} {other}";
    }
}
