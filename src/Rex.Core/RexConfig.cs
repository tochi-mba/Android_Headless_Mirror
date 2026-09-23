using System.Text.Json.Serialization;

namespace Rex.Core;

/// <summary>
/// Typed configuration (config.json, schema version 2). Every property has a safe
/// default so a missing or partial file always loads. Keep this the single
/// definition of what a setting means; the CLI's path-based access and the GUI
/// both read and write this shape.
/// </summary>
public sealed record RexConfig
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public MirrorSettings Mirror { get; set; } = new();
    public SessionSettings Session { get; set; } = new();
    public WirelessSettings Wireless { get; set; } = new();
    public TouchpadSettings Touchpad { get; set; } = new();
    public ZoomSettings Zoom { get; set; } = new();
    public AmbientSettings Ambient { get; set; } = new();
    public HudSettings Hud { get; set; } = new();
    public PatternGuideSettings PatternGuide { get; set; } = new();
    public AppSettings App { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>Clamps every value into its supported range. Called after load and before save.</summary>
    public void Normalize()
    {
        Version = CurrentVersion;
        Mirror.Normalize();
        Session.Normalize();
        Wireless.Normalize();
        Touchpad.Normalize();
        Zoom.Normalize();
        Ambient.Normalize();
        Hud.Normalize();
        PatternGuide.Normalize();
        App.Normalize();
        Logging.Normalize();
    }

    public RexConfig Copy() => this with
    {
        Mirror = Mirror.Copy(),
        Session = Session.Copy(),
        Wireless = Wireless.Copy(),
        Touchpad = Touchpad.Copy(),
        Zoom = Zoom.Copy(),
        Ambient = Ambient.Copy(),
        Hud = Hud.Copy(),
        PatternGuide = PatternGuide.Copy(),
        App = App.Copy(),
        Logging = Logging.Copy(),
    };
}

/// <summary>Video, audio and recording options passed to scrcpy at launch.</summary>
public sealed record MirrorSettings
{
    public const int MaxSizeUpperBound = 8192;
    public const int FpsUpperBound = 240;
    public static readonly string[] VideoCodecs = ["h264", "h265", "av1"];
    public static readonly string[] AudioCodecs = ["opus", "aac", "flac", "raw"];

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

    /// <summary>Record every session to <see cref="RecordDirectory"/>.</summary>
    public bool RecordOnStart { get; set; }

    public string RecordDirectory { get; set; } = "captures/recordings";

    /// <summary>Extra raw scrcpy arguments for options the app does not expose.</summary>
    public string ExtraArgs { get; set; } = string.Empty;

    public MirrorSettings Copy() => this with { };

