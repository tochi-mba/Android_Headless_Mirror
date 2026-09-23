using System.Globalization;

namespace Rex.Core;

public enum PhoneSettingKind
{
    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>One of a fixed list of values.</summary>
    Choice,

    /// <summary>A number in a range.</summary>
    Slider,

    /// <summary>Free text, validated by the setting itself.</summary>
    Text,
}

/// <summary>Where a setting actually lives. Most are keys in Android's settings provider.</summary>
public enum PhoneSettingSource
{
    SettingsProvider,
    DarkMode,
    Wifi,
    MobileData,
    AirplaneMode,
    DisplaySize,
    DisplayDensity,
    AnimationScale,
}

public sealed record PhoneChoice(string Value, string Label);

/// <summary>One phone setting: what it is called, how it is edited, and how it is read and written.</summary>
public sealed record PhoneSetting
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public required PhoneSettingKind Kind { get; init; }

    public PhoneSettingSource Source { get; init; } = PhoneSettingSource.SettingsProvider;

    /// <summary>The settings-provider namespace and key. Also read for the settings with their own writer.</summary>
    public string Namespace { get; init; } = "system";
    public string Key { get; init; } = string.Empty;

    public IReadOnlyList<PhoneChoice> Choices { get; init; } = [];
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Step { get; init; } = 1;
    public string Unit { get; init; } = string.Empty;
    public string OnValue { get; init; } = "1";
    public string OffValue { get; init; } = "0";

    /// <summary>Overrides the namespace-based risk for keys that deserve a warning of their own.</summary>
    public string? RiskOverride { get; init; }

    /// <summary>Settings with their own writer are always offered; provider keys appear only when the phone has them.</summary>
    public bool AlwaysAvailable => Source != PhoneSettingSource.SettingsProvider;

    /// <summary>Deleting the key makes Android fall back to its own default.</summary>
    public bool CanReset => Source == PhoneSettingSource.SettingsProvider;

    public string Risk => RiskOverride ?? AndroidSettings.Risk(Namespace, Key);

    /// <summary>The label for a stored value ("1" reads as "On", "300000" as "5 minutes").</summary>
    public string Describe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        return Kind switch
        {
            PhoneSettingKind.Toggle => value == OnValue ? "On" : value == OffValue ? "Off" : value,
            PhoneSettingKind.Choice => Choices.FirstOrDefault(c => c.Value == value)?.Label ?? value,
            PhoneSettingKind.Slider => Unit.Length > 0 ? value + " " + Unit : value,
            _ => value,
        };
    }
}

/// <summary>
/// The curated catalogue of Android settings the Phone tab and <c>rex phone</c> expose: a label,
/// a description and a real control for every well-known key, grouped the way a phone groups them.
/// A phone only shows the entries it actually has, so OEM differences never produce dead rows.
/// </summary>
public static class PhoneSettings
{
    public const string GroupDisplay = "Display";
    public const string GroupSound = "Sound & vibration";
    public const string GroupNotifications = "Notifications & lock screen";
    public const string GroupInput = "Input & gestures";
    public const string GroupNetwork = "Network";
    public const string GroupPower = "Power & battery";
    public const string GroupAccessibility = "Accessibility";
    public const string GroupTime = "Time & system";
    public const string GroupDeveloper = "Developer";

    /// <summary>Group order in the UI: the things people change most come first.</summary>
    public static readonly string[] Groups =
    [
        GroupDisplay, GroupSound, GroupNotifications, GroupInput,
        GroupNetwork, GroupPower, GroupAccessibility, GroupTime, GroupDeveloper,
    ];

    private static readonly PhoneChoice[] OnOff = [new("1", "On"), new("0", "Off")];

