namespace Rex.Core;

/// <summary>
/// Which keys may work from anywhere. Such a key is taken from every other app while this one runs,
/// so it has to be one that costs them nothing: it needs Ctrl (releasing a lone Alt or Win after a
/// swallowed key opens a menu bar or the Start menu in the app in front, and a Ctrl press counts as
/// another key), or is F13 to F24 on its own; it is never one of Windows' own keys; and it does not
/// already mean something else in this window or to another key from anywhere.
/// </summary>
public static class GlobalKeyRules
{
    /// <summary>The id the show-or-hide key is checked under.</summary>
    public const string ShowHide = "show-hide";

    /// <summary>Keys Windows itself answers to with Ctrl held.</summary>
    public static readonly IReadOnlyList<KeyChord> WindowsOwn =
    [
        .. new[]
        {
            "Ctrl+Alt+Delete", "Ctrl+Shift+Esc", "Ctrl+Esc", "Ctrl+Win+D", "Ctrl+Win+F4", "Ctrl+Win+Left",
            "Ctrl+Win+Right", "Ctrl+Win+Enter", "Ctrl+Win+O", "Ctrl+Win+C", "Ctrl+Win+N", "Ctrl+Win+S",
            "Ctrl+Win+V", "Ctrl+Win+Shift+B",
        }.Select(KeyChord.Parse),
    ];

    /// <summary>
    /// Why <paramref name="chord"/> cannot be the key from anywhere for <paramref name="forAction"/>
    /// (an action id, or <see cref="ShowHide"/>), or null when it can.
    /// </summary>
    public static string? WhyNot(KeyChord chord, string forAction, IEnumerable<KeyChord> otherGlobals)
    {
        if (chord.Key is KeyChord.Escape or KeyChord.Tab or KeyChord.PrintScreen)
        {
            return $"{KeyChord.NameOf(chord.Key)} cannot be part of a shortcut from anywhere.";
        }

        var functionKeyAlone = chord.Mods == KeyMods.None && chord.Key is >= KeyChord.F13 and <= KeyChord.F24;
        if (!chord.Ctrl && !functionKeyAlone)
        {
            return "A shortcut from anywhere needs Ctrl, so letting go of it never opens a menu in the app in front. F13 to F24 also work on their own.";
        }

        if (WindowsOwn.Contains(chord))
        {
            return "Windows uses this key itself.";
        }

        if (WindowKeys.All.FirstOrDefault(k => k.Chord == chord) is { Id: { } windowId } && windowId != forAction)
        {
            return $"In this window the key already means: {Shortcuts.Find(windowId)!.Description}.";
        }

        return otherGlobals.Contains(chord) ? "Another shortcut from anywhere uses this key." : null;
    }

    /// <summary>The same check for a chord still in words; the reason it cannot be read comes first.</summary>
    public static string? WhyNot(string? text, string forAction, IEnumerable<KeyChord> otherGlobals) =>
        KeyChord.TryParse(text, out var chord, out var reason) ? WhyNot(chord, forAction, otherGlobals) : reason;
}
