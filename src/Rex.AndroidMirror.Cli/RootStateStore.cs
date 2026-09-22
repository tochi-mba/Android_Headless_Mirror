using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.AndroidMirror.Cli;

public sealed record RootSessionCache(
    string Serial,
    string BootId,
    RootAccessState State,
    RootProvider Provider,
    string ProviderVersion,
    RootPrivilegeProfile? PrivilegeProfile,
    IReadOnlyList<RootCapability> Capabilities,
    DateTimeOffset VerifiedAt);

public sealed class RootStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public RootStateStore(string path) => _path = path;

    public RootSessionCache? Get(string serial, string bootId)
    {
        var all = Load();
        if (!all.TryGetValue(serial, out var value))
            return null;

        return string.Equals(value.BootId, bootId, StringComparison.Ordinal)
            ? value
            : null;
    }

    public void Set(RootSessionCache value)
    {
        var all = Load();
        all[value.Serial] = value;
        Save(all);
    }

    public void Remove(string serial)
    {
        var all = Load();
        if (all.Remove(serial))
            Save(all);
    }

    private Dictionary<string, RootSessionCache> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, RootSessionCache>(StringComparer.Ordinal);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, RootSessionCache>>(
                File.ReadAllText(_path),
                JsonOptions) ?? new Dictionary<string, RootSessionCache>();

            return new Dictionary<string, RootSessionCache>(
                parsed,
                StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "root-state.json is not valid JSON. Delete or repair it before using privileged features.",
                ex);
        }
    }

    private void Save(Dictionary<string, RootSessionCache> values)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(values, JsonOptions) + Environment.NewLine);

        _ = JsonSerializer.Deserialize<Dictionary<string, RootSessionCache>>(
            File.ReadAllText(temp),
            JsonOptions) ?? throw new InvalidOperationException(
                "Refusing to save invalid root state.");

        File.Move(temp, _path, true);
    }
}
