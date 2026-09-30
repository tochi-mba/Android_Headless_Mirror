namespace Rex.Core;

/// <summary>
/// How scrcpy shows the picture on this PC: turned a quarter at a time, and mirrored. It is the
/// PC view's own state, not the phone's, so each scrcpy session keeps its own. The app follows the
/// main session's here, composing each turn exactly as scrcpy 4.1 does (sc_orientation_apply), so a
/// copy of the phone that opens later can be started already showing the picture the same way.
///
/// The values are scrcpy's own: the low two bits are the clockwise quarter turns, and 4 is a
/// horizontal flip applied before the turn.
/// </summary>
public static class DisplayOrientation
{
    /// <summary>scrcpy's names for the eight orientations, as --display-orientation takes them.</summary>
    public static IReadOnlyList<string> Names { get; } = ["0", "90", "180", "270", "flip0", "flip90", "flip180", "flip270"];

    public const int Upright = 0;

    /// <summary>The orientation <paramref name="source"/> becomes once <paramref name="transform"/> is applied to it.</summary>
    public static int Apply(int source, int transform)
    {
        source &= 7;
        transform &= 7;
        var transformFlip = transform & 4;
        var transformTurns = transform & 3;
        var sourceFlip = source & 4;
        var sourceTurns = source & 3;

        // Every flip is kept ahead of every turn. Moving the new flip ahead of a quarter turn the
        // source already has reverses that quarter turn, which is the same as adding a half turn.
        if ((source & 1) != 0 && transformFlip != 0)
        {
            sourceTurns += 2;
        }

        return (sourceFlip ^ transformFlip) | ((transformTurns + sourceTurns) % 4);
    }

    /// <summary>What an action does to the PC view's orientation, or null when it leaves it alone.</summary>
    public static int? TransformFor(string actionId) => actionId switch
    {
        "rotate-left" => 3,
        "rotate-right" => 1,
        "flip-horizontal" => 4,
        "flip-vertical" => 6,
        _ => null,
    };

    /// <summary>The orientation a name stands for, or null for anything scrcpy would not take.</summary>
    public static int? Parse(string? name)
    {
        if (name is null)
        {
            return null;
        }

        for (var i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    public static string Name(int orientation) => Names[orientation & 7];

    /// <summary>
    /// The orientation a session starts in: upright, unless the extra scrcpy arguments say
    /// otherwise with --display-orientation or --orientation (the last one given wins, as in scrcpy).
    /// </summary>
    public static int Initial(IReadOnlyList<string> extraArgs)
    {
        var orientation = Upright;
        for (var i = 0; i < extraArgs.Count; i++)
        {
            var value = ValueOf(extraArgs, i, "--display-orientation") ?? ValueOf(extraArgs, i, "--orientation");
            if (Parse(value) is { } parsed)
            {
                orientation = parsed;
            }
        }

        return orientation;
    }

    private static string? ValueOf(IReadOnlyList<string> args, int index, string option)
    {
        var arg = args[index];
        if (arg.StartsWith(option + "=", StringComparison.Ordinal))
        {
            return arg[(option.Length + 1)..];
        }

        return arg == option && index + 1 < args.Count ? args[index + 1] : null;
    }
}
