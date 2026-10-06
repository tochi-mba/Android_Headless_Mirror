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
    public SoundSettings Sound { get; set; } = new();
    public SessionSettings Session { get; set; } = new();
    public WirelessSettings Wireless { get; set; } = new();
    public TouchpadSettings Touchpad { get; set; } = new();
    public InputSettings Input { get; set; } = new();
    public ZoomSettings Zoom { get; set; } = new();
    public CopiesSettings Copies { get; set; } = new();
    public AmbientSettings Ambient { get; set; } = new();
    public HudSettings Hud { get; set; } = new();
    public PatternGuideSettings PatternGuide { get; set; } = new();
    public AppSettings App { get; set; } = new();
    public GlobalKeysSettings GlobalKeys { get; set; } = new();
    public AppsSettings Apps { get; set; } = new();
    public TransferSettings Transfer { get; set; } = new();
    public SecondScreenSettings SecondScreen { get; set; } = new();
    public ViewsSettings Views { get; set; } = new();
    public ProfilesSettings Profiles { get; set; } = new();
    public SecondPhoneSettings SecondPhone { get; set; } = new();

    /// <summary>The person's own keys for the window, for browse mode, and scrcpy's own shortcut key.</summary>
    public KeysSettings Keys { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    /// <summary>Clamps every value into its supported range. Called after load and before save.</summary>
    public void Normalize()
    {
        Version = CurrentVersion;
        Mirror.Normalize();
        Sound.Normalize();
        Session.Normalize();
        Wireless.Normalize();
        Touchpad.Normalize();
        Input.Normalize();
        Zoom.Normalize();
        Copies.Normalize();
        Ambient.Normalize();
        Hud.Normalize();
        PatternGuide.Normalize();
        App.Normalize();
        GlobalKeys.Normalize();
        Apps.Normalize();
        Transfer.Normalize();
        SecondScreen.Normalize();
        Views.Normalize();
        Profiles.Normalize();
        SecondPhone.Normalize();
        Keys ??= new KeysSettings();
        Keys.Normalize();
        Logging.Normalize();
    }

    public RexConfig Copy() => this with
    {
        Mirror = Mirror.Copy(),
        Sound = Sound.Copy(),
        Session = Session.Copy(),
        Wireless = Wireless.Copy(),
        Touchpad = Touchpad.Copy(),
        Input = Input.Copy(),
        Zoom = Zoom.Copy(),
        Copies = Copies.Copy(),
        Ambient = Ambient.Copy(),
        Hud = Hud.Copy(),
        PatternGuide = PatternGuide.Copy(),
        App = App.Copy(),
        GlobalKeys = GlobalKeys.Copy(),
        Apps = Apps.Copy(),
        Transfer = Transfer.Copy(),
        SecondScreen = SecondScreen.Copy(),
        Views = Views.Copy(),
        Profiles = Profiles.Copy(),
        SecondPhone = SecondPhone.Copy(),
        Keys = Keys.Copy(),
        Logging = Logging.Copy(),
    };

    /// <summary>Copies every setting from another config without replacing this instance.</summary>
    public void CopyFrom(RexConfig other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var copy = other.Copy();
        Version = copy.Version;
        Mirror = copy.Mirror;
        Sound = copy.Sound;
        Session = copy.Session;
        Wireless = copy.Wireless;
        Touchpad = copy.Touchpad;
        Input = copy.Input;
        Zoom = copy.Zoom;
        Copies = copy.Copies;
        Ambient = copy.Ambient;
        Hud = copy.Hud;
        PatternGuide = copy.PatternGuide;
        App = copy.App;
        GlobalKeys = copy.GlobalKeys;
        Apps = copy.Apps;
        Transfer = copy.Transfer;
        SecondScreen = copy.SecondScreen;
        Views = copy.Views;
        Profiles = copy.Profiles;
        SecondPhone = copy.SecondPhone;
        Keys = copy.Keys;
        Logging = copy.Logging;
    }

    /// <summary>Returns every app setting to its shipped value while keeping this config instance.</summary>
    public void Reset() => CopyFrom(new RexConfig());
}

/// <summary>How a mirror session behaves on the phone and what happens when it ends.</summary>
public sealed record SessionSettings
{
    public const int ScreenOffTimeoutUpperBound = 86_400;
    public const int StartAppMaxLength = 200;
    public const int RestartLimitUpperBound = 20;

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

    /// <summary>
    /// How long the phone waits before turning its screen off while it is mirrored, in seconds
    /// (scrcpy --screen-off-timeout, which puts the phone's own back when the mirror ends). 0 leaves
    /// the phone's setting alone.
    /// </summary>
    public int ScreenOffTimeoutSeconds { get; set; }

    /// <summary>Keep this PC from sleeping or starting its screen saver while it shows the phone (scrcpy --disable-screensaver).</summary>
    public bool KeepPcAwake { get; set; }

