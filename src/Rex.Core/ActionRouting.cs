namespace Rex.Core;

public enum RouteKind
{
    /// <summary>As always: to the phone over ADB, to the main session, or done by the app.</summary>
    AsUsual,

    /// <summary>To the second screen's own session as one of scrcpy's shortcuts, which act on its display.</summary>
    Screen,

    /// <summary>Not something this view can do; the words say why.</summary>
    Refuse,

    /// <summary>To the other phone over ADB, with its own serial.</summary>
    OtherPhoneAdb,

    /// <summary>To the other phone's own session as one of scrcpy's shortcuts.</summary>
    OtherPhoneSession,
}

public sealed record Route(RouteKind Kind, ScrcpyShortcut? Shortcut = null, string? Why = null);

/// <summary>
/// Where an action goes when the second screen has the keyboard. Home, Back, Recents and the
/// notification shade act on the display they are pressed on, so they go to the second screen's own
/// session, which injects them there; the same for turning or pausing that view and the clipboard.
/// Turning the phone itself makes no sense for a display of its own. Everything else is about the
/// phone (volume, power, the lock screen) and goes as usual.
/// </summary>
public static class ActionRouting
{
    public const string NoTurning = "The phone's own screen turns; a second screen does not.";

    public const string CopiesAreTheMainPhones = "Copies are of the main phone. Click it first, or make this one the main phone.";

    public const string ScreenIsTheMainPhones = "The second screen is the main phone's. Click it first, or make this one the main phone.";

    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27;

    public static Route For(string actionId, bool screenActive)
    {
        if (!screenActive)
        {
            return new Route(RouteKind.AsUsual);
        }

        ScrcpyShortcut? shortcut = actionId switch
        {
            "home" => new('H', false),
            "back" => new('B', false),
            "recents" => new('S', false),
            "notifications" => new('N', false),
            "quick-settings" => new('N', false, Repeat: 2),
            "collapse" => new('N', true),
            "rotate-left" => new(VkLeft, false),
            "rotate-right" => new(VkRight, false),
            "flip-horizontal" => new(VkLeft, true),
            "flip-vertical" => new(VkUp, true),
            "pause" or "resume" or "reset-capture" or "copy" or "cut" or "paste" or "paste-text" => ScrcpyShortcuts.For(actionId),
            _ => null,
        };

        if (shortcut is not null)
        {
            return new Route(RouteKind.Screen, shortcut);
        }

        return actionId is "rotate-device" or "rotation-portrait" or "rotation-landscape" or "rotation-auto"
            ? new Route(RouteKind.Refuse, Why: NoTurning)
            : new Route(RouteKind.AsUsual);
    }

    /// <summary>
    /// Where an action goes when the other phone's view has the keyboard: what goes over ADB goes
    /// to that phone, scrcpy's shortcuts to its own session; copies and the second screen belong
    /// to the main phone and say so; the rest (zoom, fullscreen, screenshots, gestures) the app
    /// does itself, for that phone.
    /// </summary>
    public static Route ForOtherPhone(string actionId)
    {
        if (actionId is "copy-add" or "copy-remove")
        {
            return new Route(RouteKind.Refuse, Why: CopiesAreTheMainPhones);
        }

        if (actionId == "second-screen")
        {
            return new Route(RouteKind.Refuse, Why: ScreenIsTheMainPhones);
        }

        return MirrorActions.Find(actionId)?.Kind switch
        {
            ActionKind.Adb => new Route(RouteKind.OtherPhoneAdb),
            ActionKind.Scrcpy when ScrcpyShortcuts.For(actionId) is { } shortcut => new Route(RouteKind.OtherPhoneSession, shortcut),
            _ => new Route(RouteKind.AsUsual),
        };
    }
}
