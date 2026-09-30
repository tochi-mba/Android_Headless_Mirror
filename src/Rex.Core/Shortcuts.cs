namespace Rex.Core;

/// <summary>One way of reaching something without the mouse, or with a gesture rather than a click.</summary>
/// <param name="Id">The action or view this reaches, matching <see cref="MirrorActions"/> ids where one exists.</param>
/// <param name="Gesture">What the person presses or does, written the way it is shown.</param>
/// <param name="Description">What happens, in the same voice as the buttons.</param>
/// <param name="IsKey">True for a key combination, false for a pointer or touchpad gesture.</param>
/// <param name="Browse">True for a plain key that only means this while browse mode is on.</param>
public sealed record Shortcut(string Id, string Gesture, string Description, bool IsKey = true, bool Browse = false);

/// <summary>
/// Every shortcut the app answers to, in one place.
///
/// This used to be written out three times over: the keys the window handles, the list the Info
/// panel shows and the table on the website. They drifted, and the Info panel ended up omitting
/// rotation and Escape while the Controls panel advertised them in its own tooltips. Anything that
/// tells a person about a shortcut reads it from here.
/// </summary>
public static class Shortcuts
{
    public static readonly IReadOnlyList<Shortcut> All =
    [
        new("fullscreen", "F11", "Fill the display"),
        new("fullscreen-exit", "Esc", "Leave fullscreen"),
        new("tour", "F1", "Show the tour again"),
        new("sidebar", "Ctrl+Alt+B", "Show or hide the side panel"),
        new("tab-controls", "Ctrl+Alt+1", "Controls"),
        new("tab-phone", "Ctrl+Alt+2", "Phone settings"),
        new("tab-settings", "Ctrl+Alt+3", "App settings"),
        new("tab-info", "Ctrl+Alt+4", "Info"),
        new("home", "Ctrl+Alt+H", "Home"),
        new("back", "Ctrl+Alt+Backspace", "Back"),
        new("recents", "Ctrl+Alt+R", "Recent apps"),
        new("swipe-down", "Ctrl+Alt+Up", "Previous item in a feed (swipe down)"),
        new("swipe-up", "Ctrl+Alt+Down", "Next item in a feed (swipe up)"),
        new("tap", "Ctrl+Alt+Enter", "Tap the centre: open, play or pause"),
        new("browse", "Ctrl+Alt+K", "Browse mode on or off: plain keys drive the phone"),
        new("screenshot", "Ctrl+Alt+S", "Save a screenshot"),
        new("zoom-in", "Ctrl+Alt+Plus", "Zoom the PC view in"),
        new("zoom-out", "Ctrl+Alt+Minus", "Zoom the PC view out"),
        new("zoom-reset", "Ctrl+Alt+0", "Fit the phone to the window"),
        new("rotation-portrait", "Ctrl+Alt+U", "Lock the phone to portrait"),
        new("rotation-landscape", "Ctrl+Alt+L", "Lock the phone to landscape"),
        new("rotation-auto", "Ctrl+Alt+A", "Let the phone rotate by itself"),
        new("rotate-left", "Ctrl+Alt+Left", "Turn the PC view left"),
        new("rotate-right", "Ctrl+Alt+Right", "Turn the PC view right"),
        new("pattern-guide", "Ctrl+Alt+P", "Show or hide the pattern guide"),
        new("pattern-calibrate", "Ctrl+Alt+C", "Calibrate the guide with the arrow keys"),
        new("browse-next", "Down", "Next item in a feed (swipe up)", Browse: true),
        new("browse-previous", "Up", "Previous item in a feed (swipe down)", Browse: true),
        new("browse-forward", "Right", "Next story, photo or page (swipe left)", Browse: true),
        new("browse-back", "Left", "Previous story, photo or page (swipe right)", Browse: true),
        new("browse-tap", "Enter", "Tap the centre: open, play or pause (Space does the same)", Browse: true),
        new("browse-like", "L", "Like: double-tap the centre", Browse: true),
        new("browse-mute", "M", "Mute or unmute the phone", Browse: true),
        new("browse-android-back", "Backspace", "Back", Browse: true),
        new("host-zoom", "Alt + wheel", "Zoom the PC view at the pointer", IsKey: false),
        new("host-pinch", "Alt + pinch", "Zoom the PC view on a touchpad", IsKey: false),
        new("host-pan", "Alt + drag", "Pan while zoomed", IsKey: false),
        new("phone-gesture", "Two fingers", "Pinch, rotate and pan on the phone", IsKey: false),
    ];

    public static Shortcut? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>The keys that mean something on their own while browse mode is on.</summary>
    public static IReadOnlyList<Shortcut> BrowseKeys => All.Where(s => s.Browse).ToArray();

    /// <summary>The gesture for an action, or an empty string when it has none.</summary>
    public static string Gesture(string id) => Find(id)?.Gesture ?? string.Empty;

    /// <summary>A tooltip that ends with the shortcut, for controls that have one.</summary>
    public static string Tip(string text, string id)
    {
        var gesture = Gesture(id);
        return gesture.Length == 0 ? text : text + " · " + gesture;
    }
}
