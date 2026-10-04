using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>A profile once applied, and the settings in it this version does not know.</summary>
public sealed record ProfileApplied(Profile Profile, IReadOnlyList<string> Unknown)
{
    /// <summary>"Gaming applied", and how many of its settings are not used any more.</summary>
    public string Words => Profile.Name + " applied" + (Unknown.Count switch
    {
        0 => string.Empty,
        1 => " · 1 setting in it is not used any more",
        var n => $" · {n} settings in it are not used any more",
    });
}

/// <summary>
/// What can be done with profiles, the same for the app and the command line: apply one (a saved
/// one, else a preset) in a single config write, save, update, rename and delete them with the
/// rules and phones that name them following along, and keep the record of what an automatic
/// profile changed (profiles/.automatic.json, so a crash cannot strand it).
/// </summary>
public sealed class ProfileBook
{
    private readonly Func<RexConfig> _config;
    private readonly Action<Action<RexConfig>> _updateConfig;

    public ProfileBook(ProfileStore store, StateStore state, Func<RexConfig> config, Action<Action<RexConfig>> updateConfig)
    {
        Store = store;
        State = state;
        _config = config;
        _updateConfig = updateConfig;
    }

    /// <summary>For the command line: config.json and state.json themselves, under their locks.</summary>
    public static ProfileBook ForFiles(AppPaths paths)
    {
        var config = new ConfigStore(paths.Config);
        return new ProfileBook(ProfileStore.For(paths), new StateStore(paths.State), config.Load, mutate =>
        {
            using var transaction = CrossProcessFileLock.Acquire(paths.Config);
            var latest = ConfigFile.Load(paths.Config);
            mutate(latest);
            latest.Normalize();
            ConfigFile.Save(paths.Config, latest);
        });
    }

    public ProfileStore Store { get; }

    public StateStore State { get; }

    public RexConfig Config => _config();

    private string PutBackPath => Path.Combine(Store.Folder, ".automatic.json");

    /// <summary>A saved profile by name, else a preset; null when neither has that name.</summary>
    public Profile? Find(string name) => Store.Load(name) ?? ProfilePresets.Find(name);

    /// <summary>The profile a number key applies: the list's first readable profiles, in order.</summary>
    public string? NameAt(int number) =>
        number < 1 ? null : Store.List().Where(e => e.Profile is not null).Select(e => e.Name).ElementAtOrDefault(number - 1);

    /// <summary>
    /// Applies a profile in one config write. By hand it is remembered as the one in effect, the
    /// base an automatic profile sits on. Null when there is no profile of that name.
    /// </summary>
    public ProfileApplied? Apply(string name, bool byHand = true)
    {
        if (Find(name) is not { } profile)
        {
            return null;
        }

        IReadOnlyList<string> unknown = [];
        _updateConfig(config =>
        {
            var (applied, missing) = profile.Apply(config);
            unknown = missing;
            config.CopyFrom(applied);
        });

        if (byHand)
        {
            State.SetUi(State.Ui with { Profile = profile.Name });
        }

        return new ProfileApplied(profile, unknown);
    }

    /// <summary>Saves the current settings as a profile (of these groups, only what differs from how the app ships when asked).</summary>
    public Profile Save(string name, IReadOnlyCollection<string>? groups, bool onlyChanged, DateTimeOffset now)
    {
        var profile = Profile.From(name.Trim(), Config, groups, onlyChanged, now);
        Store.Save(profile);
        return profile;
    }

    /// <summary>Gives a profile the current values of the settings it already has.</summary>
    public Profile Update(string name, DateTimeOffset now)
    {
        var profile = Store.Load(name) ?? throw new KeyNotFoundException($"There is no profile called \"{name.Trim()}\".");
        var current = ConfigPaths.Values(Config);
        var settings = profile.Settings.Keys.Where(current.ContainsKey).ToDictionary(path => path, path => current[path], StringComparer.Ordinal);
        var updated = profile with { Settings = settings, Saved = now };
        Store.Save(updated);
        return updated;
    }

