using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rex.AndroidMirror.Cli;

public static class RexBrand
{
    public const string Signal = "#D7FF3F";
    public const string Text = "#F2F5EE";
    public const string Muted = "#858D83";
    public const string Danger = "#FF774D";
    public const string Line = "#29302A";

    public static readonly Color SignalColor = new(0xD7, 0xFF, 0x3F);
    public static readonly Color TextColor = new(0xF2, 0xF5, 0xEE);
    public static readonly Color MutedColor = new(0x85, 0x8D, 0x83);
    public static readonly Color DangerColor = new(0xFF, 0x77, 0x4D);
    public static readonly Color LineColor = new(0x29, 0x30, 0x2A);

    public static void Header(string subtitle = "ANDROID HEADLESS MIRROR") =>
        Header(AnsiConsole.Console, subtitle);

    public static void Header(IAnsiConsole console, string subtitle = "ANDROID HEADLESS MIRROR")
    {
        console.Clear();

        var title = new FigletText("REX")
            .LeftJustified()
            .Color(SignalColor);

        console.Write(title);
        console.MarkupLine($"[{Signal}]TECHNOLOGIES[/]  [{Muted}]{Markup.Escape(subtitle)}[/]");
        console.Write(new Rule().RuleStyle(Line));
    }

    public static Panel Panel(string title, IRenderable body)
    {
        return new Panel(body)
        {
            Header = new PanelHeader($" {title} "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(LineColor),
            Padding = new Padding(1, 0, 1, 0),
        };
    }

    public static void Success(string message) => Success(AnsiConsole.Console, message);
    public static void Warn(string message) => Warn(AnsiConsole.Console, message);
    public static void Error(string message) => Error(AnsiConsole.Console, message);

    public static void Success(IAnsiConsole console, string message) =>
        console.MarkupLine($"[{Signal}]✓[/] {Markup.Escape(message)}");

    public static void Warn(IAnsiConsole console, string message) =>
        console.MarkupLine($"[{Danger}]![/] {Markup.Escape(message)}");

    public static void Error(IAnsiConsole console, string message) =>
        console.MarkupLine($"[{Danger}]ERROR[/] {Markup.Escape(message)}");

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
            .HighlightStyle(new Style(SignalColor, decoration: Decoration.Bold))
            .MoreChoicesText($"[{Muted}](move up/down to reveal more)[/]")
            .AddChoices(choices);
    }
}
