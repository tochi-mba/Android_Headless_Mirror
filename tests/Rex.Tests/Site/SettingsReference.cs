using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rex.Core;

namespace Rex.Tests.Site;

/// <summary>One choice a setting offers: the value config.json holds and the words the control shows.</summary>
internal sealed record SettingChoice(string Value, string Label);

/// <summary>One setting as the Settings tab shows it.</summary>
internal sealed record SettingRowInfo(
    string Path,
    string Control,
    string Kind,
    string Label,
    string Hint,
    IReadOnlyList<SettingChoice> Choices,
    double? Minimum,
    double? Maximum,
    double? Step);

/// <summary>One group of the Settings tab, in the order the tab shows its rows.</summary>
internal sealed record SettingGroupInfo(string Name, string Title, string Intro, IReadOnlyList<SettingRowInfo> Rows);

/// <summary>
/// Reads the Settings tab out of its own markup: the groups in order (following the groups that live
/// in files of their own), and for each control the catalogue names, its label, hint and choices.
/// The markup is the source of truth for the words; reading it as data keeps the site's settings
/// page exactly what the tab says without starting a window.
/// </summary>
internal static class SettingsReference
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Choices that stand for true and false, as the panel's code reads them.</summary>
    private static readonly Dictionary<string, (string True, string False)> TrueFalseChoices = new(StringComparer.Ordinal)
    {
        ["Zoom.ZoomAtPointer"] = ("pointer", "middle"),
    };

    /// <summary>Switches over a value that is not true or false: what on and off write.</summary>
    private static readonly Dictionary<string, (string On, string Off)> SwitchValues = new(StringComparer.Ordinal)
    {
        ["Input.Gamepad"] = ("uhid", "disabled"),
    };

    public static IReadOnlyList<SettingGroupInfo> Read(string repoRoot)
    {
        var views = Path.Combine(repoRoot, "src", "Rex.Mirror", "Views");
        var controls = SettingsCatalogue.Controls.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var groups = Groups(Path.Combine(views, "SettingsPanel.xaml"), views, controls).ToList();
        return groups;
    }

    private static IEnumerable<SettingGroupInfo> Groups(string file, string views, IReadOnlyDictionary<string, string> controls)
    {
        var root = XDocument.Load(file).Root!;
        var body = root.Elements().First();
        foreach (var element in body.Name.LocalName == "Expander" ? [body] : body.Elements())
        {
            if (element.Name.LocalName == "Expander")
            {
                yield return Group(element, controls);
            }
            else if (element.Name.NamespaceName.Contains("Rex.Mirror.Views.Settings", StringComparison.Ordinal))
            {
                foreach (var group in Groups(Path.Combine(views, "Settings", element.Name.LocalName + ".xaml"), views, controls))
                {
                    yield return group;
                }
            }
        }
    }

    private static SettingGroupInfo Group(XElement expander, IReadOnlyDictionary<string, string> controls)
    {
        var name = (string)expander.Attribute(X + "Name")!;
        var title = (string?)expander.Attribute("Header")
            ?? expander.Elements().First(e => e.Name.LocalName == "Expander.Header").Descendants().First(e => e.Name.LocalName == "TextBlock").Attribute("Text")!.Value;
        var content = expander.Elements().First(e => e.Name.LocalName != "Expander.Header");
        var first = content.Elements().FirstOrDefault();
        var intro = first is { Name.LocalName: "TextBlock" } && first.Attribute("Style")?.Value.Contains("MutedText", StringComparison.Ordinal) == true
            ? first.Attribute("Text")?.Value ?? string.Empty
            : string.Empty;

        var rows = expander.Descendants()
            .Where(e => e.Attribute(X + "Name") is { } n && controls.ContainsKey(n.Value))
            .Select(e => Row(e, controls[e.Attribute(X + "Name")!.Value]))
            .ToArray();
        return new SettingGroupInfo(name, title, intro, rows);
    }

    private static SettingRowInfo Row(XElement control, string path)
    {
        var name = control.Attribute(X + "Name")!.Value;
        var (label, hint) = Words(control);
        var kind = control.Name.LocalName switch
        {
            "ComboBox" => "choice",
            "CheckBox" => "switch",
            "Slider" => "slider",
            "TextBox" => "text",
            _ => Default(path) is JsonArray ? "list" : "text",
        };

        IReadOnlyList<SettingChoice> choices = control.Elements()
            .Where(e => e.Name.LocalName == "ComboBoxItem")
            .Select(e => new SettingChoice(e.Attribute("Tag")?.Value ?? string.Empty, e.Attribute("Content")?.Value ?? string.Empty))
            .ToArray();
        if (kind == "choice" && choices.Count == 0 && path.StartsWith("Input.", StringComparison.Ordinal))
        {
            // The mouse buttons share one list of choices, made in code from InputSettings.
            choices = InputSettings.ButtonChoices.Select(c => new SettingChoice(c.Action, c.Label)).ToArray();
        }

        if (TrueFalseChoices.TryGetValue(path, out var truth))
        {
            choices = choices.Select(c => c with { Value = c.Value == truth.True ? "true" : c.Value == truth.False ? "false" : c.Value }).ToArray();
        }

        double? Number(string attribute) =>
            control.Attribute(attribute) is { } value ? double.Parse(value.Value, CultureInfo.InvariantCulture) : null;
        var step = control.Attribute("IsSnapToTickEnabled")?.Value == "True" ? Number("TickFrequency") : null;
        return new SettingRowInfo(path, name, kind, label, hint, choices, Number("Minimum"), Number("Maximum"), step);
    }

    /// <summary>The words a person reads for a control: its row's label and the line under it.</summary>
    private static (string Label, string Hint) Words(XElement control)
    {
        var tooltip = control.Attribute("ToolTip")?.Value ?? string.Empty;
        var row = control.Ancestors().FirstOrDefault(e => e.Name.LocalName == "HeaderedContentControl");
        if (row is not null)
        {
            if (row.Attribute("Header")?.Value is { } header)
            {
                return (header, tooltip.Length > 0 ? tooltip : row.Attribute("ToolTip")?.Value ?? string.Empty);
            }

            var lines = row.Elements().First(e => e.Name.LocalName == "HeaderedContentControl.Header")
                .Descendants().Where(e => e.Name.LocalName == "TextBlock").Select(e => e.Attribute("Text")?.Value ?? string.Empty).ToArray();
            return (lines[0], lines.Length > 1 ? lines[1] : tooltip);
        }

        // A slider's label is the line above it; anything else falls back to the nearest plain heading.
        var before = control.ElementsBeforeSelf().Reverse().ToArray();
        if (control.Name.LocalName == "Slider" && before.FirstOrDefault() is { Name.LocalName: "Grid" } grid)
        {
            return (grid.Elements().First(e => e.Name.LocalName == "TextBlock").Attribute("Text")!.Value, tooltip);
        }

        var heading = before.FirstOrDefault(e => e.Name.LocalName == "TextBlock" && e.Attribute("Style") is null)?.Attribute("Text")?.Value;
        return (heading ?? control.Attribute("AutomationProperties.Name")?.Value ?? control.Attribute(X + "Name")!.Value, tooltip);
    }

    /// <summary>The shipped value of a setting, as JSON.</summary>
    public static JsonNode? Default(string path)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(new RexConfig(), RexJsonContext.Default.RexConfig);
        foreach (var segment in path.Split('.'))
        {
            node = node?[segment];
        }

        return node;
    }

    /// <summary>The shipped value in the words the control shows.</summary>
    public static string DefaultText(SettingRowInfo row) => Describe(row, Default(row.Path));

    /// <summary>The value a switch over a non-true/false setting writes when on, or null for an ordinary switch.</summary>
    public static (string On, string Off)? SwitchValue(string path) => SwitchValues.TryGetValue(path, out var values) ? values : null;

    public static string Describe(SettingRowInfo? row, JsonNode? value)
    {
        if (value is JsonArray list)
        {
            return list.Count == 0 ? "None" : string.Join(", ", list.Select(item => item?.ToString() ?? string.Empty));
        }

        if (value is null)
        {
            return "Not set";
        }

        var kind = value.GetValueKind();
        if (kind is JsonValueKind.True or JsonValueKind.False)
        {
            var truth = kind == JsonValueKind.True ? "true" : "false";
            return row?.Choices.FirstOrDefault(c => c.Value == truth)?.Label ?? (kind == JsonValueKind.True ? "On" : "Off");
        }

        if (row is not null && SwitchValue(row.Path) is { } values)
        {
            return value.GetValue<string>() == values.On ? "On" : "Off";
        }

        var text = kind == JsonValueKind.Number
            ? value.GetValue<double>().ToString(CultureInfo.InvariantCulture)
            : value.GetValue<string>();
        var choice = row?.Choices.FirstOrDefault(c => string.Equals(c.Value, text, StringComparison.OrdinalIgnoreCase));
        return choice?.Label ?? (text.Length == 0 ? "Empty" : text);
    }

    /// <summary>The values a test can set a setting to: every other choice, the other side of a switch, a slider's ends.</summary>
    public static IEnumerable<string> OtherValues(SettingRowInfo row)
    {
        var current = Default(row.Path);
        var text = current is null ? string.Empty : current.GetValueKind() switch
        {
            JsonValueKind.Number => current.GetValue<double>().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => current.GetValue<string>(),
            _ => string.Empty,
        };

        return row.Kind switch
        {
            "choice" => row.Choices.Select(c => c.Value).Where(v => !string.Equals(v, text, StringComparison.OrdinalIgnoreCase)),
            "switch" when text is "true" or "false" => [text == "true" ? "false" : "true"],
            "switch" => SwitchValue(row.Path) is { } values ? [text == values.On ? values.Off : values.On] : [],
            "slider" => new[] { row.Minimum, row.Maximum }.OfType<double>()
                .Select(v => v.ToString(CultureInfo.InvariantCulture)).Where(v => v != text),
            _ => [],
        };
    }

    /// <summary>
    /// Whether giving a setting this value changes what scrcpy is started with: the same comparison
    /// the app makes to offer a restart.
    /// </summary>
    public static bool ChangesLaunch(string path, string value, params (string Path, string Value)[] given)
    {
        var baseline = ScrcpyArguments.LaunchSettings(Configured(given), isTcp: false);
        var changed = ScrcpyArguments.LaunchSettings(Configured([.. given, (path, value)]), isTcp: false);
        return !changed.SequenceEqual(baseline);
    }

    /// <summary>The shipped settings with these values set, normalised as the app would load them.</summary>
    private static RexConfig Configured(IEnumerable<(string Path, string Value)> values)
    {
        var node = JsonSerializer.SerializeToNode(new RexConfig(), RexJsonContext.Default.RexConfig)!;
        foreach (var (path, value) in values)
        {
            var segments = path.Split('.');
            var parent = segments[..^1].Aggregate(node, (current, segment) => current[segment]!);
            parent[segments[^1]] = parent[segments[^1]]?.GetValueKind() switch
            {
                JsonValueKind.Number => JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)),
                JsonValueKind.True or JsonValueKind.False => JsonValue.Create(bool.Parse(value)),
                _ => JsonValue.Create(value),
            };
        }

        var config = node.Deserialize(RexJsonContext.Default.RexConfig)!;
        config.Normalize();
        return config;
    }
}