    public void Normalize()
    {
        MaxSize = Math.Clamp(MaxSize, 0, MaxSizeUpperBound);
        MaxFps = Math.Clamp(MaxFps, 0, FpsUpperBound);
        VideoBitRate = ScrcpyArguments.IsValidBitRate(VideoBitRate) ? VideoBitRate.Trim() : "12M";
        VideoCodec = VideoCodecs.Contains(VideoCodec, StringComparer.OrdinalIgnoreCase) ? VideoCodec.ToLowerInvariant() : "h264";
        AudioCodec = AudioCodecs.Contains(AudioCodec, StringComparer.OrdinalIgnoreCase) ? AudioCodec.ToLowerInvariant() : "opus";
        AudioBufferMs = Math.Clamp(AudioBufferMs, 0, 5000);
        RecordDirectory = PathRules.IsSafeRelativePath(RecordDirectory) ? RecordDirectory.Trim() : "captures/recordings";
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

/// <summary>How a mirror session behaves on the phone and what happens when it ends.</summary>
public sealed record SessionSettings
{
    /// <summary>Turn the physical display off while mirroring (scrcpy --turn-screen-off).</summary>
    public bool TurnScreenOff { get; set; } = true;

    /// <summary>Keep the phone awake while plugged in over USB (scrcpy --stay-awake).</summary>
    public bool StayAwake { get; set; } = true;

    /// <summary>Periodically report user activity so Android does not sleep (scrcpy --keep-active).</summary>
    public bool KeepActive { get; set; } = true;

    /// <summary>Send KEYCODE_WAKEUP before launching.</summary>
    public bool WakeBeforeMirror { get; set; } = true;

    /// <summary>Ask Android to dismiss an insecure keyguard before launching.</summary>
    public bool DismissKeyguard { get; set; } = true;

    /// <summary>Turn the phone screen off when the mirror closes (scrcpy --power-off-on-close).</summary>
    public bool PowerOffOnClose { get; set; }

    /// <summary>Relaunch scrcpy after an unexpected exit while the device stays connected.</summary>
    public bool RestartOnUnexpectedExit { get; set; } = true;

    /// <summary>Prefer a USB device over a wireless one when both are ready.</summary>
    public bool PreferUsb { get; set; } = true;

    /// <summary>Optional serial to prefer when several devices are ready. Never a lock.</summary>
    public string PreferredSerial { get; set; } = string.Empty;

    public int PollSeconds { get; set; } = 1;
    public int RetrySeconds { get; set; } = 4;

    public SessionSettings Copy() => this with { };

    public void Normalize()
    {
        PreferredSerial = (PreferredSerial ?? string.Empty).Trim();
        PollSeconds = Math.Clamp(PollSeconds, 1, 30);
        RetrySeconds = Math.Clamp(RetrySeconds, 1, 60);
    }
}

/// <summary>Optional ADB-over-TCP/IP fallback. Off by default; USB is the recovery path.</summary>
public sealed record WirelessSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 5555;

    /// <summary>While a USB session exists, switch the phone's ADB daemon to TCP/IP and remember its IP.</summary>
    public bool EnableTcpipWhenUsbAvailable { get; set; }

    public List<string> ManualHosts { get; set; } = [];

    public WirelessSettings Copy() => this with { ManualHosts = [.. ManualHosts] };

    public void Normalize()
    {
        Port = Math.Clamp(Port, 1, 65535);
        ManualHosts = (ManualHosts ?? [])
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>Windows Precision Touchpad bridging. Two fingers on the touchpad become two fingers on the phone.</summary>
public sealed record TouchpadSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Forward two-finger gestures (pinch, rotate, pan, scroll) to Android as real touch.</summary>
    public bool TwoFingerToAndroid { get; set; } = true;

    /// <summary>How far the phone fingers move per millimetre of touchpad travel. 1.0 is neutral.</summary>
    public double Sensitivity { get; set; } = 1.0;

    public TouchpadSettings Copy() => this with { };

    public void Normalize()
    {
        Sensitivity = double.IsFinite(Sensitivity) ? Math.Clamp(Sensitivity, 0.25, 3.0) : 1.0;
    }
}

/// <summary>
/// The floating controls in fullscreen: which buttons, where they sit, how big they are and how
/// long they stay. The bar hides itself and comes back when the pointer reaches its own edge.
/// </summary>
public sealed record HudSettings
{
    public static readonly string[] Positions =
        ["top", "bottom", "left", "right", "top-left", "top-right", "bottom-left", "bottom-right"];

    /// <summary>What a new install starts with: the handful of controls that matter in fullscreen.</summary>
    public static readonly string[] DefaultButtons =
        ["back", "home", "recents", "rotation-portrait", "rotation-landscape", "rotation-auto", "zoom-reset", "screenshot", "fullscreen"];

    public bool Enabled { get; set; } = true;

    public string Position { get; set; } = "top";

    /// <summary>
    /// Where the bar was dragged to, as a fraction of the mirror area measured to the middle of the
    /// bar. Null means it sits wherever <see cref="Position"/> puts it, which is what choosing a
    /// position in settings goes back to.
    /// </summary>
    public double? X { get; set; }

    public double? Y { get; set; }

    /// <summary>True once the bar has been dragged somewhere of its own.</summary>
    [JsonIgnore]
    public bool IsPlaced => X is not null && Y is not null;

    /// <summary>Action ids from <c>MirrorActions</c>, in the order they appear.</summary>
    public List<string> Buttons { get; set; } = [.. DefaultButtons];

    /// <summary>Seconds the bar stays after the pointer leaves it.</summary>
    public double HideSeconds { get; set; } = 3;

    /// <summary>Size of the whole bar: 0.75 (compact) to 1.75 (large).</summary>
    public double Scale { get; set; } = 1;

    public double Opacity { get; set; } = 1;

    /// <summary>Show the one-line status message under the buttons.</summary>
    public bool ShowMessages { get; set; } = true;

    public HudSettings Copy() => this with { Buttons = [.. Buttons] };

    public void Normalize()
    {
        Position = Positions.Contains(Position?.ToLowerInvariant() ?? "", StringComparer.Ordinal) ? Position!.ToLowerInvariant() : "top";
        X = X is { } x && double.IsFinite(x) ? Math.Clamp(x, 0, 1) : null;
        Y = Y is { } y && double.IsFinite(y) ? Math.Clamp(y, 0, 1) : null;
        HideSeconds = double.IsFinite(HideSeconds) ? Math.Clamp(HideSeconds, 1, 15) : 3;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.75, 1.75) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.3, 1) : 1;
        Buttons = Buttons
            .Where(id => MirrorActions.Find(id) is not null)
            .Select(id => MirrorActions.Find(id)!.Id)
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToList();
    }
}