    /// <summary>
    /// An app to open on the phone when the mirror starts (scrcpy --start-app): its package name,
    /// "?name" to find it by the start of its name, and a leading "+" to restart it. Empty opens nothing.
    /// </summary>
    public string StartApp { get; set; } = string.Empty;

    /// <summary>Prefer a USB device over a wireless one when both are ready.</summary>
    public bool PreferUsb { get; set; } = true;

    /// <summary>Optional serial to prefer when several devices are ready. Never a lock.</summary>
    public string PreferredSerial { get; set; } = string.Empty;

    public int PollSeconds { get; set; } = 1;
    public int RetrySeconds { get; set; } = 4;

    /// <summary>How many times in a row a mirror that closes by itself is started again before the app stops trying.</summary>
    public int RestartLimit { get; set; } = 4;

    public SessionSettings Copy() => this with { };

    /// <summary>
    /// Whether a start-app value is one scrcpy can use: a name of printable characters that is not
    /// itself an option (it is passed as one argument, never through a shell).
    /// </summary>
    public static bool IsValidStartApp(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is 0 or > StartAppMaxLength || text.StartsWith('-') || text.Any(char.IsControl))
        {
            return false;
        }

        // What remains once the prefixes are taken off must still name something.
        return text.TrimStart('+').TrimStart('?').Trim().Length > 0;
    }

    public void Normalize()
    {
        PreferredSerial = (PreferredSerial ?? string.Empty).Trim();
        ScreenOffTimeoutSeconds = ScreenOffTimeoutSeconds <= 0 ? 0 : Math.Clamp(ScreenOffTimeoutSeconds, 5, ScreenOffTimeoutUpperBound);
        StartApp = IsValidStartApp(StartApp) ? StartApp.Trim() : string.Empty;
        PollSeconds = Math.Clamp(PollSeconds, 1, 30);
        RetrySeconds = Math.Clamp(RetrySeconds, 1, 60);
        RestartLimit = Math.Clamp(RestartLimit, 1, RestartLimitUpperBound);
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

/// <summary>
/// How the mouse buttons, the keyboard, the clipboard and game controllers reach the phone. Every
/// one is a scrcpy option at launch, and applies to every copy of the phone as well, except game
/// controllers: each session would add a controller of its own to the phone.
/// </summary>
public sealed record InputSettings
{
    /// <summary>What a secondary mouse button can do, in the words config.json uses.</summary>
    public static readonly string[] ButtonActions = ["click", "nothing", "back", "home", "recents", "notifications"];

    /// <summary>What a mouse button can be set to do, in the words the Settings tab and the site show.</summary>
    public static readonly IReadOnlyList<(string Action, string Label)> ButtonChoices =
    [
        ("back", "Back"),
        ("home", "Home"),
        ("recents", "Recent apps"),
        ("notifications", "Notifications"),
        ("click", "Click on the phone"),
        ("nothing", "Nothing"),
    ];

    public static readonly string[] GamepadModes = ["disabled", "uhid"];

    /// <summary>What the right button does: back (scrcpy's own choice), home, recents, notifications, click (on the phone) or nothing.</summary>
    public string RightClick { get; set; } = "back";

    /// <summary>What the middle button (the wheel pressed) does.</summary>
    public string MiddleClick { get; set; } = "home";

    /// <summary>What the mouse's back button (the fourth) does.</summary>
    public string BackButton { get; set; } = "recents";

    /// <summary>What the mouse's forward button (the fifth) does.</summary>
    public string ForwardButton { get; set; } = "notifications";

    /// <summary>With Shift held, every button clicks on the phone instead, whatever it is set to do.</summary>
    public bool ShiftClicks { get; set; } = true;

    /// <summary>Send a held key again and again, as a keyboard does (off: --no-key-repeat, compatibility keyboard only).</summary>
    public bool KeyRepeat { get; set; } = true;

    /// <summary>Send mouse movement without a click, for apps that react to hovering (off: --no-mouse-hover).</summary>
    public bool MouseHover { get; set; } = true;

    /// <summary>Keep the PC and phone clipboards in step (off: --no-clipboard-autosync).</summary>
    public bool ClipboardAutosync { get; set; } = true;

    /// <summary>Paste by typing the text out, for phones that ignore a pasted clipboard (--legacy-paste).</summary>
    public bool LegacyPaste { get; set; }

    /// <summary>Game controllers on this PC: "disabled", or "uhid" to hand them to the phone as real controllers.</summary>
    public string Gamepad { get; set; } = "disabled";

    public const double SwipeLengthMin = 0.5;
    public const double SwipeLengthMax = 1.5;
    public const int SwipeMillisecondsMin = 80;
    public const int SwipeMillisecondsMax = 800;

    /// <summary>How far keyboard swipes travel, against the usual: 1 is 44% of the screen up and down, 56% across.</summary>
    public double SwipeLength { get; set; } = 1;

    /// <summary>How long a keyboard swipe takes, in milliseconds. Quicker ones fling further.</summary>
    public int SwipeMilliseconds { get; set; } = 200;

    /// <summary>
    /// The buttons as scrcpy's --mouse-bind: a letter per button (right, middle, back, forward),
    /// then the same four with Shift held. scrcpy's own default is "bhsn:++++".
    /// </summary>
    [JsonIgnore]
    public string MouseBind
    {
        get
        {
            var plain = string.Concat(new[] { RightClick, MiddleClick, BackButton, ForwardButton }.Select(Letter));
            return ShiftClicks ? plain + ":++++" : plain;
        }
    }

    public InputSettings Copy() => this with { };

    public void Normalize()
    {
        RightClick = MirrorSettings.OneOf(ButtonActions, RightClick, "back");
        MiddleClick = MirrorSettings.OneOf(ButtonActions, MiddleClick, "home");
        BackButton = MirrorSettings.OneOf(ButtonActions, BackButton, "recents");
        ForwardButton = MirrorSettings.OneOf(ButtonActions, ForwardButton, "notifications");
        Gamepad = MirrorSettings.OneOf(GamepadModes, Gamepad, "disabled");
        SwipeLength = double.IsFinite(SwipeLength) ? Math.Clamp(Math.Round(SwipeLength, 2), SwipeLengthMin, SwipeLengthMax) : 1;
        SwipeMilliseconds = Math.Clamp(SwipeMilliseconds, SwipeMillisecondsMin, SwipeMillisecondsMax);
    }

    private static char Letter(string action) => action switch
    {
        "click" => '+',
        "nothing" => '-',
        "home" => 'h',
        "recents" => 's',
        "notifications" => 'n',
        _ => 'b',
    };
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
        ["home", "back", "recents", "rotation-portrait", "rotation-landscape", "rotation-auto", "screenshot", "zoom-reset", "fullscreen"];

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

    /// <summary>The controls hide by themselves after <see cref="HideSeconds"/>; off, they stay on screen.</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>The same floating controls over the mirror in the window too, not only in fullscreen.</summary>
    public bool ShowInWindow { get; set; }

    public HudSettings Copy() => this with { Buttons = [.. Buttons] };

    public void Normalize()
    {
        Position = Positions.Contains(Position?.ToLowerInvariant() ?? "", StringComparer.Ordinal) ? Position!.ToLowerInvariant() : "top";
        X = X is { } x && double.IsFinite(x) ? Math.Clamp(x, 0, 1) : null;
        Y = Y is { } y && double.IsFinite(y) ? Math.Clamp(y, 0, 1) : null;
        HideSeconds = double.IsFinite(HideSeconds) ? Math.Clamp(HideSeconds, 1, 15) : 3;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.75, 1.75) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.3, 1) : 1;
        // In the catalogue's order, as the preview and the bar show them, whatever order they were picked in.
        var chosen = (Buttons ?? [])
            .Select(id => MirrorActions.Find(id)?.Id)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        Buttons = MirrorActions.Ids.Where(chosen.Contains).Take(16).ToList();
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

    /// <summary>How many times per second the background follows the live video (1 to 60).</summary>
    public double FrameRate { get; set; } = 15;

    public static readonly string[] WhenZoomedChoices = ["show", "hide"];

    /// <summary>While the picture is zoomed in: keep the soft background ("show") or let it go ("hide").</summary>
    public string WhenZoomed { get; set; } = "show";

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
        FrameRate = double.IsFinite(FrameRate) ? Math.Clamp(FrameRate, 1, 60) : 15;
        WhenZoomed = MirrorSettings.OneOf(WhenZoomedChoices, WhenZoomed, "show");
    }
}

