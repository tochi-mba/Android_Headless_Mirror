namespace Rex.Core;

/// <summary>
/// The keys the app has taken and is still holding: Windows repeats a held key's key-down until its
/// key-up, and every repeat (and the key-up) of a key the app took belongs to the app too, so a held
/// shortcut acts once and nothing of it leaks to the window in front.
/// </summary>
public sealed class KeyRepeat
{
    private readonly HashSet<int> _held = [];

    /// <summary>Whether this key-down repeats a key the app already took.</summary>
    public bool IsHeld(int key) => _held.Contains(key);

    /// <summary>The app took this key: its repeats and its key-up are swallowed.</summary>
    public void Take(int key) => _held.Add(key);

    /// <summary>The key came up. True when the app had taken it, so its key-up is swallowed too.</summary>
    public bool Release(int key) => _held.Remove(key);

    public void Clear() => _held.Clear();
}