    public static readonly IReadOnlyList<PhoneSetting> All =
    [
        // ----- Display -----
        new()
        {
            Id = "brightness", Group = GroupDisplay, Label = "Brightness",
            Description = "Screen brightness while automatic brightness is off.",
            Kind = PhoneSettingKind.Slider, Namespace = "system", Key = "screen_brightness",
            Minimum = 1, Maximum = 255, Step = 1,
        },
        new()
        {
            Id = "brightness-mode", Group = GroupDisplay, Label = "Adaptive brightness",
            Description = "Let the phone set brightness from its light sensor.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "screen_brightness_mode",
        },
        new()
        {
            Id = "screen-timeout", Group = GroupDisplay, Label = "Screen timeout",
            Description = "How long the phone waits before turning its own screen off.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "screen_off_timeout",
            Choices =
            [
                new("15000", "15 seconds"), new("30000", "30 seconds"), new("60000", "1 minute"),
                new("120000", "2 minutes"), new("300000", "5 minutes"), new("600000", "10 minutes"),
                new("1800000", "30 minutes"),
            ],
        },
        new()
        {
            Id = "font-scale", Group = GroupDisplay, Label = "Text size",
            Description = "Scales every text size on the phone.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "font_scale",
            Choices = [new("0.85", "Small"), new("1.0", "Default"), new("1.15", "Large"), new("1.3", "Larger"), new("1.5", "Largest")],
        },
        new()
        {
            Id = "auto-rotate", Group = GroupDisplay, Label = "Auto-rotate screen",
            Description = "Follow the phone's orientation sensor.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "accelerometer_rotation",
        },
        new()
        {
            Id = "user-rotation", Group = GroupDisplay, Label = "Locked orientation",
            Description = "The orientation used while auto-rotate is off.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "user_rotation",
            Choices = [new("0", "Portrait"), new("1", "Landscape"), new("2", "Portrait upside down"), new("3", "Landscape (other way)")],
        },
        new()
        {
            Id = "dark-mode", Group = GroupDisplay, Label = "Dark theme",
            Description = "Switches Android's light and dark theme.",
            Kind = PhoneSettingKind.Choice, Source = PhoneSettingSource.DarkMode,
            Namespace = "secure", Key = "ui_night_mode",
            Choices = [new("yes", "On"), new("no", "Off"), new("auto", "Automatic")],
        },
        new()
        {
            Id = "display-size", Group = GroupDisplay, Label = "Display size",
            Description = "Resolution Android renders at, like 1080x2400. Use reset for the panel's own size.",
            Kind = PhoneSettingKind.Text, Source = PhoneSettingSource.DisplaySize, RiskOverride = AndroidSettings.RiskAdvanced,
        },
        new()
        {
            Id = "display-density", Group = GroupDisplay, Label = "Display density",
            Description = "Dots per inch Android draws with; higher makes everything smaller. Use reset for the default.",
            Kind = PhoneSettingKind.Text, Source = PhoneSettingSource.DisplayDensity, RiskOverride = AndroidSettings.RiskAdvanced,
        },
        new()
        {
            Id = "night-light", Group = GroupDisplay, Label = "Night light",
            Description = "Warms the screen colours.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "night_display_activated",
        },
        new()
        {
            Id = "night-light-temperature", Group = GroupDisplay, Label = "Night light warmth",
            Description = "Colour temperature in kelvin; lower is warmer.",
            Kind = PhoneSettingKind.Slider, Namespace = "secure", Key = "night_display_color_temperature",
            Minimum = 2350, Maximum = 4850, Step = 50, Unit = "K",
        },
        new()
        {
            Id = "always-on-display", Group = GroupDisplay, Label = "Always-on display",
            Description = "Keeps a dim clock on the phone's own screen.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "doze_always_on",
        },
        new()
        {
            Id = "ambient-display", Group = GroupDisplay, Label = "Wake screen for notifications",
            Description = "Lights the phone's screen when a notification arrives.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "doze_enabled",
        },
        new()
        {
            Id = "lift-to-wake", Group = GroupDisplay, Label = "Lift to wake",
            Description = "Wakes the phone when it is picked up.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "wake_gesture_enabled",
        },
        new()
        {
            Id = "double-tap-to-wake", Group = GroupDisplay, Label = "Double tap to wake",
            Description = "Wakes the phone when its screen is tapped twice.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "double_tap_to_wake",
        },
        new()
        {
            Id = "screen-saver", Group = GroupDisplay, Label = "Screen saver",
            Description = "Runs a screen saver while charging.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "screensaver_enabled",
        },

        // ----- Sound & vibration -----
        new()
        {
            Id = "touch-sounds", Group = GroupSound, Label = "Touch sounds",
            Description = "Clicks when you tap the phone's own screen.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "sound_effects_enabled",
        },
        new()
        {
            Id = "dial-pad-tones", Group = GroupSound, Label = "Dial pad tones",
            Description = "Tones when dialling.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "dtmf_tone",
        },
        new()
        {
            Id = "lock-sounds", Group = GroupSound, Label = "Screen lock sounds",
            Description = "Sound when the phone locks and unlocks.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "lockscreen_sounds_enabled",
        },
        new()
        {
            Id = "charging-sounds", Group = GroupSound, Label = "Charging sounds",
            Description = "Chime when a charger is connected.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "charging_sounds_enabled",
        },
        new()
        {
            Id = "haptic-feedback", Group = GroupSound, Label = "Touch vibration",
            Description = "Vibrates on taps and gestures.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "haptic_feedback_enabled",
        },
        new()
        {
            Id = "vibrate-when-ringing", Group = GroupSound, Label = "Vibrate when ringing",
            Description = "Vibrates as well as rings for calls.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "vibrate_when_ringing",
        },
        new()
        {
            Id = "haptic-intensity", Group = GroupSound, Label = "Touch vibration strength",
            Description = "How strong touch vibration feels.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "haptic_feedback_intensity",
            Choices = [new("0", "Off"), new("1", "Light"), new("2", "Medium"), new("3", "Strong")],
        },
        new()
        {
            Id = "ring-vibration-intensity", Group = GroupSound, Label = "Ring vibration strength",
            Description = "How strong call vibration feels.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "ring_vibration_intensity",
            Choices = [new("0", "Off"), new("1", "Light"), new("2", "Medium"), new("3", "Strong")],
        },
        new()
        {
            Id = "notification-vibration-intensity", Group = GroupSound, Label = "Notification vibration strength",
            Description = "How strong notification vibration feels.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "notification_vibration_intensity",
            Choices = [new("0", "Off"), new("1", "Light"), new("2", "Medium"), new("3", "Strong")],
        },
        new()
        {
            Id = "do-not-disturb", Group = GroupSound, Label = "Do not disturb",
            Description = "Silences the phone.",
            Kind = PhoneSettingKind.Choice, Namespace = "global", Key = "zen_mode",
            Choices = [new("0", "Off"), new("1", "Priority only"), new("2", "Total silence"), new("3", "Alarms only")],
        },

        // ----- Notifications & lock screen -----
        new()
        {
            Id = "lock-screen-notifications", Group = GroupNotifications, Label = "Notifications on the lock screen",
            Description = "Show notifications while the phone is locked.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "lock_screen_show_notifications",
        },
        new()
        {
            Id = "lock-screen-private", Group = GroupNotifications, Label = "Sensitive content when locked",
            Description = "Show notification contents on the lock screen.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "lock_screen_allow_private_notifications",
        },
        new()
        {
            Id = "heads-up", Group = GroupNotifications, Label = "Pop-up notifications",
            Description = "Let notifications appear over what is on screen.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "heads_up_notifications_enabled",
        },
        new()
        {
            Id = "notification-badges", Group = GroupNotifications, Label = "App icon badges",
            Description = "Dots on app icons for unread notifications.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "notification_badging",
        },
        new()
        {
            Id = "notification-bubbles", Group = GroupNotifications, Label = "Bubbles",
            Description = "Let conversations float in bubbles.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "notification_bubbles",
        },
        new()
        {
            Id = "lock-screen-owner-info", Group = GroupNotifications, Label = "Lock screen message",
            Description = "Text shown on the lock screen.",
            Kind = PhoneSettingKind.Text, Namespace = "secure", Key = "lock_screen_owner_info",
        },

        // ----- Input & gestures -----
        new()
        {
            Id = "show-touches", Group = GroupInput, Label = "Show taps",
            Description = "Draws a circle wherever the screen is touched. Useful while mirroring.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "show_touches",
        },
        new()
        {
            Id = "pointer-location", Group = GroupInput, Label = "Pointer location",
            Description = "Overlays touch coordinates and tracks.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "pointer_location",
        },
        new()
        {
            Id = "long-press-timeout", Group = GroupInput, Label = "Touch and hold delay",
            Description = "How long a press becomes a long press.",
            Kind = PhoneSettingKind.Choice, Namespace = "secure", Key = "long_press_timeout",
            Choices = [new("300", "Short"), new("400", "Default"), new("1000", "Medium"), new("1500", "Long")],
        },
        new()
        {
            Id = "navigation-mode", Group = GroupInput, Label = "Navigation",
            Description = "Gesture navigation or on-screen buttons.",
            Kind = PhoneSettingKind.Choice, Namespace = "secure", Key = "navigation_mode",
            RiskOverride = AndroidSettings.RiskSensitive,
            Choices = [new("0", "Three buttons"), new("1", "Two buttons"), new("2", "Gestures")],
        },
        new()
        {
            Id = "show-ime-with-hard-keyboard", Group = GroupInput, Label = "On-screen keyboard with a hardware keyboard",
            Description = "Keeps the phone's keyboard available while a physical keyboard is attached.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "show_ime_with_hard_keyboard",
        },

        // ----- Network -----
        new()
        {
            Id = "wifi", Group = GroupNetwork, Label = "Wi-Fi",
            Description = "Turns the phone's Wi-Fi radio on or off.",
            Kind = PhoneSettingKind.Toggle, Source = PhoneSettingSource.Wifi,
            Namespace = "global", Key = "wifi_on", RiskOverride = AndroidSettings.RiskSensitive,
        },
        new()
        {
            Id = "mobile-data", Group = GroupNetwork, Label = "Mobile data",
            Description = "Turns cellular data on or off.",
            Kind = PhoneSettingKind.Toggle, Source = PhoneSettingSource.MobileData,
            Namespace = "global", Key = "mobile_data", RiskOverride = AndroidSettings.RiskSensitive,
        },
        new()
        {
            Id = "airplane-mode", Group = GroupNetwork, Label = "Airplane mode",
            Description = "Turns every radio off. Wireless ADB drops with it.",
            Kind = PhoneSettingKind.Toggle, Source = PhoneSettingSource.AirplaneMode,
            Namespace = "global", Key = "airplane_mode_on", RiskOverride = AndroidSettings.RiskSensitive,
        },
        new()
        {
            Id = "wifi-scanning", Group = GroupNetwork, Label = "Wi-Fi scanning",
            Description = "Let apps scan for networks even with Wi-Fi off.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "wifi_scan_always_enabled",
        },
        new()
        {
            Id = "bluetooth-scanning", Group = GroupNetwork, Label = "Bluetooth scanning",
            Description = "Let apps scan for nearby devices even with Bluetooth off.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "ble_scan_always_enabled",
        },
        new()
        {
            Id = "private-dns", Group = GroupNetwork, Label = "Private DNS",
            Description = "Encrypted DNS mode.",
            Kind = PhoneSettingKind.Choice, Namespace = "global", Key = "private_dns_mode",
            Choices = [new("off", "Off"), new("opportunistic", "Automatic"), new("hostname", "Named provider")],
        },
        new()
        {
            Id = "data-roaming", Group = GroupNetwork, Label = "Data roaming",
            Description = "Allow mobile data on other networks.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "data_roaming",
        },

        // ----- Power & battery -----
        new()
        {
            Id = "stay-awake", Group = GroupPower, Label = "Stay awake while charging",
            Description = "Keeps the phone's screen on while it is powered.",
            Kind = PhoneSettingKind.Choice, Namespace = "global", Key = "stay_on_while_plugged_in",
            Choices = [new("0", "Off"), new("1", "Charger"), new("2", "USB"), new("4", "Wireless"), new("7", "Any power")],
        },
        new()
        {
            Id = "battery-saver", Group = GroupPower, Label = "Battery saver",
            Description = "Limits background work to save power.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "low_power",
        },
        new()
        {
            Id = "battery-saver-level", Group = GroupPower, Label = "Battery saver turns on at",
            Description = "Battery percentage that starts battery saver; 0 never does.",
            Kind = PhoneSettingKind.Slider, Namespace = "global", Key = "low_power_trigger_level",
            Minimum = 0, Maximum = 75, Step = 5, Unit = "%",
        },
        new()
        {
            Id = "adaptive-battery", Group = GroupPower, Label = "Adaptive battery",
            Description = "Let Android limit apps you rarely use.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "adaptive_battery_management_enabled",
        },
        new()
        {
            Id = "battery-percentage", Group = GroupPower, Label = "Battery percentage in the status bar",
            Description = "Shows the exact percentage next to the battery icon.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "status_bar_show_battery_percent",
        },

        // ----- Accessibility -----
        new()
        {
            Id = "color-inversion", Group = GroupAccessibility, Label = "Colour inversion",
            Description = "Inverts the colours on the phone's screen.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "accessibility_display_inversion_enabled",
        },
        new()
        {
            Id = "color-correction", Group = GroupAccessibility, Label = "Colour correction",
            Description = "Adjusts colours for colour blindness.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "accessibility_display_daltonizer_enabled",
        },
        new()
        {
            Id = "color-correction-mode", Group = GroupAccessibility, Label = "Colour correction mode",
            Description = "Which correction colour correction applies.",
            Kind = PhoneSettingKind.Choice, Namespace = "secure", Key = "accessibility_display_daltonizer",
            Choices = [new("0", "Greyscale"), new("11", "Red-green (deuteranomaly)"), new("12", "Red-green (protanomaly)"), new("13", "Blue-yellow")],
        },
        new()
        {
            Id = "high-contrast-text", Group = GroupAccessibility, Label = "High contrast text",
            Description = "Outlines text for readability.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "high_text_contrast_enabled",
        },
        new()
        {
            Id = "large-pointer", Group = GroupAccessibility, Label = "Large mouse pointer",
            Description = "Bigger pointer on the phone's screen.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "accessibility_large_pointer_icon",
        },
        new()
        {
            Id = "mono-audio", Group = GroupAccessibility, Label = "Mono audio",
            Description = "Combines both channels when playing audio.",
            Kind = PhoneSettingKind.Toggle, Namespace = "system", Key = "master_mono",
        },
        new()
        {
            Id = "captions", Group = GroupAccessibility, Label = "Live caption preference",
            Description = "Android's global captioning switch.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "accessibility_captioning_enabled",
        },
        new()
        {
            Id = "magnification", Group = GroupAccessibility, Label = "Magnification",
            Description = "Lets the phone's screen be magnified with a gesture.",
            Kind = PhoneSettingKind.Toggle, Namespace = "secure", Key = "accessibility_display_magnification_enabled",
        },

        // ----- Time & system -----
        new()
        {
            Id = "auto-time", Group = GroupTime, Label = "Automatic date and time",
            Description = "Take the time from the network.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "auto_time",
        },
        new()
        {
            Id = "auto-timezone", Group = GroupTime, Label = "Automatic time zone",
            Description = "Take the time zone from the network.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "auto_time_zone",
        },
        new()
        {
            Id = "time-format", Group = GroupTime, Label = "Time format",
            Description = "12-hour or 24-hour clock.",
            Kind = PhoneSettingKind.Choice, Namespace = "system", Key = "time_12_24",
            Choices = [new("12", "12-hour"), new("24", "24-hour")],
        },

        // ----- Developer -----
        new()
        {
            Id = "animation-scale", Group = GroupDeveloper, Label = "Animation speed",
            Description = "Scales window, transition and animator durations together. Off feels fastest over USB.",
            Kind = PhoneSettingKind.Choice, Source = PhoneSettingSource.AnimationScale,
            Namespace = "global", Key = "window_animation_scale",
            Choices = [new("0", "Off"), new("0.5", "0.5×"), new("1", "Default"), new("1.5", "1.5×"), new("2", "2×")],
        },
        new()
        {
            Id = "strict-mode", Group = GroupDeveloper, Label = "Strict mode flashing",
            Description = "Flashes the screen when an app blocks its main thread.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "strict_mode_visual",
        },
        new()
        {
            Id = "wifi-verbose-logging", Group = GroupDeveloper, Label = "Verbose Wi-Fi logging",
            Description = "Extra Wi-Fi detail in the phone's logs.",
            Kind = PhoneSettingKind.Toggle, Namespace = "global", Key = "wifi_verbose_logging_enabled",
        },
    ];

