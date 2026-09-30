using Rex.Mirror.Native;

namespace Rex.Mirror.Mirror;

/// <summary>
/// What each plain key does while browse mode is on. The keys are the ones a feed is browsed
/// with anywhere else: arrows to move, Enter to open or pause, L to like, M to mute, Backspace to
/// go back. Everything not listed here still reaches the phone, so a search box can be typed into
/// without leaving the mode; the arrows, Enter, Space, L, M and Backspace cannot, which is why the
/// mode is explicit and Esc leaves it.
/// </summary>
public static class KeyboardBrowse
{
    /// <summary>The action a key asks for in browse mode, or null when the key is not one of them.</summary>
    public static string? ActionFor(int virtualKey) => virtualKey switch
    {
        NativeMethods.VK_DOWN => "swipe-up",
        NativeMethods.VK_UP => "swipe-down",
        NativeMethods.VK_RIGHT => "swipe-left",
        NativeMethods.VK_LEFT => "swipe-right",
        NativeMethods.VK_RETURN or NativeMethods.VK_SPACE => "tap",
        'L' => "like",
        'M' => "mute",
        NativeMethods.VK_BACK => "back",
        _ => null,
    };

    /// <summary>
    /// Whether a browse key should be taken now. Modifier chords are the app's own shortcuts and
    /// AltGr is text; a key pressed while a text box or button in the side panel has focus belongs
    /// to that control.
    /// </summary>
    public static bool Applies(bool browsing, bool ctrl, bool alt, bool focusInsideControl) =>
        browsing && !ctrl && !alt && !focusInsideControl;

    /// <summary>The line the status bar shows while the mode is on.</summary>
    public const string Hint = "BROWSE · ↑ ↓ feed · ← → pages · Enter tap · L like · M mute · Backspace back · Esc leaves";
}
