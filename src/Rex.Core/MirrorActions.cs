namespace Rex.Core;

public enum ActionKind
{
    /// <summary>Executed over ADB; works whether or not the mirror is open.</summary>
    Adb,

    /// <summary>A scrcpy keyboard shortcut delivered to the embedded mirror window.</summary>
    Scrcpy,

    /// <summary>Handled by the desktop app itself (zoom, fullscreen, screenshot).</summary>
    App,
}

/// <param name="Detail">
/// What the action does, in the words a person would use. Key codes stay in <see cref="MirrorActions.AdbCommand"/>
/// and shortcuts in <see cref="Shortcuts"/>; tooltips add the shortcut from there.
/// </param>
public sealed record MirrorAction(string Id, string Label, ActionKind Kind, string Detail);

/// <summary>The one list of user-facing actions, shared by the sidebar, the tray, the CLI and machine mode.</summary>
public static class MirrorActions
{
    public static readonly IReadOnlyList<MirrorAction> All =
    [
        new("home", "Home", ActionKind.Adb, "Go to the home screen"),
        new("back", "Back", ActionKind.Adb, "Go back"),
        new("recents", "Recents", ActionKind.Adb, "Show recent apps"),
        new("power", "Power", ActionKind.Adb, "Press the power button"),
        new("wake", "Wake", ActionKind.Adb, "Turn the screen on"),
        new("sleep", "Screen off", ActionKind.Scrcpy, "Turn the phone screen off while the mirror keeps running"),
        new("volume-up", "Volume up", ActionKind.Adb, "Raise the volume"),
        new("volume-down", "Volume down", ActionKind.Adb, "Lower the volume"),
        new("mute", "Mute", ActionKind.Adb, "Mute or unmute"),
        new("notifications", "Notifications", ActionKind.Adb, "Pull down the notifications"),
        new("quick-settings", "Quick settings", ActionKind.Adb, "Pull down quick settings"),
        new("collapse", "Collapse", ActionKind.Adb, "Close the notifications and quick settings"),
        new("rotate-device", "Rotate the phone", ActionKind.Scrcpy, "Ask Android to turn the screen a quarter turn"),
        new("rotation-portrait", "Portrait", ActionKind.Adb, "Lock the phone to portrait"),
        new("rotation-landscape", "Landscape", ActionKind.Adb, "Lock the phone to landscape"),
        new("rotation-auto", "Auto rotate", ActionKind.Adb, "Let the phone rotate by itself"),
        new("rotate-left", "Turn view left", ActionKind.Scrcpy, "Turn the picture on this PC 90° counter-clockwise; the phone stays as it is"),
        new("rotate-right", "Turn view right", ActionKind.Scrcpy, "Turn the picture on this PC 90° clockwise; the phone stays as it is"),
        new("pause", "Pause", ActionKind.Scrcpy, "Freeze the mirror image"),
        new("resume", "Resume", ActionKind.Scrcpy, "Resume the mirror image"),
        new("reset-capture", "Recapture", ActionKind.Scrcpy, "Restart video capture if the image is stuck"),
        new("fps", "FPS counter", ActionKind.Scrcpy, "Toggle scrcpy's FPS counter in its console"),
        new("copy", "Copy", ActionKind.Scrcpy, "Copy from the phone to the PC clipboard"),
        new("cut", "Cut", ActionKind.Scrcpy, "Cut on the phone to the PC clipboard"),
        new("paste", "Paste", ActionKind.Scrcpy, "Paste the PC clipboard on the phone"),
        new("paste-text", "Type clipboard", ActionKind.Scrcpy, "Type the PC clipboard as keystrokes"),
        new("swipe-up", "Next item", ActionKind.App, "Swipe up: the next video or post in a feed"),
        new("swipe-down", "Previous item", ActionKind.App, "Swipe down: the previous video or post"),
        new("swipe-left", "Next page", ActionKind.App, "Swipe left: the next story, photo or page"),
        new("swipe-right", "Previous page", ActionKind.App, "Swipe right: the previous story, photo or page"),
        new("tap", "Tap", ActionKind.App, "Tap the centre of the screen: open, play or pause"),
        new("like", "Like", ActionKind.App, "Double-tap the centre of the screen"),
        new("browse", "Browse mode", ActionKind.App, "Plain keys swipe and tap the phone until Esc"),
        new("keyboard-layout", "Keyboard layout", ActionKind.Adb, "Open Android's physical keyboard settings, where the layout typing follows is chosen"),
        new("copy-add", "Add a copy", ActionKind.App, "Show another live, fully controllable copy of the phone beside it"),
        new("copy-remove", "Remove a copy", ActionKind.App, "Close the last copy of the phone"),
        new("screenshot", "Screenshot", ActionKind.App, "Save a PNG of the phone screen"),
        new("zoom-in", "Zoom in", ActionKind.App, "Magnify the PC view"),
        new("zoom-out", "Zoom out", ActionKind.App, "Shrink the PC view"),
        new("zoom-reset", "Reset zoom", ActionKind.App, "Back to 100%"),
        new("fullscreen", "Fullscreen", ActionKind.App, "Fill the display, or go back to the window"),
        new("sound-up", "Louder on this PC", ActionKind.App, "Turn the phone's sound up on this PC; the phone's own volume stays as it is"),
        new("sound-down", "Quieter on this PC", ActionKind.App, "Turn the phone's sound down on this PC; the phone's own volume stays as it is"),
        new("sound-mute", "Mute on this PC", ActionKind.App, "Mute or unmute the phone's sound on this PC; the phone itself is not muted"),
    ];

