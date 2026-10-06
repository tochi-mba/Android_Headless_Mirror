namespace Rex.Core;

/// <summary>One way of reaching something without the mouse, or with a gesture rather than a click.</summary>
/// <param name="Id">The action or view this reaches, matching <see cref="MirrorActions"/> ids where one exists.</param>
/// <param name="Gesture">What the person presses or does, written the way it is shown.</param>
/// <param name="Description">What happens, in the same voice as the buttons.</param>
/// <param name="IsKey">True for a key combination, false for a pointer or touchpad gesture.</param>
/// <param name="Browse">True for a plain key that only means this while browse mode is on.</param>
/// <param name="Action">For a browse key or a key from anywhere, the <see cref="MirrorActions"/> id it plays.</param>
/// <param name="Global">True for a key that works from anywhere, while the window is hidden or behind others.</param>
public sealed record Shortcut(string Id, string Gesture, string Description, bool IsKey = true, bool Browse = false, string? Action = null, bool Global = false);

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
        new("tab-controls", "Ctrl+Alt+1", "The Controls tab"),
        new("tab-apps", "Ctrl+Alt+2", "The Apps tab"),
        new("tab-phone", "Ctrl+Alt+3", "The Phone tab"),
        new("tab-settings", "Ctrl+Alt+4", "The Settings tab"),
        new("tab-info", "Ctrl+Alt+5", "The Info tab"),
        new("home", "Ctrl+Alt+H", "Home"),
        new("back", "Ctrl+Alt+Backspace", "Back"),
        new("recents", "Ctrl+Alt+R", "Recent apps"),
        new("swipe-down", "Ctrl+Alt+Up", "Previous item in a feed (swipe down)"),
        new("swipe-up", "Ctrl+Alt+Down", "Next item in a feed (swipe up)"),
        new("tap", "Ctrl+Alt+Enter", "Tap the centre: open, play or pause"),
        new("browse", "Ctrl+Alt+K", "Browse mode on or off: plain keys drive the phone"),
        new("copy-add", "Ctrl+Alt+N", "Add a copy of the phone beside it"),
        new("copy-remove", "Ctrl+Alt+W", "Remove the last copy"),
        new("send-copied-files", "Ctrl+Alt+V", "Send files copied in File Explorer to the phone"),
        new("second-screen", "Ctrl+Alt+D", "Open or close the second screen"),
        new("phone-switch", "Ctrl+Alt+O", "Use the other phone shown beside"),
        new("screenshot", "Ctrl+Alt+S", "Save a screenshot"),
        new("zoom-in", "Ctrl+Alt+Plus", "Zoom the PC view in"),
        new("zoom-out", "Ctrl+Alt+Minus", "Zoom the PC view out"),
        new("zoom-reset", "Ctrl+Alt+0", "Fit the phone to the window"),
        new("fit-window", "Ctrl+Alt+F", "Fit the window to the phone"),
        new("rotation-portrait", "Ctrl+Alt+U", "Lock the phone to portrait"),
        new("rotation-landscape", "Ctrl+Alt+L", "Lock the phone to landscape"),
        new("rotation-auto", "Ctrl+Alt+A", "Let the phone rotate by itself"),
        new("rotate-left", "Ctrl+Alt+Left", "Turn the PC view left"),
        new("rotate-right", "Ctrl+Alt+Right", "Turn the PC view right"),
        new("pattern-guide", "Ctrl+Alt+P", "Show or hide the pattern guide"),
        new("pattern-calibrate", "Ctrl+Alt+C", "Calibrate the guide with the arrow keys"),
        new("sound-up", "Ctrl+Alt+PageUp", "The phone's sound louder on this PC"),
        new("sound-down", "Ctrl+Alt+PageDown", "The phone's sound quieter on this PC"),
        new("sound-mute", "Ctrl+Alt+Shift+M", "Mute or unmute the phone's sound on this PC"),
        .. Enumerable.Range(1, FavouriteKeys).Select(n => new Shortcut(FavouritePrefix + n, "Ctrl+Alt+Shift+" + n, $"Open favourite app {n}")),
        .. Enumerable.Range(1, ProfileKeys).Select(n => new Shortcut(ProfilePrefix + n, "Ctrl+Alt+F" + n, $"Apply profile {n}")),
        new(GlobalKeyRules.ShowHide, GlobalKeysSettings.DefaultShowHide, "Show or hide the window, from anywhere", Global: true),
        new("browse-next", "Down", "Next item in a feed (swipe up)", Browse: true, Action: "swipe-up"),
        new("browse-previous", "Up", "Previous item in a feed (swipe down)", Browse: true, Action: "swipe-down"),
        new("browse-forward", "Right", "Next story, photo or page (swipe left)", Browse: true, Action: "swipe-left"),
        new("browse-back", "Left", "Previous story, photo or page (swipe right)", Browse: true, Action: "swipe-right"),
        new("browse-tap", "Enter", "Tap the centre: open, play or pause (Space does the same)", Browse: true, Action: "tap"),
        new("browse-like", "L", "Like: double-tap the centre", Browse: true, Action: "like"),
        new("browse-mute", "M", "Mute or unmute the phone", Browse: true, Action: "mute"),
        new("browse-android-back", "Backspace", "Back", Browse: true, Action: "back"),
        new("host-zoom", "Alt + wheel", "Zoom the PC view at the pointer", IsKey: false),
        new("host-pinch", "Alt + pinch", "Zoom the PC view on a touchpad", IsKey: false),
        new("host-pan", "Alt + drag", "Pan while zoomed", IsKey: false),
        new("phone-gesture", "Two fingers", "Pinch, rotate and pan on the phone", IsKey: false),
    ];

    /// <summary>How many favourite apps have a key of their own, and the start of those keys' ids.</summary>
    public const int FavouriteKeys = 9;
    public const string FavouritePrefix = "favourite-";

    /// <summary>How many profiles have a key of their own, and the start of those keys' ids.</summary>
    public const int ProfileKeys = 9;
    public const string ProfilePrefix = "profile-";

    /// <summary>The profile (1 to 9) a shortcut id applies, or 0 when it is not one of those keys.</summary>
    public static int ProfileNumber(string? id) =>
        id is not null && id.StartsWith(ProfilePrefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(ProfilePrefix.Length), out var n) ? n : 0;

    /// <summary>The favourite (1 to 9) a shortcut id opens, or 0 when it is not one of those keys.</summary>
    public static int Favourite(string? id) =>
        id is not null && id.StartsWith(FavouritePrefix, StringComparison.Ordinal) && int.TryParse(id.AsSpan(FavouritePrefix.Length), out var n) ? n : 0;

    public static Shortcut? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// The shortcuts as this person has them: the show-or-hide key they chose (left out when it or
    /// every key from anywhere is off), and their own keys from anywhere after it.
    /// </summary>
    public static IReadOnlyList<Shortcut> Effective(RexConfig config)
    {
        var keys = config.GlobalKeys;
        var map = new KeyMap(config.Keys);
        var list = new List<Shortcut>(All.Count + keys.Actions.Count);
        foreach (var shortcut in All)
        {
            if (shortcut.Browse && shortcut.Action is { } played)
            {
                // A browse key the person moved shows where it is now; one they took away is left out.
                if (map.BrowseChordFor(played) is { } browseKey)
                {
                    list.Add(shortcut with { Gesture = browseKey.ToString() });
                }
            }
            else if (shortcut.IsKey && !shortcut.Global && KeyMap.WindowIds.Any(w => w.Id == shortcut.Id))
            {
                if (map.ChordFor(shortcut.Id) is { } windowKey)
                {
                    list.Add(shortcut with { Gesture = windowKey.ToString() });
                }
            }
            else if (shortcut.Id != GlobalKeyRules.ShowHide)
            {
                list.Add(shortcut);
            }
            else if (keys.Enabled && keys.ShowHide.Length > 0)
            {
                list.Add(shortcut with { Gesture = keys.ShowHide });
            }
        }

        // Actions that ship without a key and were given one.
        list.AddRange(map.Window
            .Where(k => k.Shipped is null && k.Now is not null)
            .Select(k => new Shortcut(k.Id, k.Now!.Value.ToString(), k.Label)));

        if (keys.Enabled)
        {
            list.AddRange(keys.Actions.Select(a => new Shortcut(
                "global-" + a.Action, a.Key, MirrorActions.Find(a.Action)!.Label + ", from anywhere", Action: a.Action, Global: true)));
        }

        return list;
    }

    /// <summary>The keys that mean something on their own while browse mode is on.</summary>
    public static IReadOnlyList<Shortcut> BrowseKeys => All.Where(s => s.Browse).ToArray();

    /// <summary>The key or gesture for a shortcut or action as this person has it, or an empty string when it has none.</summary>
    public static string Gesture(string id) =>
        KeyMap.WindowIds.Any(w => w.Id == id) ? KeyMap.Current.ChordFor(id)?.ToString() ?? string.Empty : Find(id)?.Gesture ?? string.Empty;

    /// <summary>The plain key browse mode plays an action with, as this person has it, or an empty string when it has none.</summary>
    public static string BrowseKey(string actionId) => KeyMap.Current.BrowseChordFor(actionId)?.ToString() ?? string.Empty;

    /// <summary>
    /// A tooltip that ends with every key that does the same thing: the shortcut, and the plain
    /// key browse mode uses for it. A control with neither keeps its text as it is.
    /// </summary>
    public static string Tip(string text, string id)
    {
        var keys = new List<string>(2);
        if (Gesture(id) is { Length: > 0 } gesture)
        {
            keys.Add(gesture);
        }

        if (BrowseKey(id) is { Length: > 0 } browse)
        {
            keys.Add(browse + " in browse mode");
        }

        return keys.Count == 0 ? text : text + " · " + string.Join(" · ", keys);
    }
}
