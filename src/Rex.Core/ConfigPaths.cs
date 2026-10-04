using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>
/// Settings by their config.json paths ("Mirror.MaxFps"), on a configuration in memory: every value
/// with its path, setting several at once, and which paths two configurations disagree on. A list
/// is one value (a whole list is set, never one item of it). Profiles are made of these.
/// </summary>
public static class ConfigPaths
{
    /// <summary>Every setting's path and value, lists as one value each; Version is the app's, not a setting.</summary>
    public static IReadOnlyDictionary<string, JsonNode> Values(RexConfig config)
    {
        var values = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        Walk(ToJson(config), string.Empty, values);
        values.Remove("Version");
        return values;
    }

    /// <summary>
    /// A copy of the configuration with these values set, normalised as the app normalises it, and
    /// the paths it does not know (a profile saved by another version) or could not take. One bad
    /// value is left out on its own; the rest still apply.
    /// </summary>
    public static (RexConfig Config, IReadOnlyList<string> Unknown) Apply(RexConfig config, IReadOnlyDictionary<string, JsonNode> values)
    {
        var root = ToJson(config);
        var unknown = new List<string>();
        foreach (var (path, value) in values)
        {
            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 2 || !TrySet(root, segments, value))
            {
                unknown.Add(path);
            }
        }

        RexConfig result;
        try
        {
            result = root.Deserialize(RexJsonContext.Default.RexConfig) ?? config.Copy();
        }
        catch (JsonException)
        {
            // A value of the wrong kind somewhere: apply them one at a time so the good ones still count.
            return ApplyOneByOne(config, values);
        }

        result.Normalize();
        return (result, unknown);
    }

    private static (RexConfig Config, IReadOnlyList<string> Unknown) ApplyOneByOne(RexConfig config, IReadOnlyDictionary<string, JsonNode> values)
    {
        var current = config.Copy();
        var unknown = new List<string>();
        foreach (var pair in values)
        {
            var root = ToJson(current);
            var segments = pair.Key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            try
            {
                if (segments.Length < 2 || !TrySet(root, segments, pair.Value))
                {
                    unknown.Add(pair.Key);
                    continue;
                }

                current = root.Deserialize(RexJsonContext.Default.RexConfig) ?? current;
            }
            catch (JsonException)
            {
                unknown.Add(pair.Key);
            }
        }

        current.Normalize();
        return (current, unknown);
    }

    /// <summary>The paths whose values differ between two configurations.</summary>
    public static IReadOnlyList<string> Differences(RexConfig a, RexConfig b)
    {
        var left = Values(a);
        var right = Values(b);
        return left.Keys.Where(path => !right.TryGetValue(path, out var other) || !JsonNode.DeepEquals(left[path], other)).ToArray();
    }

    /// <summary>The value at a path, or null when there is none.</summary>
    public static JsonNode? Get(RexConfig config, string path) => Values(config).TryGetValue(path, out var value) ? value : null;

    private static bool TrySet(JsonObject root, string[] segments, JsonNode value)
    {
        JsonNode? current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current is JsonObject parent && Key(parent, segments[i]) is { } key ? parent[key] : null;
        }

        if (current is not JsonObject leafParent || Key(leafParent, segments[^1]) is not { } leaf || leafParent[leaf] is JsonObject)
        {
            return false;
        }

        if (!SameKind(leafParent[leaf], value))
        {
            return false;
        }

        leafParent[leaf] = value.DeepClone();
        return true;
    }

    private static string? Key(JsonObject obj, string segment) =>
        obj.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(segment, StringComparison.OrdinalIgnoreCase));

    /// <summary>A value may only replace one of its own kind: a number a number, a list a list.</summary>
    private static bool SameKind(JsonNode? existing, JsonNode value) =>
        existing?.GetValueKind() is { } kind && (kind == value.GetValueKind() ||
            kind is JsonValueKind.True or JsonValueKind.False && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False);

    private static JsonObject ToJson(RexConfig config) =>
        JsonSerializer.SerializeToNode(config, RexJsonContext.Default.RexConfig)!.AsObject();

    private static void Walk(JsonNode node, string prefix, IDictionary<string, JsonNode> values)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, child) in obj)
            {
                if (child is not null)
                {
                    Walk(child, prefix.Length == 0 ? key : prefix + "." + key, values);
                }
            }

            return;
        }

        values[prefix] = node.DeepClone();
    }
}
