using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>
/// Builds the scrcpy command line for a session. The app embeds scrcpy's window, so
/// a few options are fixed and cannot be overridden by <see cref="MirrorSettings.ExtraArgs"/>.
/// </summary>
public static partial class ScrcpyArguments
{
    /// <summary>
    /// What scrcpy paints around a picture that does not fill its window: the app's own ink. scrcpy's
    /// default is a mid grey (#222), which showed as grey bars whenever the picture was letterboxed,
    /// even for the moment it takes the window to follow the phone turning.
    /// </summary>
    public const string LetterboxColour = "#080A09";

    /// <summary>scrcpy's letterbox with a black backdrop.</summary>
    public const string BlackLetterbox = "#000000";

    /// <summary>What scrcpy paints around the picture for the backdrop chosen, so the bars match the mirror area.</summary>
    public static string LetterboxFor(string backdrop) => backdrop == "black" ? BlackLetterbox : LetterboxColour;

    /// <summary>
    /// The local port the main session's tunnel listens on. Every session gets a port of its own:
    /// on Windows scrcpy binds with SO_REUSEADDR, so two sessions starting together on the default
    /// port can each accept the other's connection.
    /// </summary>
    public const int MainPort = 27183;

    /// <summary>The port for copy number <paramref name="index"/> (0-based), after the main session's.</summary>
    public static int CopyPort(int index) => MainPort + 1 + Math.Max(0, index);

    /// <summary>scrcpy's own bindings for an SDK mouse: right Back, middle Home, back button Recents, forward Notifications; with Shift, all clicks.</summary>
    public const string DefaultMouseBind = "bhsn:++++";

    public const string FullKeyboardMode = "uhid";
    public const string CompatibilityKeyboardMode = "sdk";

    /// <summary>
    /// The keyboard a session starts with: the hardware one unless the settings or this phone's
    /// history say otherwise. The history wins because a UHID refusal costs a failed launch every
    /// time, and it never changes for a given phone.
    /// </summary>
    public static string KeyboardModeFor(RexConfig config, DeviceProfile? profile) =>
        config.Mirror.CompatibilityKeyboard || profile?.CompatibilityKeyboard == true
            ? CompatibilityKeyboardMode
            : FullKeyboardMode;

    /// <summary>
    /// A new recording's file name, named as screenshots are (<see cref="CaptureName"/>); scrcpy
    /// writes the container its extension names. A name already taken is never overwritten.
    /// </summary>
    public static string RecordingFileName(string format, DateTime startedAt, string template = CaptureName.Default,
        string phone = "", string model = "", Func<string, bool>? taken = null) =>
        CaptureName.Unique(template, startedAt, phone, model, "." + MirrorSettings.OneOf(MirrorSettings.RecordFormats, format, "mp4"), taken ?? (_ => false));

    /// <summary>
    /// The launch settings a running session is compared against to offer a restart. The frame
    /// rate counter is left out: the app switches it on and off in the running session itself.
    /// A recording stands for itself by its folder and its format, not by the moment it started
    /// or the name it is given.
    /// </summary>
    public static IReadOnlyList<string> LaunchSettings(RexConfig config, bool isTcp) =>
        Build(config, "", isTcp, "", null,
            config.Mirror.RecordOnStart
                ? Path.Combine(config.Mirror.RecordDirectory, RecordingFileName(config.Mirror.RecordFormat, DateTime.MinValue))
                : null)
            .Where(argument => argument != PrintFps && !argument.StartsWith(PushTarget, StringComparison.Ordinal))
            .ToArray();

    /// <summary>scrcpy's frame rate counter, which prints the rate to the console every second.</summary>
    public const string PrintFps = "--print-fps";
    public const string PushTarget = "--push-target=";
    public const string NoFileDrop = "--no-file-drop";

    [GeneratedRegex(@"^\s*INFO:\s+(\d{1,4})\s+fps\b")]
    private static partial Regex FrameRatePattern();

