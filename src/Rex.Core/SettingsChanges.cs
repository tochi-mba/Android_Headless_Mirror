using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>
/// What differs from how the app ships, and putting it back: for the Settings tab's list of what
/// you changed, a group's "put back" button, and "put them all back". A list is one value, so a
/// changed list is put back whole.
/// </summary>
public static class SettingsChanges
{
    /// <summary>Of these settings, the ones whose value is not the one the app ships with, in the order given.</summary>
    public static IReadOnlyList<string> Of(RexConfig config, IEnumerable<string> paths)
    {
        var now = ConfigPaths.Values(config);
        var shipped = ConfigPaths.Values(Shipped());
        return paths
            .Where(path => shipped.TryGetValue(path, out var original) && (!now.TryGetValue(path, out var value) || !JsonNode.DeepEquals(original, value)))
            .ToArray();
    }

    /// <summary>The configuration with these settings back at the values the app ships with, and nothing else changed.</summary>
    public static RexConfig PutBack(RexConfig config, IEnumerable<string> paths)
    {
        var shipped = ConfigPaths.Values(Shipped());
        var back = paths.Distinct(StringComparer.Ordinal).Where(shipped.ContainsKey).ToDictionary(path => path, path => shipped[path], StringComparer.Ordinal);
        return ConfigPaths.Apply(config, back).Config;
    }

    /// <summary>How the app ships, normalised as it always is once loaded.</summary>
    private static RexConfig Shipped()
    {
        var config = new RexConfig();
        config.Normalize();
        return config;
    }
}