    /// <summary>Renames a profile; the rules, the phones and the hand-applied one that name it follow.</summary>
    public void Rename(string from, string to)
    {
        Store.Rename(from, to);
        Follow(from, to.Trim());
    }

    /// <summary>Deletes a profile; the rules, the phones and the hand-applied one that named it are cleared.</summary>
    public void Delete(string name)
    {
        if (!Store.List().Any(e => e.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new KeyNotFoundException($"There is no profile called \"{name.Trim()}\".");
        }

        Store.Delete(name);
        Follow(name, string.Empty);
    }

    private void Follow(string from, string to)
    {
        bool Named(string value) => value.Equals(from.Trim(), StringComparison.OrdinalIgnoreCase);
        var rules = Config.Profiles;
        if (Named(rules.WhenFullscreen) || Named(rules.WhenOnBattery))
        {
            _updateConfig(c =>
            {
                if (Named(c.Profiles.WhenFullscreen)) c.Profiles.WhenFullscreen = to;
                if (Named(c.Profiles.WhenOnBattery)) c.Profiles.WhenOnBattery = to;
            });
        }

        State.Update(state =>
        {
            if (Named(state.Ui.Profile))
            {
                state.Ui = state.Ui with { Profile = to };
            }

            foreach (var device in state.Devices.Values.Where(d => Named(d.Profile)))
            {
                device.Profile = to;
            }
        });
    }

    /// <summary>Makes a profile a phone's own, applied when it connects; an empty name for none.</summary>
    public void UseForPhone(string serial, string name) => State.Update(state =>
    {
        if (state.Devices.TryGetValue(serial, out var device))
        {
            device.Profile = name.Trim();
        }
    });

    /// <summary>What the automatic profile in effect changed, or null when none is.</summary>
    public PutBack? ReadPutBack()
    {
        try
        {
            if (!File.Exists(PutBackPath) || JsonNode.Parse(File.ReadAllText(PutBackPath)) is not JsonObject root ||
                root["profile"] is not JsonValue profile || !profile.TryGetValue<string>(out var name))
            {
                return null;
            }

            static IReadOnlyDictionary<string, JsonNode> Values(JsonNode? node) =>
                node is JsonObject obj
                    ? obj.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!.DeepClone(), StringComparer.Ordinal)
                    : new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            return new PutBack(name, Values(root["before"]), Values(root["set"]));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Keeps the record of what an automatic profile changed; null removes it.</summary>
    public void WritePutBack(PutBack? record)
    {
        if (record is null)
        {
            if (File.Exists(PutBackPath))
            {
                File.Delete(PutBackPath);
            }

            return;
        }

        static JsonObject Object(IReadOnlyDictionary<string, JsonNode> values)
        {
            var obj = new JsonObject();
            foreach (var (key, value) in values)
            {
                obj[key] = value.DeepClone();
            }

            return obj;
        }

        Directory.CreateDirectory(Store.Folder);
        AtomicFile.Write(PutBackPath, new JsonObject
        {
            ["profile"] = record.Profile,
            ["before"] = Object(record.Before),
            ["set"] = Object(record.Set),
        }.ToJsonString(), keepBackupAt: null, validate: null);
    }

    /// <summary>Starts an automatic profile in one config write and keeps what it changed.</summary>
    public PutBack Start(Profile profile)
    {
        PutBack? started = null;
        _updateConfig(config =>
        {
            var (applied, record, _) = ProfilePolicy.Start(config, profile);
            started = record;
            config.CopyFrom(applied);
        });
        var result = started ?? new PutBack(profile.Name, new Dictionary<string, JsonNode>(), new Dictionary<string, JsonNode>());
        WritePutBack(result);
        return result;
    }

    /// <summary>Ends the automatic profile in effect: puts back what it changed when asked, and forgets it.</summary>
    public void End(PutBack record, bool putBack)
    {
        if (putBack)
        {
            _updateConfig(config => config.CopyFrom(ProfilePolicy.End(config, record)));
        }

        WritePutBack(null);
    }
}
