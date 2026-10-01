namespace Rex.Core;

/// <summary>
/// The keys the window answers to while it is in front, read from <see cref="Shortcuts"/> so they
/// are never written out a second time. A key matches only with exactly its own modifiers:
/// Ctrl+Alt+Shift+1 is not Ctrl+Alt+1, and Ctrl+Alt+F1 is not F1.
/// </summary>
public static class WindowKeys
{
    public static readonly IReadOnlyList<(KeyChord Chord, string Id)> All = Shortcuts.All
        .Where(s => s.IsKey && !s.Browse && !s.Global)
        .Select(s => (KeyChord.Parse(s.Gesture), s.Id))
        .ToArray();

    /// <summary>The <see cref="Shortcuts"/> id the key means in the window, or null.</summary>
    public static string? ActionFor(int key, KeyMods mods) =>
        All.FirstOrDefault(k => k.Chord.Matches(key, mods)).Id;
}
