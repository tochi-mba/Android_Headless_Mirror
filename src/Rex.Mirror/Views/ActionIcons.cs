namespace Rex.Mirror.Views;

/// <summary>
/// The icon each action shows, in the Controls tiles, the fullscreen controls and their preview in
/// Settings. One map, so an action looks the same wherever it appears; actions without an icon
/// show their label instead.
///
/// No two actions share a picture: mute is not volume down, turning the view left is not turning
/// it right, and neither is the phone turning. A feed gesture shows the arrow of the key that
/// plays it (Down plays "next item", which swipes up), so the tile and the key agree.
/// </summary>
public static class ActionIcons
{
    private static readonly Dictionary<string, string> Icons = new(StringComparer.Ordinal)
    {
        ["home"] = "IconHome",
        ["back"] = "IconBack",
        ["recents"] = "IconRecents",
        ["power"] = "IconPower",
        ["wake"] = "IconSun",
        ["sleep"] = "IconMoon",
        ["volume-up"] = "IconVolumeUp",
        ["volume-down"] = "IconVolumeDown",
        ["mute"] = "IconMute",
        ["notifications"] = "IconBell",
        ["quick-settings"] = "IconSliders",
        ["collapse"] = "IconChevronUp",
        ["rotate-device"] = "IconRotatePhone",
        ["rotate-left"] = "IconRotateLeft",
        ["rotate-right"] = "IconRotate",
        ["pause"] = "IconPause",
        ["resume"] = "IconPlay",
        ["reset-capture"] = "IconRefresh",
        ["copy"] = "IconCopy",
        ["cut"] = "IconCut",
        ["paste"] = "IconClipboard",
        ["paste-text"] = "IconTypeClipboard",
        ["screenshot"] = "IconCamera",
        ["second-screen"] = "IconSecondScreen",
        ["fullscreen"] = "IconFullscreen",
        ["zoom-in"] = "IconZoomIn",
        ["zoom-out"] = "IconZoomOut",
        ["swipe-up"] = "IconArrowDown",
        ["swipe-down"] = "IconArrowUp",
        ["swipe-left"] = "IconArrowRight",
        ["swipe-right"] = "IconArrowLeft",
        ["tap"] = "IconTap",
        ["like"] = "IconHeart",
        ["browse"] = "IconKeyboard",
        ["keyboard-layout"] = "IconKeyboardLayout",
        ["sound-up"] = "IconPcSoundUp",
        ["sound-down"] = "IconPcSoundDown",
        ["sound-mute"] = "IconPcMute",
    };

    /// <summary>The theme key of the action's icon, or null when it shows its label instead.</summary>
    public static string? For(string actionId) => Icons.GetValueOrDefault(actionId);

    /// <summary>Every action that has an icon, for checking the map against the theme.</summary>
    internal static IReadOnlyDictionary<string, string> All => Icons;
}
