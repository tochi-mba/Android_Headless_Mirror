using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rex.AndroidMirror.Cli;

public sealed class DisplayVerificationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public DisplayVerificationStore(string path) => _path = path;

    public DisplayVerification Get(string transport)
    {
        var all = Load();
        return all.TryGetValue(transport, out var value)
            ? value
            : new DisplayVerification(
                VerificationOutcome.Unknown,
                VerificationOutcome.Unknown,
                null,
                string.Empty);
    }

    public DisplayVerification Set(
        string transport,
        string target,
        VerificationOutcome outcome,
        string note = "")
    {
        if (string.IsNullOrWhiteSpace(transport))
            throw new ArgumentException("Transport is required.", nameof(transport));

        var current = Get(transport);
        var updated = target.ToLowerInvariant() switch
        {
            "normal" => current with
            {
                NormalPlayback = outcome,
                UpdatedAt = DateTimeOffset.UtcNow,
                Note = note
            },
            "protected" => current with
            {
                ProtectedPlayback = outcome,
                UpdatedAt = DateTimeOffset.UtcNow,
                Note = note
            },
            _ => throw new ArgumentException("Verification target must be normal or protected.", nameof(target))
        };

        var all = Load();
        all[transport] = updated;
        Save(all);
        return updated;
    }

    public DisplayVerification Clear(string transport)
    {
        var all = Load();
        all.Remove(transport);
        Save(all);
        return Get(transport);
    }

    private Dictionary<string, DisplayVerification> Load()
    {
        if (!File.Exists(_path))
            return new Dictionary<string, DisplayVerification>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, DisplayVerification>>(json, JsonOptions)
                ?? new Dictionary<string, DisplayVerification>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "display-verification.json is not valid JSON. Delete or repair it before updating verification state.",
                ex);
        }
    }

    private void Save(Dictionary<string, DisplayVerification> values)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(values, JsonOptions) + Environment.NewLine);
        _ = JsonSerializer.Deserialize<Dictionary<string, DisplayVerification>>(
            File.ReadAllText(temp),
            JsonOptions) ?? throw new InvalidOperationException("Refusing to save invalid display verification state.");

        File.Move(temp, _path, true);
    }
}
