namespace Rex.Core;

/// <summary>
/// When the window says what is new: once per version, after an update. Never after a first
/// install (there is no "before"), a downgrade, or a version it cannot read, and never once the
/// person has switched it off.
/// </summary>
public static class WhatsNew
{
    /// <summary>The feature-sized updates worth teaching inside the app, in release order.</summary>
    public static IReadOnlyList<UpdateFeature> Features { get; } =
    [
        new("2.4.0", "Keys from anywhere",
            "Bring the mirror up, hide it, or run an action while another app is in front.",
            "Settings → Shortcuts from anywhere", "TabSettings"),
        new("2.5.0", "The phone's sound, on this PC",
            "The sound button has its own level, mute, meter and rules without changing the phone's volume.",
            "Sound button · Ctrl+Alt+PageUp or PageDown", "QuickSound"),
        new("2.6.0", "Every app, by name",
            "Find, open and favourite the phone's apps from the Apps tab.",
            "Apps · Ctrl+Alt+2", "TabApps"),
        new("2.7.0", "Send files straight to the phone",
            "Drop files or folders on the window, paste them, or choose Send files in Controls.",
            "Controls → Send files · Ctrl+Alt+V", "TabControls"),
        new("2.8.0", "A second screen for one app",
            "Run one app beside the phone or instead of it on a display of its own.",
            "Controls → Second screen · Ctrl+Alt+D", "TabControls"),
        new("2.9.0", "Profiles for the way you use the mirror",
            "Save groups of settings, apply a preset, or switch profiles for fullscreen, battery power or one phone.",
            "Settings → Profiles · Ctrl+Alt+F1 to F9", "TabSettings"),
        new("2.10.0", "Two phones side by side",
            "Show a second, different phone beside the first, each fully usable; the side panel follows the one you click.",
            "The phone menu · Ctrl+Alt+O switches", "DeviceChip"),
        new("2.11.0", "Every setting in Settings, and a way back",
            "Every value the app keeps now has a control. Changed lists what you changed, and each one, or a whole group, can go back to how the app ships.",
            "Settings → Changed", "TabSettings"),
    ];

    public static bool ShouldOffer(string? lastRunVersion, string currentVersion, bool enabled) =>
        enabled &&
        Version.TryParse(lastRunVersion, out var last) &&
        Version.TryParse(currentVersion, out var current) &&
        current > last;

    /// <summary>
    /// Feature-sized changes crossed by an update. A first install, downgrade, unreadable version
    /// or patch with no new workflow returns none, so it never gets a made-up onboarding screen.
    /// </summary>
    public static IReadOnlyList<UpdateFeature> FeaturesBetween(string? lastRunVersion, string currentVersion)
    {
        if (!Version.TryParse(lastRunVersion, out var last) ||
            !Version.TryParse(currentVersion, out var current) ||
            current <= last)
        {
            return [];
        }

        return Features.Where(feature =>
            Version.TryParse(feature.Version, out var introduced) && introduced > last && introduced <= current).ToArray();
    }
}

/// <summary>
/// One concise card in the update-only onboarding, and the part of the window its tour stop
/// lights up (<see cref="Target"/> is an element's name in the main window).
/// </summary>
public sealed record UpdateFeature(string Version, string Title, string Body, string Where, string Target);
