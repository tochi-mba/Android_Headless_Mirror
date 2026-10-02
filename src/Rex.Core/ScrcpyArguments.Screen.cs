using System.Globalization;

namespace Rex.Core;

/// <summary>The second screen's session: a copy of the phone's session with a display of its own.</summary>
public static partial class ScrcpyArguments
{
    /// <summary>The second screen's first port; the next ones are tried when another program holds it.</summary>
    public const int ScreenPort = 27190;

    /// <summary>The last port the second screen tries.</summary>
    public const int LastScreenPort = 27199;

    /// <summary>
    /// The second screen's command line. It is a copy's in everything about the phone (it changes
    /// nothing, plays no sound, records nothing), with a display of its own on the phone, the app
    /// that opens there, and its own resolution limit. A display that follows the view says so with
    /// -x, and scrcpy then takes no window size (it refuses one): the size is the display's.
    /// Extra arguments that cannot apply to another display are left out.
    /// </summary>
    public static IReadOnlyList<string> BuildScreen(
        RexConfig config,
        string serial,
        bool isTcp,
        string windowTitle,
        (int X, int Y) window,
        ScreenSpec spec,
        string? keyboardMode = null,
        int port = ScreenPort)
    {
        var screen = config.SecondScreen;
        var args = Build(config, serial, isTcp, windowTitle, null, recordPath: null, keyboardMode, copyIndex: 0)
            .Where(a => !a.StartsWith("--port=", StringComparison.Ordinal) &&
                        !a.StartsWith("--max-size=", StringComparison.Ordinal) &&
                        !IsScreenUnsafeExtra(a))
            .ToList();

        args.Add("--port=" + port.ToString(CultureInfo.InvariantCulture));
        args.Add("--window-x=" + window.X.ToString(CultureInfo.InvariantCulture));
        args.Add("--window-y=" + window.Y.ToString(CultureInfo.InvariantCulture));
        if (!spec.Follows)
        {
            args.Add("--window-width=" + spec.Width.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-height=" + spec.Height.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("--new-display=" + spec.NewDisplay);
        if (spec.Follows)
        {
            args.Add("--flex-display");
        }

        if (screen.MaxSize > 0)
        {
            args.Add("--max-size=" + screen.MaxSize.ToString(CultureInfo.InvariantCulture));
        }

        if (screen.KeepAppsOnClose)
        {
            args.Add("--no-vd-destroy-content");
        }

        if (!screen.Decorations)
        {
            args.Add("--no-vd-system-decorations");
        }

        args.Add("--display-ime-policy=" + screen.Keyboard switch
        {
            "phone" => "fallback",
            "never" => "hide",
            _ => "local",
        });

        if (PackageName.IsValid(spec.App))
        {
            args.Add("--start-app=" + spec.StartApp);
        }

        return args;
    }

    /// <summary>The first of the second screen's ports nothing else is listening on, or null when every one is taken.</summary>
    public static int? FirstFreeScreenPort(IEnumerable<int> busy)
    {
        var taken = busy.ToHashSet();
        return Enumerable.Range(ScreenPort, LastScreenPort - ScreenPort + 1).Cast<int?>().FirstOrDefault(port => !taken.Contains(port!.Value));
    }

    /// <summary>
    /// Why scrcpy could not open the second screen, from what it printed: the phone too old, an app
    /// that will not go on another display, or the session not connecting.
    /// </summary>
    public static string ScreenFailure(IEnumerable<string> output)
    {
        var text = string.Join('\n', output);
        return text.Contains("not supported", StringComparison.OrdinalIgnoreCase) && text.Contains("display", StringComparison.OrdinalIgnoreCase)
            ? "This phone cannot make a display of its own for an app."
            : text.Contains("Could not create", StringComparison.OrdinalIgnoreCase) && text.Contains("display", StringComparison.OrdinalIgnoreCase)
                ? "The phone could not make the second screen."
                : text.Contains("Server connection failed", StringComparison.OrdinalIgnoreCase) || text.Contains("Could not listen", StringComparison.OrdinalIgnoreCase)
                    ? "The second screen could not connect to the phone."
                    : "The second screen did not open.";
    }

    /// <summary>
    /// Extra arguments a second display cannot take: a crop of the phone's screen, another display,
    /// another new display, or an orientation for the phone's own picture.
    /// </summary>
    public static bool IsScreenUnsafeExtra(string argument) =>
        argument.StartsWith("--crop", StringComparison.Ordinal) ||
        argument.StartsWith("--display-id", StringComparison.Ordinal) ||
        argument.StartsWith("--new-display", StringComparison.Ordinal) ||
        argument.StartsWith("--display-orientation", StringComparison.Ordinal) ||
        argument.StartsWith("--capture-orientation", StringComparison.Ordinal) ||
        argument is "-x" or "--flex-display";
}