/// <summary>
/// The soft background: a blurred copy of the phone screen shown in the margins around the mirror.
/// Everything about it is adjustable; the mirror surface itself is never covered.
/// </summary>
public sealed record AmbientSettings
{
    public static readonly string[] Placements = ["around", "left", "right", "top", "bottom"];
    public static readonly string[] Scalings = ["cover", "fit", "stretch"];

    public bool Enabled { get; set; } = true;

    /// <summary>0.05 (barely there) to 1 (solid).</summary>
    public double Opacity { get; set; } = 0.42;

    /// <summary>Blur radius in pixels; 0 shows the capture sharp.</summary>
    public double Blur { get; set; } = 24;

    /// <summary>Which margins show it: "around" the phone, or only "left", "right", "top" or "bottom" of it.</summary>
    public string Placement { get; set; } = "around";

    /// <summary>"cover" fills the window and crops, "fit" shows the whole screen, "stretch" ignores the aspect ratio.</summary>
    public string Scaling { get; set; } = "cover";

    /// <summary>Multiplier on top of the scaling: 0.5 (half) to 3 (three times).</summary>
    public double Size { get; set; } = 1;

    /// <summary>Horizontal shift as a fraction of half the window width: -1 (far left) to 1 (far right).</summary>
    public double OffsetX { get; set; }

    /// <summary>Vertical shift as a fraction of half the window height: -1 (top) to 1 (bottom).</summary>
    public double OffsetY { get; set; }

    /// <summary>Mirror the capture left-to-right.</summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>Fade the background out towards the window edges: 0 (hard edges) to 1 (only the centre shows).</summary>
    public double EdgeFade { get; set; }

    /// <summary>Colour wash over the background: 0 (none) to 1 (solid colour).</summary>
    public double TintStrength { get; set; }

    /// <summary>Hue of the wash in degrees (0 red, 120 green, 240 blue).</summary>
    public double TintHue { get; set; } = 75;

    /// <summary>How many times per second the background follows the live video (1 to 30).</summary>
    public double FrameRate { get; set; } = 15;

    public AmbientSettings Copy() => this with { };

    public void Normalize()
    {
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.05, 1) : 0.42;
        Blur = double.IsFinite(Blur) ? Math.Clamp(Blur, 0, 80) : 24;
        Placement = Placements.Contains(Placement?.ToLowerInvariant() ?? "", StringComparer.Ordinal) ? Placement!.ToLowerInvariant() : "around";
        Scaling = Scalings.Contains(Scaling?.ToLowerInvariant() ?? "", StringComparer.Ordinal) ? Scaling!.ToLowerInvariant() : "cover";
        Size = double.IsFinite(Size) ? Math.Clamp(Size, 0.5, 3) : 1;
        OffsetX = double.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -1, 1) : 0;
        OffsetY = double.IsFinite(OffsetY) ? Math.Clamp(OffsetY, -1, 1) : 0;
        EdgeFade = double.IsFinite(EdgeFade) ? Math.Clamp(EdgeFade, 0, 1) : 0;
        TintStrength = double.IsFinite(TintStrength) ? Math.Clamp(TintStrength, 0, 1) : 0;
        TintHue = double.IsFinite(TintHue) ? Math.Clamp(TintHue, 0, 360) : 75;
        FrameRate = double.IsFinite(FrameRate) ? Math.Clamp(FrameRate, 1, 30) : 15;
    }
}

