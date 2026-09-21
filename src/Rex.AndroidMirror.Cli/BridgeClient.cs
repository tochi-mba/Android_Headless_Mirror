using System.Text.Json;

namespace Rex.AndroidMirror.Cli;

public sealed record RexDevice(
    string Serial,
    string State,
    bool IsTcp,
    string Manufacturer,
    string Model,
    string DisplayName);

public sealed record RexStatus(
    bool SetupComplete,
    bool AutostartEnabled,
    bool PersistentOff,
    bool SupervisorRunning,
    bool MirrorRunning,
    string AdbPath,
    string ScrcpyPath,
    IReadOnlyList<RexDevice> Devices);

public sealed class BridgeClient
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;

    public BridgeClient(AppPaths paths, ProcessRunner runner)
    {
        _paths = paths;
        _runner = runner;
    }

    public async Task<JsonDocument> InvokeAsync(
        string action,
        string? serial = null,
        string? name = null,
        string? ns = null,
        string? key = null,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-Action", action
        };

        Add(args, "-Serial", serial);
        Add(args, "-Name", name);
        Add(args, "-Namespace", ns);
        Add(args, "-Key", key);

        if (value is not null)
        {
            args.Add("-Value");
            args.Add(value);
        }

        var result = await _runner.RunPowerShellAsync(
            _paths.Bridge,
            args,
            captureOutput: true,
            cancellationToken);

        var payload = LastJsonLine(result.StdOut);
        if (string.IsNullOrWhiteSpace(payload))
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr)
                ? $"Bridge exited with code {result.ExitCode}."
                : result.StdErr.Trim();
            throw new InvalidOperationException(detail);
        }

        var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("Ok", out var ok) && !ok.GetBoolean())
        {
            var error = root.TryGetProperty("Error", out var errorNode)
                ? errorNode.GetString()
                : root.TryGetProperty("Text", out var textNode)
                    ? textNode.GetString()
                    : "The bridge action failed.";

            document.Dispose();
            throw new InvalidOperationException(error);
        }

        return document;
    }

    public async Task<RexStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await InvokeAsync("status", cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var devices = new List<RexDevice>();
        if (root.TryGetProperty("Devices", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                devices.Add(new RexDevice(
                    GetString(row, "Serial"),
                    GetString(row, "State"),
                    GetBool(row, "IsTcp"),
                    GetString(row, "Manufacturer"),
                    GetString(row, "Model"),
                    GetString(row, "DisplayName")));
            }
        }

        return new RexStatus(
            GetBool(root, "SetupComplete"),
            GetBool(root, "AutostartEnabled"),
            GetBool(root, "PersistentOff"),
            GetBool(root, "SupervisorRunning"),
            GetBool(root, "MirrorRunning"),
            GetString(root, "AdbPath"),
            GetString(root, "ScrcpyPath"),
            devices);
    }

    public async Task<IReadOnlyList<RexDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await InvokeAsync("devices", cancellationToken: cancellationToken);
        var list = new List<RexDevice>();

        if (doc.RootElement.TryGetProperty("Devices", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                list.Add(new RexDevice(
                    GetString(row, "Serial"),
                    GetString(row, "State"),
                    GetBool(row, "IsTcp"),
                    GetString(row, "Manufacturer"),
                    GetString(row, "Model"),
                    GetString(row, "DisplayName")));
            }
        }

        return list;
    }

    public async Task<IReadOnlyList<(string Key, string Value, string Risk)>> ListAndroidSettingsAsync(
        string serial,
        string ns,
        CancellationToken cancellationToken = default)
    {
        using var doc = await InvokeAsync(
            "settings-list",
            serial: serial,
            ns: ns,
            cancellationToken: cancellationToken);

        var list = new List<(string, string, string)>();
        if (doc.RootElement.TryGetProperty("Rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                list.Add((
                    GetString(row, "Key"),
                    GetString(row, "Value"),
                    GetString(row, "Risk")));
            }
        }

        return list;
    }

    private static void Add(List<string> args, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        args.Add(name);
        args.Add(value);
    }

    private static string LastJsonLine(string output)
    {
        return output
            .Split(new[] { "
", "
" }, StringSplitOptions.RemoveEmptyEntries)
            .Reverse()
            .FirstOrDefault(line => line.TrimStart().StartsWith('{'))?
            .Trim()
            ?? string.Empty;
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               value.GetBoolean();
    }
}