/// <summary>
/// Copies of the phone: extra live views of the same phone beside the first, each one fully
/// controllable. Every copy is its own scrcpy session, so each costs the phone an encoder and the
/// PC a decoder; the settings bound how many and how heavy.
/// </summary>
public sealed record CopiesSettings
{
    public const int MostUpperBound = 5;
    public const double GapUpperBound = 48;
    public const int SmallestMaxSize = 480;

    /// <summary>The most copies that may be added, not counting the phone's own view (1 to 5).</summary>
    public int Most { get; set; } = 3;

    /// <summary>Space between the views, in device-independent pixels (0 to 48).</summary>
    public double Gap { get; set; } = 12;

    /// <summary>Longest side of each copy's video in pixels; 0 matches the main picture.</summary>
    public int MaxSize { get; set; }

    /// <summary>Bring the copies back the next time the phone is mirrored.</summary>
    public bool Remember { get; set; } = true;

    /// <summary>Each copy's frame-rate cap; 0 matches the mirror's. A lower one is lighter on the phone and this PC.</summary>
    public int MaxFps { get; set; }

    public CopiesSettings Copy() => this with { };

    public void Normalize()
    {
        Most = Math.Clamp(Most, 1, MostUpperBound);
        Gap = double.IsFinite(Gap) ? Math.Clamp(Gap, 0, GapUpperBound) : 12;
        MaxSize = MaxSize <= 0 ? 0 : Math.Clamp(MaxSize, SmallestMaxSize, MirrorSettings.MaxSizeUpperBound);
        MaxFps = Math.Clamp(MaxFps, 0, MirrorSettings.FpsUpperBound);
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

    /// <summary>Zoom back out to the whole phone when it turns.</summary>
    public bool ResetOnRotate { get; set; }

    /// <summary>The wheel turned away from you zooms out instead of in.</summary>
    public bool InvertWheel { get; set; }

    /// <summary>Zoom in on the pointer (true), or on the middle of what is showing (false).</summary>
    public bool ZoomAtPointer { get; set; } = true;

    /// <summary>Show the navigator (minimap) in the mirror corner while zoomed in.</summary>
    public bool ShowNavigator { get; set; } = true;

    public static readonly string[] NavigatorCorners = ["bottom-right", "bottom-left", "top-right", "top-left"];

    /// <summary>Which corner of the mirror holds the navigator.</summary>
    public string NavigatorCorner { get; set; } = "bottom-right";

    /// <summary>Navigator width in device-independent pixels.</summary>
    public double NavigatorWidth { get; set; } = 150;

    /// <summary>Show a live picture of the whole phone screen inside the navigator, not just the frame.</summary>
    public bool NavigatorPicture { get; set; } = true;

    /// <summary>How solid the navigator is over the mirror: 0.2 (glass) to 1 (solid).</summary>
    public double NavigatorOpacity { get; set; } = 0.92;

    /// <summary>How many times per second the navigator picture follows the live video (1 to 60).</summary>
    public double NavigatorFrameRate { get; set; } = 30;

    /// <summary>Keep the navigator on screen at 100% too, as a small live preview of the whole phone.</summary>
    public bool NavigatorAlways { get; set; }

    /// <summary>How far one press of a zoom key or button zooms, as a share of the picture (0.1 to 1).</summary>
    public double KeyStep { get; set; } = 0.25;

    public ZoomSettings Copy() => this with { };

    public void Normalize()
    {
        MaxZoom = double.IsFinite(MaxZoom) ? Math.Clamp(MaxZoom, 1.5, 8.0) : 4.0;
        WheelStep = double.IsFinite(WheelStep) ? Math.Clamp(WheelStep, 0.05, 0.5) : 0.1;
        NavigatorCorner = NavigatorCorners.Contains(NavigatorCorner?.ToLowerInvariant() ?? "", StringComparer.Ordinal) ? NavigatorCorner!.ToLowerInvariant() : "bottom-right";
        NavigatorWidth = double.IsFinite(NavigatorWidth) ? Math.Clamp(NavigatorWidth, 100, 360) : 150;
        NavigatorOpacity = double.IsFinite(NavigatorOpacity) ? Math.Clamp(NavigatorOpacity, 0.2, 1) : 0.92;
        NavigatorFrameRate = double.IsFinite(NavigatorFrameRate) ? Math.Clamp(NavigatorFrameRate, 1, 60) : 30;
        KeyStep = double.IsFinite(KeyStep) ? Math.Round(Math.Clamp(KeyStep, 0.1, 1), 2) : 0.25;
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

    /// <summary>How large the guide's dots are, and how near the pointer must come to one, from 0.6 to 1.6 times usual.</summary>
    public double DotSize { get; set; } = 1.0;

    public PatternGuideSettings Copy() => this with { };

    public void Normalize()
    {
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1.0) : 0.9;
        DotSize = double.IsFinite(DotSize) ? Math.Round(Math.Clamp(DotSize, 0.6, 1.6), 2) : 1.0;
    }
}

public sealed record LoggingSettings
{
    public bool Enabled { get; set; } = true;
    public long MaxBytes { get; set; } = 2 * 1024 * 1024;
    public int KeepFiles { get; set; } = 5;

    /// <summary>Every adb and scrcpy command in the log as well, for a bug report; taps and swipes are left out.</summary>
    public bool Verbose { get; set; }

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
[JsonSerializable(typeof(List<GlobalKeyAction>))]
[JsonSerializable(typeof(List<KeyBinding>))]
[JsonSerializable(typeof(StateDocument))]
[JsonSerializable(typeof(UiState))]
[JsonSerializable(typeof(DeviceProfile))]
[JsonSerializable(typeof(PatternCalibration))]
internal sealed partial class RexJsonContext : JsonSerializerContext;
