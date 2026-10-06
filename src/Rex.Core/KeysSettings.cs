namespace Rex.Core;

/// <summary>One action and the key a person gave it; an empty key takes the action's key away.</summary>
public sealed record KeyBinding
{
    public string Action { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// The person's own keys: only what differs from how the app ships, so a key the app adds later
/// still reaches someone who never changed one. Window keys need Ctrl, Alt or Win (or are F1 to
/// F24 alone); browse keys are single keys.
/// </summary>
public sealed record KeysSettings
{
    public const int MostBindings = 100;

    public List<KeyBinding> Window { get; set; } = [];

    public List<KeyBinding> Browse { get; set; } = [];

    public KeysSettings Copy() => this with
    {
        Window = [.. Window.Select(b => b with { })],
        Browse = [.. Browse.Select(b => b with { })],
    };

    /// <summary>
    /// Keeps the bindings a key map can take, in order, each once: an action the app does not know,
    /// a key it cannot read or may not use, or one another action already has, is dropped.
    /// </summary>
    public void Normalize()
    {
        var map = new KeyMap(new KeysSettings());
        Window = Keep(Window, (b, m) => m.WhyNotWindow(b.Action, b.Key), (b, m) => m.BindWindow(b.Action, b.Key), map);
        Browse = Keep(Browse, (b, m) => m.WhyNotBrowse(b.Action, b.Key), (b, m) => m.BindBrowse(b.Action, b.Key), map);
    }

    private static List<KeyBinding> Keep(List<KeyBinding>? bindings, Func<KeyBinding, KeyMap, string?> whyNot, Action<KeyBinding, KeyMap> bind, KeyMap map)
    {
        var kept = new List<KeyBinding>();
        foreach (var binding in (bindings ?? []).OfType<KeyBinding>())
        {
            var clean = new KeyBinding { Action = (binding.Action ?? string.Empty).Trim(), Key = (binding.Key ?? string.Empty).Trim() };
            if (kept.Count == MostBindings || kept.Any(k => k.Action == clean.Action) || whyNot(clean, map) is not null)
            {
                continue;
            }

            bind(clean, map);
            // Written the way the app writes keys everywhere else, whatever spelling it was given.
            kept.Add(clean with { Key = clean.Key.Length > 0 ? KeyChord.Parse(clean.Key).ToString() : string.Empty });
        }

        return kept;
    }
}