/// <summary>PC-only magnification of the mirror surface. Alt is the host modifier.</summary>
public sealed record ZoomSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Alt + mouse wheel zooms the PC frame.</summary>
    public bool WheelZoom { get; set; } = true;

    /// <summary>Alt + touchpad pinch zooms the PC frame; Alt + two-finger slide pans it.</summary>
    public bool PinchZoom { get; set; } = true;

    public double MaxZoom { get; set; } = 4.0;
    public double WheelStep { get; set; } = 0.1;

    /// <summary>Show the navigator (minimap) in the mirror corner while zoomed in.</summary>
    public bool ShowNavigator { get; set; } = true;

    public static readonly string[] NavigatorCorners = ["bottom-right", "bottom-left", "top-right", "top-left"];

    /// <summary>Which corner of the mirror holds the navigator.</summary>
    public string NavigatorCorner { get; set; } = "bottom-right";

    /// <summary>Navigator width in device-independent pixels.</summary>
    public double NavigatorWidth { get; set; } = 150;

    public ZoomSettings Copy() => this with { };

    public void Normalize()
    {
        MaxZoom = double.IsFinite(MaxZoom) ? Math.Clamp(MaxZoom, 1.5, 8.0) : 4.0;
        WheelStep = double.IsFinite(WheelStep) ? Math.Clamp(WheelStep, 0.05, 0.5) : 0.1;
        NavigatorCorner = NavigatorCorners.Contains(NavigatorCorner?.ToLowerInvariant() ?? "", StringComparer.Ordinal) ? NavigatorCorner!.ToLowerInvariant() : "bottom-right";
        NavigatorWidth = double.IsFinite(NavigatorWidth) ? Math.Clamp(NavigatorWidth, 100, 360) : 150;
    }
}

/// <summary>The pattern-lock guide drawn over a black secure lock screen.</summary>
public sealed record PatternGuideSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Ask once per new device which lock type it uses.</summary>
    public bool AskPerDevice { get; set; } = true;

    /// <summary>Show the guide automatically when Android reports the keyguard is showing.</summary>
    public bool AutoShowOnKeyguard { get; set; } = true;

    /// <summary>Read the live UI hierarchy to find the real pattern widget bounds.</summary>
    public bool AutoDiscoverGeometry { get; set; } = true;

    /// <summary>Allow keyboard calibration when the OEM hides the pattern view.</summary>
    public bool CalibrationEnabled { get; set; } = true;

    /// <summary>Draw a short-lived trail while dragging over the guide (memory only).</summary>
    public bool ShowCursorTrail { get; set; } = true;

    public double Opacity { get; set; } = 0.9;

    public PatternGuideSettings Copy() => this with { };

    public void Normalize()
    {
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1.0) : 0.9;
    }
}

/// <summary>Window and background behaviour of the desktop app.</summary>
public sealed record AppSettings
{
    /// <summary>Keep running in the tray when the window is closed so the next phone auto-opens.</summary>
    public bool RunInBackground { get; set; } = true;

    /// <summary>Bring the window up automatically when an authorised phone connects.</summary>
    public bool OpenOnConnect { get; set; } = true;

    /// <summary>Ask before writing sensitive or advanced Android settings.</summary>
    public bool ConfirmSensitiveWrites { get; set; } = true;

    public string ScreenshotDirectory { get; set; } = "captures/screenshots";

    public AppSettings Copy() => this with { };

    public void Normalize()
    {
        ScreenshotDirectory = !string.IsNullOrWhiteSpace(ScreenshotDirectory) &&
            (Path.IsPathFullyQualified(ScreenshotDirectory) || PathRules.IsSafeRelativePath(ScreenshotDirectory))
            ? ScreenshotDirectory.Trim() : "captures/screenshots";
    }
}

public sealed record LoggingSettings
{
    public bool Enabled { get; set; } = true;
    public long MaxBytes { get; set; } = 2 * 1024 * 1024;
    public int KeepFiles { get; set; } = 5;

    public LoggingSettings Copy() => this with { };

    public void Normalize()
    {
        MaxBytes = Math.Clamp(MaxBytes, 64 * 1024, 256L * 1024 * 1024);
        KeepFiles = Math.Clamp(KeepFiles, 1, 50);
    }
}

public static class PathRules
{
    /// <summary>A relative path that stays inside the package: no rooted paths, no "..", no control characters.</summary>
    public static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 260 || trimmed.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            return false;
        }

        if (Path.IsPathRooted(trimmed) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return !trimmed
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(RexConfig))]
[JsonSerializable(typeof(StateDocument))]
[JsonSerializable(typeof(UiState))]
[JsonSerializable(typeof(DeviceProfile))]
[JsonSerializable(typeof(PatternCalibration))]
internal sealed partial class RexJsonContext : JsonSerializerContext;
