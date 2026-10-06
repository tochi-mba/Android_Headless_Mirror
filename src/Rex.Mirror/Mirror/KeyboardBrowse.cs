using Rex.Core;
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
    /// <summary>The action a key asks for in browse mode (the person's own keys, <see cref="KeyMap.Current"/>), or null.</summary>
    public static string? ActionFor(int virtualKey) => KeyMap.Current.BrowseActionFor(virtualKey);

    /// <summary>
    /// Whether a browse key should be taken now. Modifier chords are the app's own shortcuts and
    /// AltGr is text; a key pressed while a text box or button in the side panel has focus belongs
    /// to that control.
    /// </summary>
    public static bool Applies(bool browsing, bool ctrl, bool alt, bool focusInsideControl) =>
        browsing && !ctrl && !alt && !focusInsideControl;

    /// <summary>The line the status bar shows while the mode is on, with the keys as the person has them.</summary>
    public static string Hint
    {
        get
        {
            static string Key(string action) => KeyMap.Current.BrowseChordFor(action) is { } chord ? Arrow(chord.ToString()) : "-";
            static string Arrow(string key) => key switch { "Up" => "↑", "Down" => "↓", "Left" => "←", "Right" => "→", _ => key };
            return $"BROWSE · {Key("swipe-down")} {Key("swipe-up")} feed · {Key("swipe-right")} {Key("swipe-left")} pages · {Key("tap")} tap · " +
                   $"{Key("like")} like · {Key("mute")} mute · {Key("back")} back · Esc leaves";
        }
    }
}
