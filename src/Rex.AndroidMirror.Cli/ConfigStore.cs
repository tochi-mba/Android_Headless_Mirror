using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.AndroidMirror.Cli;

public sealed record ConfigLeaf(string Path, string Value, JsonValueKind Kind);

public sealed class ConfigStore
{
    private readonly string _path;

    public ConfigStore(string path) => _path = path;

    public JsonObject Load()
    {
        var json = File.ReadAllText(_path);
        return JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("config.json did not contain a JSON object.");
    }

    public IReadOnlyList<ConfigLeaf> Flatten()
    {
        var root = Load();
        var rows = new List<ConfigLeaf>();
        Walk(root, string.Empty, rows);
        return rows.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string BackupPath => _path + ".rex-backup";

    public ConfigLeaf Get(string path)
    {
        var node = Resolve(Load(), path)
            ?? throw new KeyNotFoundException($"Unknown config path '{path}'.");

        return ToLeaf(path, node);
    }

    public bool EnsureBooleanDefault(string path, bool defaultValue)
    {
        var root = Load();
        var segments = Split(path);
        if (segments.Length == 0)
            throw new ArgumentException("Config path is required.", nameof(path));

        JsonNode current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current[segments[i]]
                ?? throw new KeyNotFoundException($"Unknown config parent '{string.Join('.', segments[..(i + 1)])}'.");
        }

        var parent = current as JsonObject
            ?? throw new InvalidOperationException($"'{string.Join('.', segments[..^1])}' is not an object.");

        var key = segments[^1];
        if (parent[key] is not null)
            return false;

        parent[key] = JsonValue.Create(defaultValue);
        SaveAtomic(root);
        return true;
    }

    public void Set(string path, string rawValue)
    {
        var root = Load();
        var segments = Split(path);
        if (segments.Length == 0)
            throw new ArgumentException("Config path is required.", nameof(path));

        JsonNode current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current[segments[i]]
                ?? throw new KeyNotFoundException($"Unknown config path '{path}'.");
        }

        var parent = current as JsonObject
            ?? throw new InvalidOperationException($"'{string.Join('.', segments[..^1])}' is not an object.");

        var key = segments[^1];
        var existing = parent[key]
            ?? throw new KeyNotFoundException($"Unknown config path '{path}'.");

        parent[key] = ParseLike(existing, rawValue);
        SaveAtomic(root);
    }

    public void RestoreBackup()
    {
        if (!File.Exists(BackupPath))
            throw new InvalidOperationException("No previous REX config backup exists.");

        var candidate = File.ReadAllText(BackupPath);
        _ = JsonNode.Parse(candidate)?.AsObject()
            ?? throw new InvalidOperationException("The REX config backup is not a valid JSON object.");

        var currentTemp = _path + ".restore-current";
        File.Copy(_path, currentTemp, true);
        File.Copy(BackupPath, _path, true);
        File.Move(currentTemp, BackupPath, true);
    }

    private void SaveAtomic(JsonObject root)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var temp = _path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(options) + Environment.NewLine);

        _ = JsonNode.Parse(File.ReadAllText(temp))?.AsObject()
            ?? throw new InvalidOperationException("Refusing to save an invalid config document.");

        File.Copy(_path, BackupPath, true);
        File.Move(temp, _path, true);
    }

    private static JsonNode ParseLike(JsonNode existing, string raw)
    {
        var kind = KindOf(existing);

        return kind switch
        {
            JsonValueKind.True or JsonValueKind.False =>
                bool.TryParse(raw, out var b)
                    ? JsonValue.Create(b)!
                    : throw new FormatException("Expected true or false."),

            JsonValueKind.Number =>
                long.TryParse(raw, out var l)
                    ? JsonValue.Create(l)!
                    : double.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d)
                        ? JsonValue.Create(d)!
                        : throw new FormatException("Expected a number."),

            JsonValueKind.String => JsonValue.Create(raw)!,

            JsonValueKind.Array or JsonValueKind.Object =>
                JsonNode.Parse(raw)
                ?? throw new FormatException("Expected valid JSON."),

            _ => JsonValue.Create(raw)!
        };
    }

    private static JsonNode? Resolve(JsonObject root, string path)
    {
        JsonNode? current = root;
        foreach (var segment in Split(path))
        {
            current = current?[segment];
            if (current is null) return null;
        }
        return current;
    }

    private static string[] Split(string path) =>
        path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void Walk(JsonNode node, string prefix, List<ConfigLeaf> rows)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is null) continue;
                Walk(pair.Value, string.IsNullOrEmpty(prefix) ? pair.Key : $"{prefix}.{pair.Key}", rows);
            }
            return;
        }

        if (node is JsonArray array)
        {
            rows.Add(new ConfigLeaf(prefix, array.ToJsonString(), JsonValueKind.Array));
            return;
        }

        rows.Add(ToLeaf(prefix, node));
    }

    private static ConfigLeaf ToLeaf(string path, JsonNode node)
    {
        var kind = KindOf(node);
        var value = kind switch
        {
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => node.ToJsonString()
        };
        return new ConfigLeaf(path, value, kind);
    }

    private static JsonValueKind KindOf(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.ValueKind;
    }
}
