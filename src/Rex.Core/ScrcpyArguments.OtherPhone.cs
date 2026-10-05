using System.Globalization;

namespace Rex.Core;

/// <summary>The session of a second, different phone shown beside the main one.</summary>
public static partial class ScrcpyArguments
{
    /// <summary>The other phone's port: past the copies' and the second screen's ranges.</summary>
    public const int OtherPhonePort = 27200;

    /// <summary>
    /// The other phone's command line. Unlike a copy it is a full session of its own phone: it may
    /// turn that phone's screen off and keep it awake, and it cleans up after itself, because it
    /// is the only session on it. Its screen, resolution and bit rate follow its own settings
    /// where they are set, and the main phone's otherwise. It carries sound only when this PC
    /// plays the other phone's; what belongs to the main phone (the app it starts, recording,
    /// the frame counter the status bar reads) it leaves out.
    /// </summary>
    public static IReadOnlyList<string> BuildOtherPhone(
        RexConfig config,
        string serial,
        bool isTcp,
        string windowTitle,
        (int X, int Y, int Width, int Height)? window,
        string? keyboardMode = null)
    {
        var other = config.SecondPhone;
        var own = config.Copy();
        own.Session.TurnScreenOff = other.TurnsScreenOff(config.Session.TurnScreenOff);
        own.Session.StartApp = string.Empty;
        own.App.ShowFrameRate = false;
        own.Mirror.Audio = config.Mirror.Audio && other.OtherHasSound;
        if (other.MaxSize > 0)
        {
            own.Mirror.MaxSize = other.MaxSize;
        }

        if (other.BitRate.Length > 0)
        {
            own.Mirror.VideoBitRate = other.BitRate;
        }

        var args = Build(own, serial, isTcp, windowTitle, window, recordPath: null, keyboardMode).ToList();
        var port = args.FindIndex(a => a.StartsWith("--port=", StringComparison.Ordinal));
        args[port] = "--port=" + OtherPhonePort.ToString(CultureInfo.InvariantCulture);
        return args;
    }
}
