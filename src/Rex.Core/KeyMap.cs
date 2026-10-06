namespace Rex.Core;

/// <summary>A window key: what it does, the key it ships with (if any), and the key it has now (if any).</summary>
public sealed record WindowKey(string Id, string Label, KeyChord? Shipped, KeyChord? Now);

/// <summary>A browse-mode key: the action it plays, the key it ships with (if any), and the key it has now (if any).</summary>
public sealed record BrowseKey(string Action, string Label, KeyChord? Shipped, KeyChord? Now);

/// <summary>
/// Every key the window answers to, and every plain key browse mode plays, as this person has them:
/// the keys the app ships with (<see cref="Shortcuts"/>) with the person's own (<see cref="KeysSettings"/>)
/// over them. Any action may have a window key, not only those that ship with one. This is the
/// only place a key is resolved; the window, the tooltips, the Info tab and the HUD all ask it.
/// </summary>
public sealed class KeyMap
{
    /// <summary>The keys in use in this app. Only the app sets it, when its settings change; everything else reads it.</summary>
    public static KeyMap Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }

    private readonly Dictionary<string, KeyChord?> _window = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KeyChord?> _browse = new(StringComparer.Ordinal);

    /// <summary>The numbered keys (favourite apps, profiles) that keep their keys: they stay out of the way of every other key.</summary>
    private static readonly IReadOnlyList<(KeyChord Chord, string Label)> Fixed = Shortcuts.All
        .Where(s => s.IsKey && !s.Browse && !s.Global && (Shortcuts.Favourite(s.Id) > 0 || Shortcuts.ProfileNumber(s.Id) > 0))
        .Select(s => (KeyChord.Parse(s.Gesture), s.Description))
        .ToArray();

    /// <summary>Every window key that can be given a key of its own, in the order the Settings tab lists them.</summary>
    public static IReadOnlyList<(string Id, string Label, KeyChord? Shipped)> WindowIds { get; } = BuildWindowIds();

    /// <summary>Every action browse mode can play, with the key it ships with.</summary>
    public static IReadOnlyList<(string Action, string Label, KeyChord? Shipped)> BrowseIds { get; } = BuildBrowseIds();

    // After the lists above: statics are set in the order they are written, and this one reads them.
    private static KeyMap _current = new(new KeysSettings());

    public KeyMap(KeysSettings keys)
    {
        foreach (var (id, _, shipped) in WindowIds)
        {
            _window[id] = shipped;
        }

        foreach (var (action, _, shipped) in BrowseIds)
        {
            _browse[action] = shipped;
        }

        foreach (var binding in keys.Window)
        {
            if (WhyNotWindow(binding.Action, binding.Key) is null)
            {
                BindWindow(binding.Action, binding.Key);
            }
        }

        foreach (var binding in keys.Browse)
        {
            if (WhyNotBrowse(binding.Action, binding.Key) is null)
            {
                BindBrowse(binding.Action, binding.Key);
            }
        }
    }

    /// <summary>Every window key and what it has now.</summary>
    public IReadOnlyList<WindowKey> Window => WindowIds.Select(w => new WindowKey(w.Id, w.Label, w.Shipped, _window[w.Id])).ToArray();

    /// <summary>Every browse action and the key it has now.</summary>
    public IReadOnlyList<BrowseKey> Browse => BrowseIds.Select(b => new BrowseKey(b.Action, b.Label, b.Shipped, _browse[b.Action])).ToArray();

    /// <summary>The window key's id for a key pressed with exactly these modifiers, or null.</summary>
    public string? ActionFor(int key, KeyMods mods) =>
        _window.FirstOrDefault(pair => pair.Value is { } chord && chord.Matches(key, mods)).Key;

    /// <summary>The key a window action has now, or null when it has none.</summary>
    public KeyChord? ChordFor(string id) => _window.GetValueOrDefault(id);

    /// <summary>The action a plain key plays in browse mode, or null. Space taps too, while it means nothing else.</summary>
    public string? BrowseActionFor(int key)
    {
        if (_browse.FirstOrDefault(pair => pair.Value is { } chord && chord.Key == key).Key is { } action)
        {
            return action;
        }

        return key == Space ? "tap" : null;
    }

    /// <summary>The key a browse action has now, or null.</summary>
    public KeyChord? BrowseChordFor(string action) => _browse.GetValueOrDefault(action);

    private const int Space = 0x20;

    /// <summary>
    /// Why <paramref name="key"/> cannot be the window key for <paramref name="id"/>, or null when it
    /// can (an empty key, taking it away, always can). A key needs Ctrl, Alt or Win, or is F1 to F24
    /// alone, so typing into the phone never sets one off; Esc on its own only leaves fullscreen.
    /// </summary>
    public string? WhyNotWindow(string id, string? key)
    {
        if (!_window.ContainsKey(id))
        {
            return $"'{id}' is not something a key can do. Run 'rex keys' to see what can.";
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!KeyChord.TryParse(key, out var chord, out var reason))
        {
            return reason;
        }

        if (chord.Key is KeyChord.Tab or KeyChord.PrintScreen)
        {
            return $"{KeyChord.NameOf(chord.Key)} cannot be a shortcut.";
        }

        var plain = !chord.Ctrl && !chord.Alt && !chord.Win;
        var functionKey = chord.Key >= KeyChord.F1 && chord.Key <= KeyChord.F24;
        if (plain && !(functionKey && chord.Mods == KeyMods.None) && !(chord.Key == KeyChord.Escape && chord.Mods == KeyMods.None && id == "fullscreen-exit"))
        {
            return "A key for the window needs Ctrl, Alt or Win, or is F1 to F24 on its own, so typing into the phone never sets it off.";
        }

        if (GlobalKeyRules.WindowsOwn.Contains(chord))
        {
            return "Windows uses this key itself.";
        }

        if (Fixed.FirstOrDefault(f => f.Chord == chord) is { Label: { } fixedLabel })
        {
            return $"This key already does: {fixedLabel}.";
        }

        if (_window.FirstOrDefault(pair => pair.Key != id && pair.Value == chord).Key is { } other)
        {
            return $"This key already does: {LabelOf(other)}.";
        }

        return null;
    }

    /// <summary>Why <paramref name="key"/> cannot play <paramref name="action"/> in browse mode, or null when it can.</summary>
    public string? WhyNotBrowse(string action, string? key)
    {
        if (!_browse.ContainsKey(action))
        {
            return $"'{action}' is not something browse mode can play.";
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!KeyChord.TryParse(key, out var chord, out var reason))
        {
            return reason;
        }

        if (chord.Mods != KeyMods.None)
        {
            return "Browse mode keys are single keys, with nothing held.";
        }

        if (chord.Key is KeyChord.Escape or KeyChord.Tab or KeyChord.PrintScreen)
        {
            return chord.Key == KeyChord.Escape ? "Esc leaves browse mode." : $"{KeyChord.NameOf(chord.Key)} cannot play anything.";
        }

        return _browse.FirstOrDefault(pair => pair.Key != action && pair.Value == chord).Key is { } other
            ? $"In browse mode this key already plays: {BrowseLabelOf(other)}."
            : null;
    }

    /// <summary>Why a window key cannot be <paramref name="key"/> because a shortcut from anywhere takes it first, or null.</summary>
    public static string? WhyNotFromAnywhere(GlobalKeysSettings global, string? key)
    {
        if (!global.Enabled || !KeyChord.TryParse(key, out var chord, out _))
        {
            return null;
        }

        return global.Actions.Select(a => a.Key).Append(global.ShowHide)
            .Any(k => KeyChord.TryParse(k, out var taken, out _) && taken == chord)
            ? "A shortcut from anywhere uses this key."
            : null;
    }

    /// <summary>
    /// Writes a binding the way <see cref="KeysSettings"/> keeps them: only what differs from how
    /// the app ships, so a key put back to its own leaves the list.
    /// </summary>
    public static void Set(List<KeyBinding> bindings, string action, string key, KeyChord? shipped)
    {
        bindings.RemoveAll(b => b.Action == action);
        var same = key.Length == 0 ? shipped is null : shipped is { } s && KeyChord.TryParse(key, out var chosen, out _) && chosen == s;
        if (!same)
        {
            bindings.Add(new KeyBinding { Action = action, Key = key });
        }
    }

    /// <summary>Gives a window action the key (empty takes its key away); the caller has checked it with <see cref="WhyNotWindow"/>.</summary>
    internal void BindWindow(string id, string key) => _window[id] = string.IsNullOrWhiteSpace(key) ? null : KeyChord.Parse(key);

    internal void BindBrowse(string action, string key) => _browse[action] = string.IsNullOrWhiteSpace(key) ? null : KeyChord.Parse(key);

    private static string LabelOf(string id) => WindowIds.First(w => w.Id == id).Label;

    private static string BrowseLabelOf(string action) => BrowseIds.First(b => b.Action == action).Label;

    private static IReadOnlyList<(string, string, KeyChord?)> BuildWindowIds()
    {
        var keys = Shortcuts.All
            .Where(s => s.IsKey && !s.Browse && !s.Global && Shortcuts.Favourite(s.Id) == 0 && Shortcuts.ProfileNumber(s.Id) == 0)
            .Select(s => (s.Id, MirrorActions.Find(s.Id)?.Label ?? s.Description, (KeyChord?)KeyChord.Parse(s.Gesture)))
            .ToList();
        keys.AddRange(MirrorActions.All
            .Where(a => keys.All(k => k.Id != a.Id))
            .Select(a => (a.Id, a.Label, (KeyChord?)null)));
        return keys;
    }

    private static IReadOnlyList<(string, string, KeyChord?)> BuildBrowseIds()
    {
        var keys = Shortcuts.BrowseKeys
            .Select(s => (s.Action!, MirrorActions.Find(s.Action!)?.Label ?? s.Description, (KeyChord?)KeyChord.Parse(s.Gesture)))
            .ToList();
        keys.AddRange(MirrorActions.All
            .Where(a => keys.All(k => k.Item1 != a.Id))
            .Select(a => (a.Id, a.Label, (KeyChord?)null)));
        return keys;
    }
}
