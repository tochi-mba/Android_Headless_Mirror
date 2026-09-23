using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

public static class LockScreenModes
{
    public const string Pattern = "pattern";
    public const string Other = "other";
    public const string None = "none";
    public const string Unknown = "";

    public static bool IsValid(string? value) => value is Pattern or Other or None;
}

public sealed record PatternCalibration(double Left, double Top, double Right, double Bottom)
{
    public bool IsValid =>
        Left >= 0 && Top >= 0 && Right <= 1 && Bottom <= 1 && Right > Left && Bottom > Top;
}

public sealed record DeviceProfile
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>pattern, other, none, or empty when not asked yet.</summary>
    public string LockScreenMode { get; set; } = LockScreenModes.Unknown;

    public PatternCalibration? Calibration { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
}

/// <summary>Window placement and sidebar state; remembered between launches, never committed.</summary>
public sealed record UiState
{
    /// <summary>0 means "not saved yet"; the app then picks a size for the current screen.</summary>
    public double Width { get; set; }
    public double Height { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public bool Maximized { get; set; }
    public bool SidebarVisible { get; set; } = true;
    public string SidebarTab { get; set; } = "controls";

    /// <summary>0 means the panel has never been resized; the window then uses its own default.</summary>
    public double SidebarWidth { get; set; }

    /// <summary>The user left the first-run guide before a phone was ever mirrored.</summary>
    public bool SetupDismissed { get; set; }

    /// <summary>
    /// Which tour has been seen through. 0 means never; a later release can raise the number it
    /// looks for and show what is new without repeating the whole thing.
    /// </summary>
    public int TourSeenVersion { get; set; }

    /// <summary>Ids from <see cref="Tips"/> that have been shown once already.</summary>
    public List<string> TipsSeen { get; set; } = [];
}

public sealed record StateDocument
{
    public int Version { get; set; } = 2;
    public string PreferredSerial { get; set; } = string.Empty;
    public List<string> WirelessHosts { get; set; } = [];
    public Dictionary<string, DeviceProfile> Devices { get; set; } = new(StringComparer.Ordinal);
    public UiState Ui { get; set; } = new();
}

/// <summary>
/// Per-machine runtime state (state.json): which phone was seen, its lock type, saved pattern
/// calibration and learned wireless addresses. Never contains credentials or the pattern itself.
/// </summary>
public sealed class StateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private StateDocument _state;

    public StateStore(string path)
    {
        _path = path;
        _state = Load(path);
    }

    public string PreferredSerial
    {
        get { lock (_gate) { return _state.PreferredSerial; } }
    }

    public IReadOnlyList<string> WirelessHosts
    {
        get { lock (_gate) { return _state.WirelessHosts.ToArray(); } }
    }

    public UiState Ui
    {
        get { lock (_gate) { return _state.Ui with { }; } }
    }

    public void SetUi(UiState ui) => Update(state => state.Ui = ui with { });

    public DeviceProfile? GetDevice(string serial)
    {
        lock (_gate)
        {
            return _state.Devices.TryGetValue(serial, out var profile) ? profile with { } : null;
        }
    }

    public IReadOnlyDictionary<string, DeviceProfile> Devices
    {
        get { lock (_gate) { return _state.Devices.ToDictionary(x => x.Key, x => x.Value with { }, StringComparer.Ordinal); } }
    }

    public void Update(Action<StateDocument> mutate)
    {
        lock (_gate)
        {
            using var transaction = CrossProcessFileLock.Acquire(_path);

            // Reload while holding the cross-process lock. The in-memory snapshot may be
            // older than a CLI or second process update made since this store was created.
            var latest = Load(_path);
            mutate(latest);
            Save(latest);
            _state = latest;
        }
    }

