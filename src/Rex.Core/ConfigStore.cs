using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

public sealed record ConfigLeaf(string Path, string Value, JsonValueKind Kind);

/// <summary>
/// Path-based access to config.json ("Mirror.MaxFps") for the CLI and agents. Values keep
/// the type of the existing leaf, and every write goes through <see cref="ConfigFile"/>
/// so the typed model validates and normalizes it.
/// </summary>
public sealed class ConfigStore
{
    private readonly string _path;

    public ConfigStore(string path) => _path = path;

    public string Path => _path;
    public string BackupPath => ConfigFile.BackupPath(_path);

    public RexConfig Load() => ConfigFile.Load(_path);

    public void Save(RexConfig config) => ConfigFile.Save(_path, config);

    public IReadOnlyList<ConfigLeaf> Flatten()
    {
        var rows = new List<ConfigLeaf>();
        Walk(ToJson(Load()), string.Empty, rows);
        return rows.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ConfigLeaf Get(string path)
    {
        var node = Resolve(ToJson(Load()), path)
            ?? throw new KeyNotFoundException($"Unknown setting '{path}'. Run 'rex config list' to see every setting.");
        return ToLeaf(NormalizePath(path), node);
    }

    public ConfigLeaf Set(string path, string rawValue)
    {
        var root = ToJson(Load());
        var segments = Split(path);
        if (segments.Length == 0)
        {
            throw new ArgumentException("A setting path such as Mirror.MaxFps is required.", nameof(path));
        }

        JsonNode current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = FindChild(current, segments[i])
                ?? throw new KeyNotFoundException($"Unknown setting '{path}'.");
        }

        if (current is not JsonObject parent)
        {
            throw new InvalidOperationException($"'{string.Join('.', segments[..^1])}' is not a group of settings.");
        }

        var key = ResolveKey(parent, segments[^1])
            ?? throw new KeyNotFoundException($"Unknown setting '{path}'.");
        var existing = parent[key]!;
        if (key == "Version")
        {
            throw new InvalidOperationException("Version is managed by the app and cannot be changed.");
        }

        parent[key] = ParseLike(existing, rawValue);

        var config = root.Deserialize(RexJsonContext.Default.RexConfig)
            ?? throw new InvalidOperationException("The updated configuration could not be read back.");
        Save(config);

        return Get(path);
    }

    public bool RestoreBackup() => ConfigFile.RestoreBackup(_path);

    private static JsonObject ToJson(RexConfig config) =>
        JsonSerializer.SerializeToNode(config, RexJsonContext.Default.RexConfig)!.AsObject();

    private static JsonNode ParseLike(JsonNode existing, string raw)
    {
        var kind = KindOf(existing);
        return kind switch
        {
            JsonValueKind.True or JsonValueKind.False =>
                bool.TryParse(raw, out var b) ? JsonValue.Create(b) : throw new FormatException("Expected true or false."),
            JsonValueKind.Number =>
                long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                    ? JsonValue.Create(l)
                    : double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                        ? JsonValue.Create(d)
                        : throw new FormatException("Expected a number."),
            JsonValueKind.String => JsonValue.Create(raw),
            JsonValueKind.Array or JsonValueKind.Object =>
                JsonNode.Parse(raw) ?? throw new FormatException("Expected valid JSON."),
            _ => JsonValue.Create(raw),
        };
    }

    private static JsonNode? Resolve(JsonObject root, string path)
    {
        JsonNode? current = root;
        foreach (var segment in Split(path))
        {
            current = FindChild(current, segment);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static JsonNode? FindChild(JsonNode? node, string segment)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        var key = ResolveKey(obj, segment);
        return key is null ? null : obj[key];
    }

    private static string? ResolveKey(JsonObject obj, string segment) =>
        obj.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(segment, StringComparison.OrdinalIgnoreCase));

    private static string NormalizePath(string path) => string.Join('.', Split(path));

    private static string[] Split(string path) =>
        path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void Walk(JsonNode node, string prefix, List<ConfigLeaf> rows)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Value is not null)
                    {
                        Walk(pair.Value, string.IsNullOrEmpty(prefix) ? pair.Key : $"{prefix}.{pair.Key}", rows);
                    }
                }

                return;
            case JsonArray array:
                rows.Add(new ConfigLeaf(prefix, array.ToJsonString(), JsonValueKind.Array));
                return;
            default:
                rows.Add(ToLeaf(prefix, node));
                return;
        }
    }

    private static ConfigLeaf ToLeaf(string path, JsonNode node)
    {
        var kind = KindOf(node);
        var value = kind switch
        {
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => node.ToJsonString(),
        };
        return new ConfigLeaf(path, value, kind);
    }

    private static JsonValueKind KindOf(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b))
            {
                return b ? JsonValueKind.True : JsonValueKind.False;
            }

            if (value.TryGetValue<string>(out _))
            {
                return JsonValueKind.String;
            }

            return JsonValueKind.Number;
        }

        return node is JsonArray ? JsonValueKind.Array : JsonValueKind.Object;
    }
}