    public static MirrorAction? Find(string id) =>
        All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Ids => All.Select(x => x.Id).ToArray();

    /// <summary>The touch gestures a key or button can play on the phone.</summary>
    public static readonly IReadOnlyList<string> Gestures = ["swipe-up", "swipe-down", "swipe-left", "swipe-right", "tap", "like"];

    public static bool IsGesture(string id) => Gestures.Contains(id, StringComparer.Ordinal);

    /// <summary>ADB key code, status-bar verb, rotation mode or settings activity for <see cref="ActionKind.Adb"/> actions.</summary>
    public static (string Kind, string Argument)? AdbCommand(string id) => id switch
    {
        "home" => ("key", "KEYCODE_HOME"),
        "back" => ("key", "KEYCODE_BACK"),
        "recents" => ("key", "KEYCODE_APP_SWITCH"),
        "power" => ("key", "KEYCODE_POWER"),
        "wake" => ("key", "KEYCODE_WAKEUP"),
        "volume-up" => ("key", "KEYCODE_VOLUME_UP"),
        "volume-down" => ("key", "KEYCODE_VOLUME_DOWN"),
        "mute" => ("key", "KEYCODE_VOLUME_MUTE"),
        "notifications" => ("statusbar", "expand-notifications"),
        "quick-settings" => ("statusbar", "expand-settings"),
        "collapse" => ("statusbar", "collapse"),
        "rotation-portrait" => ("rotation", "0"),
        "rotation-landscape" => ("rotation", "1"),
        "rotation-auto" => ("rotation", "auto"),
        "keyboard-layout" => ("activity", "android.settings.HARD_KEYBOARD_SETTINGS"),
        _ => null,
    };

    /// <summary>Runs an ADB-backed action through the one command dispatcher shared by the app and both CLI modes.</summary>
    public static async Task<AndroidResult> RunAdbAsync(
        AdbClient adb,
        string serial,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var action = Find(actionId);
        var command = AdbCommand(actionId);
        if (action?.Kind != ActionKind.Adb || command is null)
        {
            return AndroidResult.Failure($"'{actionId}' is not an ADB action.");
        }

        return command.Value.Kind switch
        {
            "key" => await adb.KeyEventAsync(serial, command.Value.Argument, cancellationToken).ConfigureAwait(false),
            "rotation" => await adb.SetRotationOverrideAsync(serial, command.Value.Argument, cancellationToken).ConfigureAwait(false),
            "activity" => await adb.StartActivityAsync(serial, command.Value.Argument, cancellationToken).ConfigureAwait(false),
            // AdbCommand has four kinds; the last is the status bar.
            _ => await adb.StatusBarAsync(serial, command.Value.Argument, cancellationToken).ConfigureAwait(false),
        };
    }
}

/// <summary>scrcpy shortcut bindings (MOD is the configured shortcut modifier). Keys are virtual-key codes.</summary>
public sealed record ScrcpyShortcut(int VirtualKey, bool Shift, int Repeat = 1);

public static class ScrcpyShortcuts
{
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27;

    /// <summary>
    /// Whether a shortcut changes only how this PC shows the picture (a turn, a flip, a pause, a
    /// fresh capture) rather than the phone. Each copy of the phone is a scrcpy session of its own,
    /// so these go to every copy as well; otherwise a paused or turned main view would sit beside
    /// copies still playing upright.
    /// </summary>
    public static bool AppliesToEveryView(string actionId) =>
        DisplayOrientation.TransformFor(actionId) is not null || actionId is "pause" or "resume" or "reset-capture";

    public static ScrcpyShortcut? For(string actionId) => actionId switch
    {
        "sleep" => new('O', false),
        "rotate-device" => new('R', false),
        "rotate-left" => new(VkLeft, false),
        "rotate-right" => new(VkRight, false),
        "flip-horizontal" => new(VkLeft, true),
        "flip-vertical" => new(VkUp, true),
        "pause" => new('Z', false),
        "resume" => new('Z', true),
        "reset-capture" => new('R', true),
        "fps" => new('I', false),
        "copy" => new('C', false),
        "cut" => new('X', false),
        "paste" => new('V', false),
        "paste-text" => new('V', true),
        _ => null,
    };
}
