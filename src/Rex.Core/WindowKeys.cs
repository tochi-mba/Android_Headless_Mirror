namespace Rex.Core;

/// <summary>
/// The keys the window answers to while it is in front: the person's own over the ones the app
/// ships with (<see cref="KeyMap.Current"/>), and the numbered keys for favourite apps and profiles.
/// A key matches only with exactly its own modifiers: Ctrl+Alt+Shift+1 is not Ctrl+Alt+1, and
/// Ctrl+Alt+F1 is not F1.
/// </summary>
public static class WindowKeys
{
    private static readonly IReadOnlyList<(KeyChord Chord, string Id)> Numbered = Shortcuts.All
        .Where(s => s.IsKey && !s.Browse && !s.Global && (Shortcuts.Favourite(s.Id) > 0 || Shortcuts.ProfileNumber(s.Id) > 0))
        .Select(s => (KeyChord.Parse(s.Gesture), s.Id))
        .ToArray();

    /// <summary>Every key the window answers to now, with the id of what it does.</summary>
    public static IReadOnlyList<(KeyChord Chord, string Id)> All =>
    [
        .. KeyMap.Current.Window.Where(k => k.Now is not null).Select(k => (k.Now!.Value, k.Id)),
        .. Numbered,
    ];

    /// <summary>The <see cref="Shortcuts"/> or action id the key means in the window, or null.</summary>
    public static string? ActionFor(int key, KeyMods mods) =>
        KeyMap.Current.ActionFor(key, mods) ?? Numbered.FirstOrDefault(k => k.Chord.Matches(key, mods)).Id;
}
