namespace Rex.Mirror.Views;

/// <summary>
/// One stop on the tour: the element to light up, what to say about it, and which side of it the
/// callout would rather sit on.
/// </summary>
public sealed record TourStep(string Target, string Title, string Body, string Side);

/// <summary>
/// What a first-time user is shown, once.
///
/// Six stops, each pointing at something already on screen rather than describing it in the
/// abstract. The steps adapt to what is actually there: with no phone connected the first stop
/// explains the empty window instead of the mirror.
/// </summary>
public static class Tour
{
    /// <summary>Raised when the tour changes enough that people who saw the old one should see it again.</summary>
    public const int Version = 1;

    public static IReadOnlyList<TourStep> Steps(bool mirroring) =>
    [
        new("MirrorArea",
            mirroring ? "Your phone, here" : "Your phone appears here",
            mirroring
                ? "The phone's own screen stays off while this one is live. Click and type straight into it."
                : "Plug an Android phone in with USB debugging turned on and it opens here by itself, with its own screen off.",
            "right"),
        new("SidebarTabs",
            "Everything else is in here",
            "Controls for the phone, the phone's own settings, this app's settings, and what it knows about the connection.",
            "left"),
        new("QuickActions",
            "The things you reach for",
            "Home, back, recents, turning the phone's screen off and saving a screenshot, without opening a panel.",
            "bottom"),
        new("HintText",
            "Zoom without losing your place",
            "Hold Alt and use the wheel to zoom this view. A small navigator appears showing where you are; drag it to move.",
            "bottom"),
        new("FullscreenButton",
            "Fill the display",
            "F11 gives the phone the whole screen. The controls tuck away and come back when you reach for them, wherever you drag them to.",
            "bottom"),
        new("StatusText",
            "It waits for you",
            "This line says what the app is doing. Closing the window leaves it running in the tray, and plugging the phone in later opens it again on its own.",
            "top"),
    ];
}
