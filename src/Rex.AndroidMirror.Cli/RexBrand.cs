using Spectre.Console;

namespace Rex.AndroidMirror.Cli;

public static class RexBrand
{
    public const string Signal = "#D7FF3F";
    public const string Text = "#F2F5EE";
    public const string Muted = "#858D83";
    public const string Danger = "#FF774D";
    public const string Line = "#29302A";

    public static void Header(string subtitle = "ANDROID HEADLESS MIRROR")
    {
        AnsiConsole.Clear();

        var title = new FigletText("REX")
            .LeftJustified()
            .Color(Color.Parse(Signal));

        AnsiConsole.Write(title);
        AnsiConsole.MarkupLine($"[{Signal}]TECHNOLOGIES[/]  [{Muted}]{Markup.Escape(subtitle)}[/]");
        AnsiConsole.Write(new Rule().RuleStyle(Line));
    }

    public static Panel Panel(string title, IRenderable body)
    {
        return new Panel(body)
        {
            Header = new PanelHeader($" {title} "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Parse(Line)),
            Padding = new Padding(1, 0, 1, 0),
        };
    }

    public static void Success(string message) =>
        AnsiConsole.MarkupLine($"[{Signal}]✓[/] {Markup.Escape(message)}");

    public static void Warn(string message) =>
        AnsiConsole.MarkupLine($"[{Danger}]![/] {Markup.Escape(message)}");

    public static void Error(string message) =>
        AnsiConsole.MarkupLine($"[{Danger}]ERROR[/] {Markup.Escape(message)}");

    public static string State(bool value, string yes, string no) =>
        value ? $"[{Signal}]{Markup.Escape(yes)}[/]" : $"[{Muted}]{Markup.Escape(no)}[/]";

    public static string DeviceLabel(RexDevice device)
    {
        var label = string.IsNullOrWhiteSpace(device.DisplayName)
            ? device.Serial
            : device.DisplayName;
        var transport = device.IsTcp ? "wireless" : "USB";
        return $"{label} · {device.Serial} · {transport} · {device.State}";
    }

    public static SelectionPrompt<string> Menu(string title, IEnumerable<string> choices)
    {
        return new SelectionPrompt<string>()
            .Title($"[{Text}]{Markup.Escape(title)}[/]")
            .PageSize(16)
            .HighlightStyle(new Style(Color.Parse(Signal), decoration: Decoration.Bold))
            .MoreChoicesText($"[{Muted}](move up/down to reveal more)[/]")
            .AddChoices(choices);
    }
}