    /// <summary>
    /// The frame rate in one of scrcpy's counter lines ("INFO: 60 fps", or with "(+2 frames
    /// skipped)" after it), or null for any other line.
    /// </summary>
    public static int? ParseFrameRate(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var match = FrameRatePattern().Match(line);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Modifier for scrcpy's own keyboard shortcuts. scrcpy 4.1 no longer accepts combined
    /// modifiers, so use the rarely pressed Right Ctrl key and send that same key from the app.
    /// </summary>
    public const string ShortcutModifier = "rctrl";

    /// <summary>A video codec as people write it: H.264, H.265 or AV1.</summary>
    public static string CodecName(string codec) => codec.ToLowerInvariant() switch
    {
        "h264" => "H.264",
        "h265" => "H.265",
        "av1" => "AV1",
        _ => codec,
    };

    [GeneratedRegex(@"Video encoder '(?<name>[^']+)' for (?<codec>[a-z0-9]+) not found")]
    private static partial Regex MissingEncoderPattern();

    /// <summary>
    /// What to say when scrcpy stopped because the phone has no encoder by the name it was given
    /// (a name chosen for another codec, or for another phone), or null when that is not why.
    /// </summary>
    public static string? MissingEncoder(IEnumerable<string> output)
    {
        foreach (var line in output)
        {
            if (MissingEncoderPattern().Match(line) is { Success: true } match)
            {
                return $"The phone has no video encoder called {match.Groups["name"].Value} for {CodecName(match.Groups["codec"].Value)}. " +
                       "Choose another in Settings, Picture, or let the phone choose.";
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\d+(K|M)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BitRatePattern();

    public static bool IsValidBitRate(string? value) =>
        !string.IsNullOrWhiteSpace(value) && BitRatePattern().IsMatch(value.Trim());

    public static IReadOnlyList<string> Build(
        RexConfig config,
        string serial,
        bool isTcp,
        string windowTitle,
        (int X, int Y, int Width, int Height)? window,
        string? recordPath,
        string? keyboardMode = null,
        int? copyIndex = null,
        int? displayOrientation = null)
    {
        keyboardMode ??= KeyboardModeFor(config, null);
        var isCopy = copyIndex is not null;
        if (keyboardMode is not (FullKeyboardMode or CompatibilityKeyboardMode))
        {
            throw new ArgumentException("Keyboard mode must be uhid or sdk.", nameof(keyboardMode));
        }

        var args = new List<string>
        {
            "--serial=" + serial,
            "--window-title=" + windowTitle,
            "--window-borderless",
            "--background-color=" + LetterboxFor(config.Mirror.Backdrop),
            "--no-window-aspect-ratio-lock",
            "--mouse=sdk",
            "--keyboard=" + keyboardMode,
            "--shortcut-mod=" + ShortcutModifier,
        };
        if (keyboardMode == CompatibilityKeyboardMode)
        {
            // The embedded SDL window cannot become a top-level foreground window. In SDK mode,
            // SDL consequently drops text events (the path scrcpy normally uses for digits and
            // punctuation). Raw key events take the same working path as letters.
            args.Add("--raw-key-events");
        }

        args.Add("--port=" + (isCopy ? CopyPort(copyIndex!.Value) : MainPort).ToString(CultureInfo.InvariantCulture));
        if (isCopy)
        {
            // A copy leaves the phone exactly as it found it. Every scrcpy session restores what
            // it changed when it exits, knowing nothing of the others: a copy that had turned the
            // screen off or kept it awake would, on closing, undo that for the main session still
            // running. So copies change nothing (no cleanup, no power-on, none of the power
            // options below), carry no audio (the main session plays it) and record nothing.
            args.Add("--no-cleanup");
            args.Add("--no-power-on");
        }

        if (window is { } w)
        {
            args.Add("--window-x=" + w.X.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-y=" + w.Y.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-width=" + w.Width.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-height=" + w.Height.ToString(CultureInfo.InvariantCulture));
        }

        var session = config.Session;
        if (session.TurnScreenOff && !isCopy)
        {
            args.Add("--turn-screen-off");
        }

        if (session.StayAwake && !isTcp && !isCopy)
        {
            args.Add("--stay-awake");
        }

        if (session.KeepActive && !isCopy)
        {
            args.Add("--keep-active");
        }

        if (session.PowerOffOnClose && !isCopy)
        {
            args.Add("--power-off-on-close");
        }

        // The phone's own screen timeout is changed and put back by the session that changed it,
        // so only the main one may; keeping this PC awake needs saying once.
        if (session.ScreenOffTimeoutSeconds > 0 && !isCopy)
        {
            args.Add("--screen-off-timeout=" + session.ScreenOffTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        }

        if (session.KeepPcAwake && !isCopy)
        {
            args.Add("--disable-screensaver");
        }

        var mirror = config.Mirror;
        var maxSize = isCopy && config.Copies.MaxSize > 0 ? config.Copies.MaxSize : mirror.MaxSize;
        if (maxSize > 0)
        {
            args.Add("--max-size=" + maxSize.ToString(CultureInfo.InvariantCulture));
        }

        var maxFps = isCopy && config.Copies.MaxFps > 0 ? config.Copies.MaxFps : mirror.MaxFps;
        if (maxFps > 0)
        {
            args.Add("--max-fps=" + maxFps.ToString(CultureInfo.InvariantCulture));
        }

        if (IsValidBitRate(mirror.VideoBitRate))
        {
            args.Add("--video-bit-rate=" + mirror.VideoBitRate.Trim());
        }

        args.Add("--video-codec=" + mirror.VideoCodec);
        AddPicture(args, mirror);

        if (mirror.VideoBufferMs > 0)
        {
            args.Add("--video-buffer=" + mirror.VideoBufferMs.ToString(CultureInfo.InvariantCulture));
        }

        if (!mirror.DownsizeOnError)
        {
            args.Add("--no-downsize-on-error");
        }

        if (mirror.RenderDriver.Length > 0)
        {
            args.Add("--render-driver=" + mirror.RenderDriver);
        }

        if (!mirror.Audio || isCopy)
        {
            args.Add("--no-audio");
        }
        else
        {
            args.Add("--audio-codec=" + mirror.AudioCodec);
            if (mirror.AudioBufferMs > 0)
            {
                args.Add("--audio-buffer=" + mirror.AudioBufferMs.ToString(CultureInfo.InvariantCulture));
            }

            if (mirror.AudioSource != "auto")
            {
                args.Add("--audio-source=" + mirror.AudioSource);
            }

            if (IsValidBitRate(mirror.AudioBitRate) && !string.Equals(mirror.AudioBitRate.Trim(), MirrorSettings.DefaultAudioBitRate, StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--audio-bit-rate=" + mirror.AudioBitRate.Trim());
            }

            // scrcpy only keeps the playback going on the phone, and refuses to start when asked
            // to with any other source.
            if (mirror.AudioDup && mirror.AudioDupPossible)
            {
                args.Add("--audio-dup");
            }

            if (mirror.AudioOutputBufferMs > 0)
            {
                args.Add("--audio-output-buffer=" + mirror.AudioOutputBufferMs.ToString(CultureInfo.InvariantCulture));
            }

            if (mirror.RequireAudio)
            {
                args.Add("--require-audio");
            }
        }

        AddInput(args, config.Input, keyboardMode, isCopy);

        // The counter only needs to run in the session the status bar reads.
        if (config.App.ShowFrameRate && !isCopy)
        {
            args.Add(PrintFps);
        }

        if (session.StartApp.Length > 0 && SessionSettings.IsValidStartApp(session.StartApp) && !isCopy)
        {
            // Once: a copy that started the app again would restart it under the main view.
            args.Add("--start-app=" + session.StartApp.Trim());
        }

        if (recordPath is not null && !isCopy)
        {
            args.Add("--record=" + recordPath);
        }

        // Taps shown on the phone are a phone setting the session puts back when it ends, so only
        // a session that cleans up may change it; a time limit ends the mirror, not a view of it.
        if (mirror.ShowTouches && !isCopy)
        {
            args.Add("--show-touches");
        }

        if (mirror.TimeLimitMinutes > 0 && !isCopy)
        {
            args.Add("--time-limit=" + (mirror.TimeLimitMinutes * 60).ToString(CultureInfo.InvariantCulture));
        }

        // Before the extra arguments, so an orientation given there still wins, as it always has.
        if (!isCopy && DisplayOrientation.Parse(mirror.StartOrientation) is { } start and not DisplayOrientation.Upright)
        {
            args.Add("--display-orientation=" + DisplayOrientation.Name(start));
        }

        args.AddRange(SplitExtraArgs(mirror.ExtraArgs));

        // scrcpy's own drops (a file let go straight onto the picture) go where the window's do.
        // The typed setting wins over one in the extra arguments. A new folder applies at once to
        // the window's drops and to scrcpy's at its next start, so it never asks for a restart
        // (LaunchSettings leaves it out); turning drops off does, since scrcpy takes them itself.
        if (!isCopy)
        {
            args.RemoveAll(argument => argument.StartsWith(PushTarget, StringComparison.Ordinal));
            args.Add(PushTarget + config.Transfer.Folder);
            if (!config.Transfer.Enabled)
            {
                args.Add(NoFileDrop);
            }
        }

        // After the extra arguments, so it wins over an orientation given there: the main view may
        // have been turned since it started, and a copy opens showing the picture the same way.
        if (isCopy && displayOrientation is { } orientation)
        {
            args.Add("--display-orientation=" + DisplayOrientation.Name(orientation));
        }

        return args;
    }

    /// <summary>
    /// What the phone sends as the picture: its encoder, a part of its screen, how it is captured
    /// as the phone turns and tilted, and how this PC scales it. Every view of the phone shows the
    /// same picture, so copies take these too.
    /// </summary>
    private static void AddPicture(List<string> args, MirrorSettings mirror)
    {
        if (MirrorSettings.IsValidEncoderName(mirror.VideoEncoder))
        {
            args.Add("--video-encoder=" + mirror.VideoEncoder.Trim());
        }

        if (mirror.Crop.Length > 0 && MirrorSettings.WhyNotCrop(mirror.Crop) is null)
        {
            args.Add("--crop=" + mirror.Crop.Trim());
        }

        if (mirror.CaptureOrientation.Length > 0 && MirrorSettings.CaptureOrientations.Contains(mirror.CaptureOrientation))
        {
            args.Add("--capture-orientation=" + mirror.CaptureOrientation);
        }

        if (mirror.Angle is > 0 and <= MirrorSettings.AngleUpperBound)
        {
            args.Add("--angle=" + mirror.Angle.ToString(CultureInfo.InvariantCulture));
        }

        if (!mirror.SmoothScaling)
        {
            args.Add("--no-mipmaps");
        }
    }

    public enum FileTransferPhase
    {
        Requested,
        Started,
        Succeeded,
        Failed,
    }

    public sealed record FileTransferLine(FileTransferPhase Phase, bool Install, string Path, string? Target = null);

    [GeneratedRegex(@"^(?:\[[^\]]+\]\s+)?(?:INFO|WARN|ERROR):\s+(?<message>.*)$")]
    private static partial Regex LogMessage();

    /// <summary>One of scrcpy's own install/push status lines, or null for ordinary output.</summary>
    public static FileTransferLine? ParseFileTransfer(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = LogMessage().Match(line.Trim());
        var message = match.Success ? match.Groups["message"].Value : line.Trim();
        return Prefix(message, "Request to install ", FileTransferPhase.Requested, install: true)
            ?? Prefix(message, "Installing ", FileTransferPhase.Started, install: true, trimDots: true)
            ?? Prefix(message, "Failed to install ", FileTransferPhase.Failed, install: true)
            ?? Suffix(message, " successfully installed", FileTransferPhase.Succeeded, install: true)
            ?? Prefix(message, "Request to push ", FileTransferPhase.Requested, install: false)
            ?? Prefix(message, "Pushing ", FileTransferPhase.Started, install: false, trimDots: true)
            ?? Pushed(message, failed: false)
            ?? Pushed(message, failed: true);
    }

    private static FileTransferLine? Prefix(string message, string prefix, FileTransferPhase phase, bool install, bool trimDots = false)
    {
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var path = message[prefix.Length..];
        if (trimDots && path.EndsWith("...", StringComparison.Ordinal))
        {
            path = path[..^3];
        }

        return path.Length == 0 ? null : new FileTransferLine(phase, install, path);
    }

    private static FileTransferLine? Suffix(string message, string suffix, FileTransferPhase phase, bool install) =>
        message.EndsWith(suffix, StringComparison.Ordinal) && message.Length > suffix.Length
            ? new FileTransferLine(phase, install, message[..^suffix.Length])
            : null;

    private static FileTransferLine? Pushed(string message, bool failed)
    {
        var prefix = failed ? "Failed to push " : string.Empty;
        var divider = failed ? " to " : " successfully pushed to ";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var at = message.LastIndexOf(divider, StringComparison.Ordinal);
        if (at <= prefix.Length || at + divider.Length >= message.Length)
        {
            return null;
        }

        return new FileTransferLine(failed ? FileTransferPhase.Failed : FileTransferPhase.Succeeded, false,
            message[prefix.Length..at], message[(at + divider.Length)..]);
    }

    [GeneratedRegex(@"\bTexture:\s*(\d{1,5})x(\d{1,5})\b")]
    private static partial Regex TexturePattern();

    /// <summary>
    /// The video size scrcpy reports each time the picture changes shape ("INFO: Texture: 1600x720"),
    /// or null for any other line. It is printed at the first frame and again whenever the phone
    /// turns, so it says what shape the picture is without depending on scrcpy resizing its window.
    /// </summary>
    public static (int Width, int Height)? ParseTextureSize(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var match = TexturePattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    /// <summary>
    /// UHID is the only scrcpy keyboard mode with complete layout-aware typing, but a few old
    /// Android builds deny access to /dev/uhid. Recognize that narrow failure so the session may
    /// retry once with SDK raw-key compatibility without hiding unrelated startup failures.
    /// </summary>
    public static bool IsUhidPermissionFailure(IEnumerable<string> stderr)
    {
        var text = string.Join('\n', stderr);
        var mentionsUhid = text.Contains("UhidManager", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("UHID", StringComparison.OrdinalIgnoreCase) ||
                           text.Contains("/dev/uhid", StringComparison.OrdinalIgnoreCase);
        var denied = text.Contains("EACCES", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("Failed to enable", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("not permitted", StringComparison.OrdinalIgnoreCase);
        return mentionsUhid && denied;
    }

    /// <summary>
    /// How the mouse buttons, the keys, the clipboard and game controllers reach the phone. Every
    /// view of the phone takes input the same way, so copies get the same options, except game
    /// controllers: each session would hand the phone another controller.
    /// </summary>
    private static void AddInput(List<string> args, InputSettings input, string keyboardMode, bool isCopy)
    {
        if (input.MouseBind != DefaultMouseBind)
        {
            args.Add("--mouse-bind=" + input.MouseBind);
        }

        // scrcpy refuses --no-key-repeat unless the keyboard is its raw-key one; the hardware
        // keyboard repeats a held key the way Android does for any plugged-in keyboard.
        if (!input.KeyRepeat && keyboardMode == CompatibilityKeyboardMode)
        {
            args.Add("--no-key-repeat");
        }

        if (!input.MouseHover)
        {
            args.Add("--no-mouse-hover");
        }

        if (!input.ClipboardAutosync)
        {
            args.Add("--no-clipboard-autosync");
        }

        if (input.LegacyPaste)
        {
            args.Add("--legacy-paste");
        }

        if (input.Gamepad == "uhid" && !isCopy)
        {
            args.Add("--gamepad=uhid");
        }
    }

    /// <summary>
    /// Splits the raw extra-argument string like a shell (quotes and backslash escapes) and
    /// rejects anything that would break the embedded session.
    /// </summary>
    public static IReadOnlyList<string> SplitExtraArgs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (text.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new FormatException("Extra scrcpy arguments cannot contain line breaks.");
        }

        var tokens = new List<string>();
        var builder = new StringBuilder();
        var quote = '\0';
        var escapeNext = false;

        foreach (var ch in text)
        {
            if (escapeNext)
            {
                builder.Append(ch);
                escapeNext = false;
                continue;
            }

            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                else if (quote == '"' && ch == '\\')
                {
                    escapeNext = true;
                }
                else
                {
                    builder.Append(ch);
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                Flush(tokens, builder);
            }
            else
            {
                builder.Append(ch);
            }
        }

        if (escapeNext || quote != '\0')
        {
            throw new FormatException("Extra scrcpy arguments contain an unterminated quote.");
        }

        Flush(tokens, builder);

        foreach (var token in tokens)
        {
            if (IsForbiddenExtra(token))
            {
                throw new FormatException($"'{token}' is managed by Android Headless Mirror and cannot be overridden.");
            }
        }

        return tokens;
    }

    public static bool IsForbiddenExtra(string token) =>
        Regex.IsMatch(token, @"^(-s|-S|-n|-f|-r)$") ||
        Regex.IsMatch(token, @"^--(serial|window-title|window-borderless|window-x|window-y|window-width|window-height|mouse|keyboard|shortcut-mod|fullscreen|record|no-window-aspect-ratio-lock|background-color|port|no-cleanup|no-power-on)(=|$)") ||
        token is "--no-control" or "--no-window" or "--no-video" or "--otg" or "--no-video-playback";

    private static void Flush(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
            builder.Clear();
        }
    }
}
