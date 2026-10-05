using System.Globalization;
using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>
/// How screenshots and recordings are named: a template with {date}, {time}, {phone}, {model} and
/// {n} (a number that counts up). A name that is already taken never overwrites: {n} counts on,
/// and a template without it gets " (2)", " (3)" and so on.
/// </summary>
public static partial class CaptureName
{
    public const string Default = "android-{date}-{time}";
    public const int MaxLength = 100;

    /// <summary>The tokens a template may hold, as the Settings tab lists them.</summary>
    public static readonly string[] Tokens = ["{date}", "{time}", "{phone}", "{model}", "{n}"];

    private static readonly string[] Reserved =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Token();

    /// <summary>Why a template cannot name files, or null when it can.</summary>
    public static string? WhyNot(string? template)
    {
        var text = template?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return "A name is needed, such as android-{date}-{time}.";
        }

        if (text.Length > MaxLength)
        {
            return $"A name can be at most {MaxLength} characters.";
        }

        if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "A file name cannot hold \\ / : * ? \" < > | or control characters.";
        }

        if (Token().Matches(text).Select(m => m.Value).FirstOrDefault(t => !Tokens.Contains(t, StringComparer.OrdinalIgnoreCase)) is { } unknown)
        {
            return $"{unknown} is not one of {string.Join(", ", Tokens)}.";
        }

        if (Token().Replace(text, string.Empty).IndexOfAny(['{', '}']) >= 0)
        {
            return "Braces only go around " + string.Join(", ", Tokens) + ".";
        }

        if (text.EndsWith('.'))
        {
            return "A file name cannot end with a dot.";
        }

        return Reserved.Contains(text, StringComparer.OrdinalIgnoreCase) ? "Windows keeps that name for itself." : null;
    }

    /// <summary>The name a template gives, without its extension.</summary>
    public static string Format(string template, DateTime at, string phone, string model, int n)
    {
        var name = template.Trim();
        foreach (var (token, value) in new[]
                 {
                     ("{date}", at.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
                     ("{time}", at.ToString("HHmmss", CultureInfo.InvariantCulture)),
                     ("{phone}", Clean(phone)),
                     ("{model}", Clean(model)),
                     ("{n}", n.ToString(CultureInfo.InvariantCulture)),
                 })
        {
            name = name.Replace(token, value, StringComparison.OrdinalIgnoreCase);
        }

        return name;
    }

    /// <summary>
    /// A file name, with its extension, that <paramref name="taken"/> says is free: {n} counts up
    /// from 1, and a template without it gets " (2)" and on when its name is taken.
    /// </summary>
    public static string Unique(string template, DateTime at, string phone, string model, string extension, Func<string, bool> taken)
    {
        var counts = template.Contains("{n}", StringComparison.OrdinalIgnoreCase);
        var name = Format(template, at, phone, model, 1);
        for (var attempt = 2; taken(name + extension); attempt++)
        {
            name = counts
                ? Format(template, at, phone, model, attempt)
                : Format(template, at, phone, model, 1) + $" ({attempt.ToString(CultureInfo.InvariantCulture)})";
        }

        return name + extension;
    }

    /// <summary>A phone's name as part of a file name: whatever a file name cannot hold becomes a dash.</summary>
    private static string Clean(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length > 0 ? cleaned : "phone";
    }
}
