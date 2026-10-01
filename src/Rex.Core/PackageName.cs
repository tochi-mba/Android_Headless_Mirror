using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>
/// Checks an Android package or component name before it reaches the phone. Anything sent to
/// <c>am</c> or <c>pm</c> is passed as one argument and must also be one of these, so a name typed
/// on the command line or read from a file can never become a second shell command.
/// </summary>
public static partial class PackageName
{
    public const int Longest = 255;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$")]
    private static partial Regex Package();

    [GeneratedRegex(@"^\.?[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)*$")]
    private static partial Regex ClassName();

    /// <summary>A package name: two or more dotted parts, the first starting with a letter.</summary>
    public static bool IsValid(string? name) => name is { Length: > 0 and <= Longest } && Package().IsMatch(name);

    /// <summary>A component, <c>package/class</c>, with the class written in full or starting with a dot.</summary>
    public static bool IsValidComponent(string? component)
    {
        if (component is not { Length: > 0 and <= Longest * 2 })
        {
            return false;
        }

        var slash = component.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && IsValid(component[..slash]) && ClassName().IsMatch(component[(slash + 1)..]);
    }
}
