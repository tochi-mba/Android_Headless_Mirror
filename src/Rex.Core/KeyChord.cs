using System.Globalization;

namespace Rex.Core;

/// <summary>The modifier keys held with a key.</summary>
[Flags]
public enum KeyMods
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// One key with the exact set of modifiers held with it, written the way the app writes shortcuts
/// everywhere else: <c>Ctrl+Alt+Shift+Win+Key</c>, with keys named as <see cref="Shortcuts"/> names
/// them. The key is a Windows virtual-key code.
/// </summary>
public readonly record struct KeyChord(KeyMods Mods, int Key)
{
    public const int F1 = 0x70;
    public const int F13 = 0x7C;
    public const int F24 = 0x87;
    public const int Escape = 0x1B;
    public const int Tab = 0x09;
    public const int PrintScreen = 0x2C;
    public const int Backspace = 0x08;
    public const int Delete = 0x2E;

    /// <summary>Every key a chord may end in, by the name it is written with first.</summary>
    private static readonly (string Name, int Key)[] Names = BuildNames();

    /// <summary>Other spellings people use for the same keys.</summary>
    private static readonly Dictionary<string, int> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Del"] = Delete,
        ["Ins"] = 0x2D,
        ["PgUp"] = 0x21,
        ["PgDn"] = 0x22,
        ["Return"] = 0x0D,
        ["Escape"] = Escape,
    };

    private static readonly Dictionary<string, KeyMods> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = KeyMods.Ctrl,
        ["Control"] = KeyMods.Ctrl,
        ["Alt"] = KeyMods.Alt,
        ["Shift"] = KeyMods.Shift,
        ["Win"] = KeyMods.Win,
        ["Windows"] = KeyMods.Win,
    };

    /// <summary>Virtual-key codes of the modifier keys themselves, left, right and either.</summary>
    private static readonly HashSet<int> ModifierKeys = [0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];

    public bool Ctrl => Mods.HasFlag(KeyMods.Ctrl);
    public bool Alt => Mods.HasFlag(KeyMods.Alt);
    public bool Shift => Mods.HasFlag(KeyMods.Shift);
    public bool Win => Mods.HasFlag(KeyMods.Win);

    /// <summary>Whether a virtual-key code is a modifier key (Ctrl, Alt, Shift or Windows) itself.</summary>
    public static bool IsModifier(int key) => ModifierKeys.Contains(key);

    /// <summary>Whether a key has a name a chord can be written with.</summary>
    public static bool IsNamed(int key) => Names.Any(n => n.Key == key);

    /// <summary>True when the key is this chord's key and exactly its modifiers are held.</summary>
    public bool Matches(int key, KeyMods mods) => key == Key && mods == Mods;

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(NameOf(Key));
        return string.Join('+', parts);
    }

    /// <summary>The key's own name, or its code in hex when it has none.</summary>
    public static string NameOf(int key) =>
        Names.FirstOrDefault(n => n.Key == key).Name ?? "0x" + key.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>The chord written in <paramref name="text"/>; throws <see cref="FormatException"/> with the reason when it is not one.</summary>
    public static KeyChord Parse(string? text) =>
        TryParse(text, out var chord, out var reason) ? chord : throw new FormatException(reason);

    /// <summary>
    /// Reads a chord such as <c>Ctrl+Alt+M</c>: modifiers then exactly one key, joined by <c>+</c>,
    /// without regard to case or spaces. When it cannot, <paramref name="reason"/> says why in words.
    /// </summary>
    public static bool TryParse(string? text, out KeyChord chord, out string reason)
    {
        chord = default;
        var parts = (text ?? string.Empty).Split('+').Select(p => p.Trim()).ToArray();
        if (parts.All(p => p.Length == 0))
        {
            reason = "No key was given.";
            return false;
        }

        if (parts.Any(p => p.Length == 0))
        {
            reason = $"'{text}' has an empty part. Write the plus key as Plus, as in Ctrl+Alt+Plus.";
            return false;
        }

        var mods = KeyMods.None;
        int? key = null;
        foreach (var part in parts)
        {
            if (ModifierNames.TryGetValue(part, out var mod))
            {
                mods |= mod;
                continue;
            }

            if (key is not null)
            {
                reason = $"'{text}' has more than one key. A shortcut is modifiers and one key, such as Ctrl+Alt+M.";
                return false;
            }

            if (KeyFor(part) is not { } found)
            {
                reason = $"'{part}' is not a key this app knows.";
                return false;
            }

            key = found;
        }

        if (key is not { } chosen)
        {
            reason = "A shortcut needs a key besides Ctrl, Alt, Shift and Win.";
            return false;
        }

        chord = new KeyChord(mods, chosen);
        reason = string.Empty;
        return true;
    }

    private static int? KeyFor(string name)
    {
        if (Aliases.TryGetValue(name, out var alias))
        {
            return alias;
        }

        foreach (var (known, key) in Names)
        {
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    private static (string, int)[] BuildNames()
    {
        var names = new List<(string, int)>();
        for (var c = 'A'; c <= 'Z'; c++) names.Add((c.ToString(), c));
        for (var c = '0'; c <= '9'; c++) names.Add((c.ToString(), c));
        for (var f = 1; f <= 24; f++) names.Add(("F" + f.ToString(CultureInfo.InvariantCulture), F1 + f - 1));
        for (var n = 0; n <= 9; n++) names.Add(("Num" + n.ToString(CultureInfo.InvariantCulture), 0x60 + n));
        names.AddRange(
        [
            ("Space", 0x20), ("Enter", 0x0D), ("Backspace", Backspace), ("Delete", Delete), ("Insert", 0x2D),
            ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
            ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
            ("Plus", 0xBB), ("Minus", 0xBD), ("Comma", 0xBC), ("Period", 0xBE),
            ("NumMultiply", 0x6A), ("NumPlus", 0x6B), ("NumMinus", 0x6D), ("NumDecimal", 0x6E), ("NumDivide", 0x6F),
            ("Pause", 0x13), ("ScrollLock", 0x91), ("Esc", Escape), ("Tab", Tab), ("PrintScreen", PrintScreen),
        ]);
        return [.. names];
    }
}