    public static readonly IReadOnlyList<string> Ids = All.Select(x => x.Id).ToArray();

    public static PhoneSetting? Find(string id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The settings-provider namespaces the catalogue reads to fill in current values.</summary>
    public static readonly string[] ReadNamespaces = ["system", "secure", "global"];

    /// <summary>Checks a value against the setting's own rules and returns the value to write.</summary>
    public static AndroidResult Validate(PhoneSetting setting, string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        switch (setting.Kind)
        {
            case PhoneSettingKind.Toggle:
                return trimmed == setting.OnValue || trimmed == setting.OffValue
                    ? AndroidResult.Success(trimmed)
                    : AndroidResult.Failure($"{setting.Label} accepts {setting.OnValue} or {setting.OffValue}.");

            case PhoneSettingKind.Choice:
                return setting.Choices.Any(c => c.Value == trimmed)
                    ? AndroidResult.Success(trimmed)
                    : AndroidResult.Failure($"{setting.Label} accepts: {string.Join(", ", setting.Choices.Select(c => c.Value))}.");

            case PhoneSettingKind.Slider:
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    number < setting.Minimum || number > setting.Maximum)
                {
                    return AndroidResult.Failure($"{setting.Label} must be a number between {setting.Minimum} and {setting.Maximum}.");
                }

                return AndroidResult.Success(setting.Step >= 1
                    ? ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture)
                    : number.ToString(CultureInfo.InvariantCulture));

            default:
                return ValidateText(setting, trimmed);
        }
    }

    private static AndroidResult ValidateText(PhoneSetting setting, string value) => setting.Source switch
    {
        PhoneSettingSource.DisplaySize when value == "reset" || System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{3,5}x\d{3,5}$") =>
            AndroidResult.Success(value),
        PhoneSettingSource.DisplaySize =>
            AndroidResult.Failure("Display size must look like 1080x2400, or reset."),
        PhoneSettingSource.DisplayDensity when value == "reset" ||
            (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpi) && dpi is >= 120 and <= 1000) =>
            AndroidResult.Success(value),
        PhoneSettingSource.DisplayDensity =>
            AndroidResult.Failure("Display density must be 120-1000, or reset."),
        _ when value.Length > 200 => AndroidResult.Failure($"{setting.Label} is limited to 200 characters."),
        _ => AndroidResult.Success(value),
    };

    /// <summary>The ADB shell command that writes a validated value.</summary>
    public static IReadOnlyList<string> WriteCommand(PhoneSetting setting, string value) => setting.Source switch
    {
        PhoneSettingSource.DarkMode => ["cmd", "uimode", "night", value],
        PhoneSettingSource.Wifi => ["svc", "wifi", value == setting.OnValue ? "enable" : "disable"],
        PhoneSettingSource.MobileData => ["svc", "data", value == setting.OnValue ? "enable" : "disable"],
        PhoneSettingSource.AirplaneMode => ["cmd", "connectivity", "airplane-mode", value == setting.OnValue ? "enable" : "disable"],
        PhoneSettingSource.DisplaySize => ["wm", "size", value],
        PhoneSettingSource.DisplayDensity => ["wm", "density", value],
        PhoneSettingSource.AnimationScale =>
        [
            "sh", "-c",
            $"settings put global window_animation_scale {ShellQuoting.Quote(value)} && " +
            $"settings put global transition_animation_scale {ShellQuoting.Quote(value)} && " +
            $"settings put global animator_duration_scale {ShellQuoting.Quote(value)}",
        ],
        _ => ["settings", "put", setting.Namespace, setting.Key, ShellQuoting.Quote(value)],
    };

    /// <summary>Extra probes for the settings that are not plain provider keys.</summary>
    public static readonly IReadOnlyList<(string Id, string[] Command)> Probes =
    [
        ("dark-mode", ["cmd", "uimode", "night"]),
        ("display-size", ["sh", "-c", "wm size | tr '\\n' ' '"]),
        ("display-density", ["sh", "-c", "wm density | tr '\\n' ' '"]),
    ];

    /// <summary>Turns a probe's output into the value the UI shows.</summary>
    public static string ParseProbe(string id, string output)
    {
        var text = output.Trim();
        return id switch
        {
            // "Night mode: yes"
            "dark-mode" => text.Contains("yes", StringComparison.OrdinalIgnoreCase) ? "yes"
                : text.Contains("auto", StringComparison.OrdinalIgnoreCase) ? "auto"
                : text.Contains("no", StringComparison.OrdinalIgnoreCase) ? "no" : string.Empty,
            // "Physical size: 1440x3200 Override size: 1080x2400"
            "display-size" or "display-density" => LastValue(text),
            _ => text,
        };
    }

    private static string LastValue(string text)
    {
        var parts = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }
}

/// <summary>One catalogue entry with the value the phone currently has.</summary>
public sealed record PhoneSettingValue(PhoneSetting Setting, string Value)
{
    /// <summary>True when the key is missing, so Android is using its own default.</summary>
    public bool IsDefault => string.IsNullOrWhiteSpace(Value);

    public string Display => Setting.Describe(Value);
}
