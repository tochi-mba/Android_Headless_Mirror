using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>
/// A profile: a named list of settings by their config.json paths. Applying one sets exactly those
/// settings and nothing else, so a profile for games never resets where the sound comes from; and a
/// profile saved by another version applies whatever this one still knows.
/// </summary>
public sealed record Profile(string Name, IReadOnlyDictionary<string, JsonNode> Settings, DateTimeOffset Saved)
{
    /// <summary>The settings a profile never carries: profiles' own, and the app's version.</summary>
    public static bool Carries(string path) =>
        !path.StartsWith("Profiles.", StringComparison.Ordinal) && path != "Version";

    /// <summary>
    /// A profile of the settings in these groups (all of them when none are named), only those that
    /// differ from how the app ships when asked.
    /// </summary>
    public static Profile From(string name, RexConfig config, IReadOnlyCollection<string>? groups, bool onlyChanged, DateTimeOffset now)
    {
        var shipped = ConfigPaths.Values(new RexConfig());
        var settings = ConfigPaths.Values(config)
            .Where(pair => Carries(pair.Key))
            .Where(pair => groups is null || groups.Count == 0 || groups.Contains(GroupOf(pair.Key), StringComparer.OrdinalIgnoreCase))
            .Where(pair => !onlyChanged || !shipped.TryGetValue(pair.Key, out var original) || !JsonNode.DeepEquals(original, pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new Profile(name, settings, now);
    }

    /// <summary>The configuration with this profile's settings, and the paths it has that this version does not know.</summary>
    public (RexConfig Config, IReadOnlyList<string> Unknown) Apply(RexConfig config) =>
        ConfigPaths.Apply(config, Settings.Where(pair => Carries(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    /// <summary>How many of its settings the configuration has another value for now.</summary>
    public int ChangedSince(RexConfig config)
    {
        var now = ConfigPaths.Values(config);
        return Settings.Count(pair => Carries(pair.Key) && now.TryGetValue(pair.Key, out var value) && !JsonNode.DeepEquals(value, pair.Value));
    }

    /// <summary>The groups it touches, in the order the settings file has them: "Mirror, Sound".</summary>
    public IReadOnlyList<string> Groups => Settings.Keys.Select(GroupOf).Distinct(StringComparer.Ordinal).ToArray();

    public static string GroupOf(string path) => path.Split('.')[0];
}

/// <summary>Profiles: when they switch by themselves, and how the app answers to them.</summary>
public sealed record ProfilesSettings
{
    public static readonly IReadOnlyList<string> AfterChoices = ["offer", "restart"];

    /// <summary>A phone's own profile is applied when it connects.</summary>
    public bool PerPhone { get; set; } = true;

    /// <summary>The profile used in fullscreen, or empty for none.</summary>
    public string WhenFullscreen { get; set; } = string.Empty;

    /// <summary>The profile used while this PC runs on battery, or empty for none.</summary>
    public string WhenOnBattery { get; set; } = string.Empty;

    /// <summary>offer a restart when a profile changes how the mirror starts, or restart at once.</summary>
    public string AfterApplying { get; set; } = "offer";

    /// <summary>Ctrl+Alt+F1 to F9 apply profiles 1 to 9.</summary>
    public bool Keys { get; set; } = true;

    /// <summary>The tray menu lists the profiles.</summary>
    public bool InTray { get; set; } = true;

    /// <summary>Say so when a profile switches by itself.</summary>
    public bool Announce { get; set; } = true;

    /// <summary>When an automatic profile ends, what it changed goes back.</summary>
    public bool PutBack { get; set; } = true;

    public ProfilesSettings Copy() => this with { };

    public void Normalize()
    {
        AfterApplying = MirrorSettings.OneOf(AfterChoices, AfterApplying, "offer");
        WhenFullscreen = ProfileNames.IsValid(WhenFullscreen) ? WhenFullscreen.Trim() : string.Empty;
        WhenOnBattery = ProfileNames.IsValid(WhenOnBattery) ? WhenOnBattery.Trim() : string.Empty;
    }
}

/// <summary>What a profile may be called: it is also its file's name.</summary>
public static class ProfileNames
{
    public const int Longest = 40;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "order",
    };

    /// <summary>1 to 40 letters, digits, spaces, dots, hyphens, underscores and brackets; not a name Windows keeps for itself.</summary>
    public static bool IsValid(string? name) => WhyNot(name) is null;

    /// <summary>Why a name cannot be used, in words, or null when it can.</summary>
    public static string? WhyNot(string? name)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return "A profile needs a name.";
        }

        if (text.Length > Longest)
        {
            return $"A profile's name is at most {Longest} characters.";
        }

        if (!text.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_' or '(' or ')') || text.StartsWith('.') || text.EndsWith('.'))
        {
            return "Use letters, digits, spaces, dots, hyphens, underscores and brackets, and no dot at either end.";
        }

        return Reserved.Contains(text) ? "Windows keeps that name for itself; choose another." : null;
    }
}
