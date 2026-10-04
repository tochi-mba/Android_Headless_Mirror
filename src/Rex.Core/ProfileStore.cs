using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>A profile as listed: the profile, or why its file could not be read.</summary>
public sealed record ProfileEntry(string Name, Profile? Profile, string? Problem);

/// <summary>
/// The profiles in their folder beside config.json: one file each, named after the profile, and
/// order.json for the order the person put them in. The app and the command line both use it,
/// under the same cross-process lock as the config. A file that cannot be read is listed with the
/// reason, never fatal.
/// </summary>
public sealed class ProfileStore
{
    private const string OrderFile = "order.json";
    private const int Format = 1;

    public ProfileStore(string folder) => Folder = folder;

    public string Folder { get; }

    public static ProfileStore For(AppPaths paths) => new(Path.Combine(paths.Root, "profiles"));

    private string FileOf(string name) => Path.Combine(Folder, name.Trim() + ".json");

    /// <summary>Every profile, in the person's order and then by name.</summary>
    public IReadOnlyList<ProfileEntry> List()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var names = Directory.EnumerateFiles(Folder, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => !name.Equals("order", StringComparison.OrdinalIgnoreCase) && !name.StartsWith('.'))
            .ToList();
        var order = ReadOrder();
        int Place(string name)
        {
            var at = order.FindIndex(o => o.Equals(name, StringComparison.OrdinalIgnoreCase));
            return at < 0 ? int.MaxValue : at;
        }

        return names
            .OrderBy(Place)
            .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(Read)
            .ToArray();
    }

    /// <summary>One profile by name (any case), or null when there is none or it cannot be read.</summary>
    public Profile? Load(string name) =>
        List().FirstOrDefault(e => e.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))?.Profile;

    /// <summary>Saves a profile, replacing one of the same name; a new one goes last in the order.</summary>
    public void Save(Profile profile)
    {
        if (ProfileNames.WhyNot(profile.Name) is { } why)
        {
            throw new ArgumentException(why);
        }

        Directory.CreateDirectory(Folder);
        var existing = List().FirstOrDefault(e => e.Name.Equals(profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is not null && existing.Name != profile.Name.Trim())
        {
            File.Delete(FileOf(existing.Name));
        }

        AtomicFile.Write(FileOf(profile.Name), ToJson(profile with { Name = profile.Name.Trim() }), keepBackupAt: null, validate: null);
        var order = ReadOrder();
        if (!order.Contains(profile.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            order.Add(profile.Name.Trim());
            WriteOrder(order);
        }
    }

    /// <summary>Gives a profile a new name; the order keeps its place.</summary>
    public void Rename(string from, string to)
    {
        var profile = Load(from) ?? throw new KeyNotFoundException($"There is no profile called \"{from}\".");
        if (ProfileNames.WhyNot(to) is { } why)
        {
            throw new ArgumentException(why);
        }

        if (!from.Equals(to, StringComparison.OrdinalIgnoreCase) && Load(to) is not null)
        {
            throw new ArgumentException($"There is already a profile called \"{to.Trim()}\".");
        }

        File.Delete(FileOf(Name(from)));
        AtomicFile.Write(FileOf(to), ToJson(profile with { Name = to.Trim() }), keepBackupAt: null, validate: null);
        WriteOrder(ReadOrder().Select(n => n.Equals(from.Trim(), StringComparison.OrdinalIgnoreCase) ? to.Trim() : n).ToList());
    }

    /// <summary>A copy under a free name: "Gaming (2)".</summary>
    public string Duplicate(string name, DateTimeOffset now)
    {
        var profile = Load(name) ?? throw new KeyNotFoundException($"There is no profile called \"{name}\".");
        var copy = FreeName(profile.Name);
        Save(profile with { Name = copy, Saved = now });
        return copy;
    }

    public void Delete(string name)
    {
        var real = Name(name);
        File.Delete(FileOf(real));
        WriteOrder(ReadOrder().Where(n => !n.Equals(real, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    /// <summary>Moves a profile up (negative) or down (positive) the list.</summary>
    public void Move(string name, int delta)
    {
        var names = List().Select(e => e.Name).ToList();
        var from = names.FindIndex(n => n.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (from < 0)
        {
            return;
        }

        var real = names[from];
        names.RemoveAt(from);
        names.Insert(Math.Clamp(from + delta, 0, names.Count), real);
        WriteOrder(names);
    }

    /// <summary>Writes a profile to a file of the person's choosing.</summary>
    public void Export(string name, string path)
    {
        var profile = Load(name) ?? throw new KeyNotFoundException($"There is no profile called \"{name}\".");
        File.WriteAllText(path, ToJson(profile));
    }

    /// <summary>Adds a profile from a file; a name already taken gets " (2)". Returns the name it was saved as.</summary>
    public string Import(string path, DateTimeOffset now)
    {
        var profile = Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path))
            ?? throw new FormatException("That file is not a profile.");
        var name = ProfileNames.IsValid(profile.Name) ? profile.Name.Trim() : "Imported";
        var free = Load(name) is null ? name : FreeName(name);
        Save(profile with { Name = free, Saved = now });
        return free;
    }

    private string Name(string name) =>
        List().FirstOrDefault(e => e.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))?.Name
        ?? throw new KeyNotFoundException($"There is no profile called \"{name}\".");

    private string FreeName(string name)
    {
        var taken = List().Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = name.Length > ProfileNames.Longest - 5 ? name[..(ProfileNames.Longest - 5)].Trim() : name;
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private ProfileEntry Read(string name)
    {
        try
        {
            var profile = Parse(File.ReadAllText(FileOf(name)), name);
            return profile is null
                ? new ProfileEntry(name, null, "It is not a profile.")
                : new ProfileEntry(name, profile with { Name = name }, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProfileEntry(name, null, "It could not be read: " + ex.Message);
        }
    }

    /// <summary>A profile from its file's text, or null when the text is not one.</summary>
    public static Profile? Parse(string text, string fallbackName)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || root["settings"] is not JsonObject settings)
            {
                return null;
            }

            var name = root["name"]?.GetValueKind() == JsonValueKind.String ? root["name"]!.GetValue<string>() : fallbackName;
            var saved = root["saved"]?.GetValueKind() == JsonValueKind.String && DateTimeOffset.TryParse(root["saved"]!.GetValue<string>(), out var when)
                ? when
                : DateTimeOffset.UnixEpoch;
            var values = settings.Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!.DeepClone(), StringComparer.Ordinal);
            return new Profile(name, values, saved);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ToJson(Profile profile)
    {
        var settings = new JsonObject();
        foreach (var (path, value) in profile.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            settings[path] = value.DeepClone();
        }

        return new JsonObject
        {
            ["format"] = Format,
            ["name"] = profile.Name,
            ["saved"] = profile.Saved.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["settings"] = settings,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private List<string> ReadOrder()
    {
        try
        {
            var path = Path.Combine(Folder, OrderFile);
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonArray array
                ? array.Where(n => n?.GetValueKind() == JsonValueKind.String).Select(n => n!.GetValue<string>()).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void WriteOrder(List<string> order)
    {
        Directory.CreateDirectory(Folder);
        AtomicFile.Write(Path.Combine(Folder, OrderFile), new JsonArray([.. order.Select(n => (JsonNode)n)]).ToJsonString(), keepBackupAt: null, validate: null);
    }
}
