using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>What decides which profile is in effect: the one applied by hand, the phone's own, battery, fullscreen.</summary>
public sealed record ProfileInputs(string? Manual, string? PhoneProfile, bool OnBattery, bool Fullscreen);

/// <summary>What an automatic profile changed: each path's value before it, and what it set.</summary>
public sealed record PutBack(string Profile, IReadOnlyDictionary<string, JsonNode> Before, IReadOnlyDictionary<string, JsonNode> Set);

/// <summary>
/// Which profile applies by itself, and what goes back when it stops. Fullscreen comes first, then
/// battery, then the phone's own; a profile applied by hand is the base the others sit on. An
/// automatic profile remembers each value it changed, and when it ends puts back only those still
/// as it left them: anything changed meanwhile is the person's and stays.
/// </summary>
public static class ProfilePolicy
{
    /// <summary>The automatic profile for now, or null for none (the hand-applied one then stands).</summary>
    public static string? Automatic(ProfileInputs now, ProfilesSettings settings) =>
        now.Fullscreen && settings.WhenFullscreen.Length > 0 ? settings.WhenFullscreen
        : now.OnBattery && settings.WhenOnBattery.Length > 0 ? settings.WhenOnBattery
        : settings.PerPhone && !string.IsNullOrWhiteSpace(now.PhoneProfile) ? now.PhoneProfile
        : null;

    /// <summary>Applies an automatic profile and remembers what it changed, to put it back later.</summary>
    public static (RexConfig Config, PutBack Record, IReadOnlyList<string> Unknown) Start(RexConfig config, Profile profile)
    {
        var before = ConfigPaths.Values(config);
        var (applied, unknown) = profile.Apply(config);
        var after = ConfigPaths.Values(applied);
        var changed = after.Keys.Where(path => !before.TryGetValue(path, out var was) || !JsonNode.DeepEquals(was, after[path])).ToArray();
        var record = new PutBack(
            profile.Name,
            changed.Where(before.ContainsKey).ToDictionary(path => path, path => before[path], StringComparer.Ordinal),
            changed.ToDictionary(path => path, path => after[path], StringComparer.Ordinal));
        return (applied, record, unknown);
    }

    /// <summary>
    /// The configuration once an automatic profile ends: each value it set goes back to what it was,
    /// unless it has been changed since, which leaves it as it is.
    /// </summary>
    public static RexConfig End(RexConfig config, PutBack record)
    {
        var now = ConfigPaths.Values(config);
        var back = record.Before
            .Where(pair => record.Set.TryGetValue(pair.Key, out var set) && now.TryGetValue(pair.Key, out var current) && JsonNode.DeepEquals(set, current))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return ConfigPaths.Apply(config, back).Config;
    }
}
