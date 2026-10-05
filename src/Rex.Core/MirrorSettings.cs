using System.Text.Json.Serialization;

namespace Rex.Core;

/// <summary>Video, audio and recording options passed to scrcpy at launch.</summary>
public sealed record MirrorSettings
{
    /// <summary>The value if it is one of the choices (compared without case), else the fallback.</summary>
    internal static string OneOf(IReadOnlyList<string> choices, string? value, string fallback)
    {
        var text = value?.Trim() ?? string.Empty;
        return choices.FirstOrDefault(choice => string.Equals(choice, text, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    public const int MaxSizeUpperBound = 8192;
    public const int FpsUpperBound = 240;
    public static readonly string[] VideoCodecs = ["h264", "h265", "av1"];
    public static readonly string[] AudioCodecs = ["opus", "aac", "flac", "raw"];
    public const int VideoBufferUpperBound = 1000;
    public const int AngleUpperBound = 359;
    public const int TimeLimitUpperBound = 1440;
    public const int AudioOutputBufferUpperBound = 1000;
    public const int EncoderNameMaxLength = 100;

    /// <summary>What may show around the picture: the app's ink, or black.</summary>
    public static readonly string[] Backdrops = ["ink", "black"];

    /// <summary>How the phone's screen may be captured as it turns: following it, kept as it started, or locked.</summary>
    public static readonly string[] CaptureOrientations = ["", "@", "@0", "@90", "@180", "@270"];

    /// <summary>What scrcpy may capture as audio (--audio-source); "auto" is scrcpy's own choice.</summary>
    public static readonly string[] AudioSources =
    [
        "auto", "output", "playback", "mic", "mic-unprocessed", "mic-camcorder", "mic-voice-recognition",
        "mic-voice-communication", "voice-call", "voice-call-uplink", "voice-call-downlink", "voice-performance",
    ];

    public const string DefaultAudioBitRate = "128K";

    /// <summary>Renderers SDL can be asked for (--render-driver); empty lets it choose.</summary>
    public static readonly string[] RenderDrivers = ["", "direct3d", "opengl", "opengles2", "software"];

    /// <summary>Containers a recording can be written in; scrcpy picks the format from the file's extension.</summary>
    public static readonly string[] RecordFormats = ["mp4", "mkv"];

    /// <summary>Longest side of the encoded video in pixels. 0 keeps the device resolution.</summary>
    public int MaxSize { get; set; } = 1920;

    /// <summary>Frame-rate cap. 0 lets scrcpy pick.</summary>
    public int MaxFps { get; set; } = 60;

    /// <summary>scrcpy bit-rate expression, for example 8M or 12000K.</summary>
    public string VideoBitRate { get; set; } = "12M";

    public string VideoCodec { get; set; } = "h264";
    public bool Audio { get; set; } = true;
    public string AudioCodec { get; set; } = "opus";
    public int AudioBufferMs { get; set; } = 50;

    /// <summary>Keep playing audio on the phone too (Android 13+).</summary>
    public bool AudioDup { get; set; }

    /// <summary>
    /// What is captured as audio: "auto" (everything the phone plays, or the playback when it keeps
    /// playing on the phone too), the whole output, the playback apps allow, a microphone, or a call.
    /// </summary>
    public string AudioSource { get; set; } = "auto";

    /// <summary>scrcpy bit-rate expression for the audio, for example 128K.</summary>
    public string AudioBitRate { get; set; } = DefaultAudioBitRate;

    /// <summary>Delay before each frame is shown, in milliseconds, to even out a shaky connection. 0 shows frames at once.</summary>
    public int VideoBufferMs { get; set; }

    /// <summary>When the phone's encoder fails, let scrcpy try again at a lower resolution rather than stop.</summary>
    public bool DownsizeOnError { get; set; } = true;

    /// <summary>The renderer scrcpy asks SDL for; empty lets SDL choose.</summary>
    public string RenderDriver { get; set; } = string.Empty;

    /// <summary>The container recordings are written in: mp4 or mkv (which survives a recording cut short).</summary>
    public string RecordFormat { get; set; } = "mp4";

    /// <summary>Whether the audio source allows the audio to keep playing on the phone (scrcpy only duplicates the playback).</summary>
    [JsonIgnore]
    public bool AudioDupPossible => AudioSource is "auto" or "playback";

    /// <summary>Record every session to <see cref="RecordDirectory"/>.</summary>
    public bool RecordOnStart { get; set; }

    public string RecordDirectory { get; set; } = "captures/recordings";

    /// <summary>Extra raw scrcpy arguments for options the app does not expose.</summary>
    public string ExtraArgs { get; set; } = string.Empty;

    /// <summary>
    /// Start every session with scrcpy's raw-key (sdk) keyboard instead of the hardware (UHID)
    /// one. Off by default: the hardware keyboard is what makes numbers, shifted symbols and AltGr
    /// follow the phone's own layout. Turn it on when typing misbehaves on a particular phone.
    /// </summary>
    public bool CompatibilityKeyboard { get; set; }

    /// <summary>The phone's video encoder by name (scrcpy --video-encoder); empty lets the phone choose.</summary>
    public string VideoEncoder { get; set; } = string.Empty;

    /// <summary>
    /// Only part of the phone's screen, as width:height:x:y in the phone's own pixels (scrcpy
    /// --crop). Empty shows the whole screen.
    /// </summary>
    public string Crop { get; set; } = string.Empty;

    /// <summary>
    /// How the phone's screen is captured as it turns (scrcpy --capture-orientation): empty
    /// follows the phone, "@" keeps the way it was when the mirror started, "@0" to "@270" lock it.
    /// </summary>
    public string CaptureOrientation { get; set; } = string.Empty;

    /// <summary>How the PC view is turned when the mirror starts (scrcpy --display-orientation), by scrcpy's names.</summary>
    public string StartOrientation { get; set; } = "0";

    /// <summary>Tilts the picture by this many degrees clockwise (scrcpy --angle). 0 leaves it straight.</summary>
    public int Angle { get; set; }

    /// <summary>Smooth the picture when it is shown smaller than the video; off adds scrcpy's --no-mipmaps.</summary>
    public bool SmoothScaling { get; set; } = true;

    /// <summary>The phone draws a dot where it is touched while it is mirrored (scrcpy --show-touches, put back when the mirror ends).</summary>
    public bool ShowTouches { get; set; }

    /// <summary>Stop the mirror after this many minutes (scrcpy --time-limit). 0 never stops it.</summary>
    public int TimeLimitMinutes { get; set; }

    /// <summary>Sound held on this PC before it plays, in milliseconds (scrcpy --audio-output-buffer). 0 keeps scrcpy's own.</summary>
    public int AudioOutputBufferMs { get; set; }

    /// <summary>No mirror at all when the phone cannot send its sound (scrcpy --require-audio), instead of going on silent.</summary>
    public bool RequireAudio { get; set; }

    /// <summary>What shows around the picture: the app's own ink, or pure black (for an OLED screen).</summary>
    public string Backdrop { get; set; } = "ink";

    public MirrorSettings Copy() => this with { };

    /// <summary>
    /// Whether a name could be one of the phone's encoders: letters, digits, dots, dashes and
    /// underscores, as Android names them. It is passed to scrcpy as one argument, never a shell.
    /// </summary>
    public static bool IsValidEncoderName(string? name)
    {
        var text = name?.Trim() ?? string.Empty;
        return text.Length is > 0 and <= EncoderNameMaxLength && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    }

    /// <summary>
    /// Why a crop cannot be used, or null when it can (empty shows the whole screen): it must be
    /// width:height:x:y in whole pixels, with a width and height of at least 1.
    /// </summary>
    public static string? WhyNotCrop(string? crop)
    {
        var text = crop?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        var parts = text.Split(':');
        if (parts.Length != 4 || !parts.All(p => p.Length is > 0 and <= 4 && p.All(char.IsAsciiDigit)))
        {
            return "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.";
        }

        var numbers = parts.Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return numbers[0] == 0 || numbers[1] == 0 ? "The width and height must be at least 1 pixel."
            : numbers.Any(n => n > MaxSizeUpperBound) ? $"Each number must be at most {MaxSizeUpperBound}."
            : null;
    }

    public void Normalize()
    {
        MaxSize = Math.Clamp(MaxSize, 0, MaxSizeUpperBound);
        MaxFps = Math.Clamp(MaxFps, 0, FpsUpperBound);
        VideoBitRate = ScrcpyArguments.IsValidBitRate(VideoBitRate) ? VideoBitRate.Trim() : "12M";
        VideoCodec = VideoCodecs.Contains(VideoCodec, StringComparer.OrdinalIgnoreCase) ? VideoCodec.ToLowerInvariant() : "h264";
        AudioCodec = AudioCodecs.Contains(AudioCodec, StringComparer.OrdinalIgnoreCase) ? AudioCodec.ToLowerInvariant() : "opus";
        AudioBufferMs = Math.Clamp(AudioBufferMs, 0, 5000);
        AudioSource = OneOf(AudioSources, AudioSource, "auto");
        AudioBitRate = ScrcpyArguments.IsValidBitRate(AudioBitRate) ? AudioBitRate.Trim().ToUpperInvariant() : DefaultAudioBitRate;
        VideoBufferMs = Math.Clamp(VideoBufferMs, 0, VideoBufferUpperBound);
        RenderDriver = OneOf(RenderDrivers, RenderDriver, string.Empty);
        RecordFormat = OneOf(RecordFormats, RecordFormat, "mp4");
        RecordDirectory = PathRules.IsSafeRelativePath(RecordDirectory) ? RecordDirectory.Trim() : "captures/recordings";
        VideoEncoder = IsValidEncoderName(VideoEncoder) ? VideoEncoder.Trim() : string.Empty;
        Crop = WhyNotCrop(Crop) is null ? (Crop ?? string.Empty).Trim() : string.Empty;
        CaptureOrientation = OneOf(CaptureOrientations, CaptureOrientation, string.Empty);
        StartOrientation = DisplayOrientation.Parse(StartOrientation?.Trim()) is { } start ? DisplayOrientation.Name(start) : "0";
        Angle = ((Angle % 360) + 360) % 360;
        TimeLimitMinutes = Math.Clamp(TimeLimitMinutes, 0, TimeLimitUpperBound);
        AudioOutputBufferMs = Math.Clamp(AudioOutputBufferMs, 0, AudioOutputBufferUpperBound);
        Backdrop = OneOf(Backdrops, Backdrop, "ink");
        ExtraArgs = (ExtraArgs ?? string.Empty).Trim();
        if (ExtraArgs.Length > 4096 || ExtraArgs.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            ExtraArgs = string.Empty;
        }
        else
        {
            try
            {
                _ = ScrcpyArguments.SplitExtraArgs(ExtraArgs);
            }
            catch (FormatException)
            {
                ExtraArgs = string.Empty;
            }
        }
    }
}