    public void RememberDevice(string serial, string name, string model)
    {
        Update(state =>
        {
            var profile = GetOrAdd(state, serial);
            if (!string.IsNullOrWhiteSpace(name))
            {
                profile.Name = name;
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                profile.Model = model;
            }

            profile.LastSeenUtc = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(state.PreferredSerial) && !AdbParsing.IsTcpSerial(serial))
            {
                state.PreferredSerial = serial;
            }
        });
    }

    public void SetLockScreenMode(string serial, string mode)
    {
        if (!LockScreenModes.IsValid(mode))
        {
            throw new ArgumentException("Lock-screen mode must be pattern, other or none.", nameof(mode));
        }

        Update(state => GetOrAdd(state, serial).LockScreenMode = mode);
    }

    /// <summary>Forgets the lock-screen answer and calibration for one serial, or for every device with "ALL".</summary>
    public int ResetLockScreen(string serialOrAll)
    {
        var count = 0;
        Update(state =>
        {
            foreach (var pair in state.Devices)
            {
                if (serialOrAll == "ALL" || pair.Key == serialOrAll)
                {
                    pair.Value.LockScreenMode = LockScreenModes.Unknown;
                    pair.Value.Calibration = null;
                    count++;
                }
            }
        });
        return count;
    }

    public void SetCalibration(string serial, PatternCalibration? calibration)
    {
        if (calibration is { IsValid: false })
        {
            throw new ArgumentException("Calibration bounds must be normalized and non-empty.", nameof(calibration));
        }

        Update(state => GetOrAdd(state, serial).Calibration = calibration);
    }

    public void AddWirelessHosts(IEnumerable<string> hosts)
    {
        Update(state =>
        {
            foreach (var host in hosts)
            {
                var trimmed = host.Trim();
                if (trimmed.Length > 0 && !state.WirelessHosts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    state.WirelessHosts.Add(trimmed);
                }
            }
        });
    }

    private static DeviceProfile GetOrAdd(StateDocument state, string serial)
    {
        if (!state.Devices.TryGetValue(serial, out var profile))
        {
            profile = new DeviceProfile();
            state.Devices[serial] = profile;
        }

        return profile;
    }

    private void Save(StateDocument state)
    {
        var json = JsonSerializer.Serialize(state, RexJsonContext.Default.StateDocument) + Environment.NewLine;
        AtomicFile.Write(_path, json, keepBackupAt: null, validate: null);
    }

    private static StateDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new StateDocument();
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            // A corrupt state file must never block mirroring; start fresh.
            return new StateDocument();
        }

        if (root["Version"] is null && root["DeviceProfiles"] is not null)
        {
            return MigrateLegacy(root, path);
        }

        try
        {
            var state = root.Deserialize(RexJsonContext.Default.StateDocument) ?? new StateDocument();
            state.Devices = new Dictionary<string, DeviceProfile>(state.Devices, StringComparer.Ordinal);
            state.WirelessHosts ??= [];
            state.Ui ??= new UiState();
            return state;
        }
        catch (JsonException)
        {
            return new StateDocument();
        }
    }

    private static StateDocument MigrateLegacy(JsonObject root, string path)
    {
        var state = new StateDocument
        {
            PreferredSerial = root["PreferredSerial"]?.ToString() ?? string.Empty,
        };

        if (root["WirelessHosts"] is JsonArray hosts)
        {
            state.WirelessHosts = hosts.Select(x => x?.ToString() ?? string.Empty).Where(x => x.Length > 0).ToList();
        }

        if (root["DeviceProfiles"] is JsonArray profiles)
        {
            foreach (var node in profiles.OfType<JsonObject>())
            {
                var serial = node["Serial"]?.ToString();
                if (string.IsNullOrWhiteSpace(serial))
                {
                    continue;
                }

                var mode = node["LockScreenMode"]?.ToString()?.ToLowerInvariant() ?? string.Empty;
                state.Devices[serial] = new DeviceProfile
                {
                    LockScreenMode = LockScreenModes.IsValid(mode) ? mode : LockScreenModes.Unknown,
                    Calibration = LoadLegacyCalibration(path, serial),
                };
            }
        }

        return state;
    }

    private static PatternCalibration? LoadLegacyCalibration(string statePath, string serial)
    {
        var directory = Path.Combine(Path.GetDirectoryName(statePath) ?? string.Empty, "pattern-calibration");
        var safeSerial = System.Text.RegularExpressions.Regex.Replace(serial, "[^A-Za-z0-9._-]", "_");
        var file = Path.Combine(directory, safeSerial + ".json");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
            if (node is null)
            {
                return null;
            }

            var calibration = new PatternCalibration(
                node["Left"]?.GetValue<double>() ?? -1,
                node["Top"]?.GetValue<double>() ?? -1,
                node["Right"]?.GetValue<double>() ?? -1,
                node["Bottom"]?.GetValue<double>() ?? -1);
            return calibration.IsValid ? calibration : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
