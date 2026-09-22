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

public sealed record MirrorAction(string Id, string Label, ActionKind Kind, string Detail);

/// <summary>The one list of user-facing actions, shared by the sidebar, the tray, the CLI and machine mode.</summary>
public static class MirrorActions
{
    public static readonly IReadOnlyList<MirrorAction> All =
    [
        new("home", "Home", ActionKind.Adb, "KEYCODE_HOME"),
        new("back", "Back", ActionKind.Adb, "KEYCODE_BACK"),
        new("recents", "Recents", ActionKind.Adb, "KEYCODE_APP_SWITCH"),
        new("power", "Power", ActionKind.Adb, "KEYCODE_POWER"),
        new("wake", "Wake", ActionKind.Adb, "KEYCODE_WAKEUP"),
        new("sleep", "Screen off", ActionKind.Scrcpy, "Turn the phone screen off while the mirror keeps running"),
        new("volume-up", "Volume up", ActionKind.Adb, "KEYCODE_VOLUME_UP"),
        new("volume-down", "Volume down", ActionKind.Adb, "KEYCODE_VOLUME_DOWN"),
        new("mute", "Mute", ActionKind.Adb, "KEYCODE_VOLUME_MUTE"),
        new("notifications", "Notifications", ActionKind.Adb, "Expand the notification shade"),
        new("quick-settings", "Quick settings", ActionKind.Adb, "Expand quick settings"),
        new("collapse", "Collapse", ActionKind.Adb, "Collapse the shade"),
        new("rotate-device", "Rotate", ActionKind.Scrcpy, "Ask Android to rotate the display"),
        new("rotation-portrait", "Portrait", ActionKind.Adb, "Lock Android to portrait · Ctrl+Alt+U"),
        new("rotation-landscape", "Landscape", ActionKind.Adb, "Lock Android to landscape · Ctrl+Alt+L"),
        new("rotation-auto", "Auto rotate", ActionKind.Adb, "Restore Android sensor rotation · Ctrl+Alt+A"),
        new("rotate-left", "Turn left", ActionKind.Scrcpy, "Rotate the mirror image 90° counter-clockwise"),
        new("rotate-right", "Turn right", ActionKind.Scrcpy, "Rotate the mirror image 90° clockwise"),
        new("pause", "Pause", ActionKind.Scrcpy, "Freeze the mirror image"),
        new("resume", "Resume", ActionKind.Scrcpy, "Resume the mirror image"),
        new("reset-capture", "Recapture", ActionKind.Scrcpy, "Restart video capture if the image is stuck"),
        new("fps", "FPS counter", ActionKind.Scrcpy, "Toggle scrcpy's FPS counter in its console"),
        new("copy", "Copy", ActionKind.Scrcpy, "Copy from the phone to the PC clipboard"),
        new("cut", "Cut", ActionKind.Scrcpy, "Cut on the phone to the PC clipboard"),
        new("paste", "Paste", ActionKind.Scrcpy, "Paste the PC clipboard on the phone"),
        new("paste-text", "Type clipboard", ActionKind.Scrcpy, "Type the PC clipboard as keystrokes"),
        new("screenshot", "Screenshot", ActionKind.App, "Save a PNG of the phone screen"),
        new("zoom-in", "Zoom in", ActionKind.App, "Magnify the PC view"),
        new("zoom-out", "Zoom out", ActionKind.App, "Shrink the PC view"),
        new("zoom-reset", "Reset zoom", ActionKind.App, "Back to 100%"),
        new("fullscreen", "Fullscreen", ActionKind.App, "Toggle fullscreen (F11)"),
    ];

    public static MirrorAction? Find(string id) =>
        All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> Ids => All.Select(x => x.Id).ToArray();

    /// <summary>ADB key code or status-bar verb for <see cref="ActionKind.Adb"/> actions.</summary>
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
        _ => null,
    };
}

/// <summary>scrcpy shortcut bindings (MOD is the configured shortcut modifier). Keys are virtual-key codes.</summary>
public sealed record ScrcpyShortcut(int VirtualKey, bool Shift, int Repeat = 1);

public static class ScrcpyShortcuts
{
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27;

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
