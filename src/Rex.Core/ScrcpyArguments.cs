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

    public static IReadOnlyList<string> LaunchSettings(RexConfig config, bool isTcp) =>
        Build(config, "", isTcp, "", null,
            config.Mirror.RecordOnStart ? config.Mirror.RecordDirectory : null);

    /// <summary>
    /// Modifier for scrcpy's own keyboard shortcuts. scrcpy 4.1 no longer accepts combined
    /// modifiers, so use the rarely pressed Right Ctrl key and send that same key from the app.
    /// </summary>
    public const string ShortcutModifier = "rctrl";

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
        string? keyboardMode = null)
    {
        keyboardMode ??= KeyboardModeFor(config, null);
        if (keyboardMode is not (FullKeyboardMode or CompatibilityKeyboardMode))
        {
            throw new ArgumentException("Keyboard mode must be uhid or sdk.", nameof(keyboardMode));
        }

        var args = new List<string>
        {
            "--serial=" + serial,
            "--window-title=" + windowTitle,
            "--window-borderless",
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

        if (window is { } w)
        {
            args.Add("--window-x=" + w.X.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-y=" + w.Y.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-width=" + w.Width.ToString(CultureInfo.InvariantCulture));
            args.Add("--window-height=" + w.Height.ToString(CultureInfo.InvariantCulture));
        }

        var session = config.Session;
        if (session.TurnScreenOff)
        {
            args.Add("--turn-screen-off");
        }

        if (session.StayAwake && !isTcp)
        {
            args.Add("--stay-awake");
        }

        if (session.KeepActive)
        {
            args.Add("--keep-active");
        }

        if (session.PowerOffOnClose)
        {
            args.Add("--power-off-on-close");
        }

        var mirror = config.Mirror;
        if (mirror.MaxSize > 0)
        {
            args.Add("--max-size=" + mirror.MaxSize.ToString(CultureInfo.InvariantCulture));
        }

        if (mirror.MaxFps > 0)
        {
            args.Add("--max-fps=" + mirror.MaxFps.ToString(CultureInfo.InvariantCulture));
        }

        if (IsValidBitRate(mirror.VideoBitRate))
        {
            args.Add("--video-bit-rate=" + mirror.VideoBitRate.Trim());
        }

        args.Add("--video-codec=" + mirror.VideoCodec);

        if (!mirror.Audio)
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

            if (mirror.AudioDup)
            {
                args.Add("--audio-dup");
            }
        }

        if (recordPath is not null)
        {
            args.Add("--record=" + recordPath);
        }

        args.AddRange(SplitExtraArgs(mirror.ExtraArgs));
        return args;
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
        Regex.IsMatch(token, @"^--(serial|window-title|window-borderless|window-x|window-y|window-width|window-height|mouse|keyboard|shortcut-mod|fullscreen|record|no-window-aspect-ratio-lock)(=|$)") ||
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
