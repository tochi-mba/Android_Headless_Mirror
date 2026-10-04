using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>One profile command: what to do, to which profile, and with what.</summary>
public sealed record ProfileRequest(string Verb, string Name = "", string To = "", string File = "", IReadOnlyList<string>? Groups = null, bool All = false)
{
    public const string Usage = "rex profile list | show <name> | apply <name> | save <name> [--groups Mirror,Sound] [--all] | rename <old> <new> | delete <name> | export <name> <file> | import <file>";

    public static readonly IReadOnlyList<string> Verbs = ["list", "show", "apply", "save", "rename", "delete", "export", "import"];

    /// <summary>The request as the pipe carries it.</summary>
    public Dictionary<string, string> ToFields()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["verb"] = Verb };
        if (Name.Length > 0) fields["name"] = Name;
        if (To.Length > 0) fields["to"] = To;
        if (File.Length > 0) fields["file"] = File;
        if (Groups is { Count: > 0 }) fields["groups"] = string.Join(',', Groups);
        if (All) fields["all"] = "true";
        return fields;
    }

    /// <summary>A request from the pipe's fields; an unknown verb or a missing name is refused with the usage.</summary>
    public static ProfileRequest FromFields(IReadOnlyDictionary<string, string> fields)
    {
        string Field(string key) => fields.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
        var request = new ProfileRequest(
            Field("verb").ToLowerInvariant() is { Length: > 0 } verb ? verb : "list",
            Field("name"),
            Field("to"),
            Field("file"),
            Field("groups").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Field("all").Equals("true", StringComparison.OrdinalIgnoreCase));
        return request.Check();
    }

    /// <summary>The request, or an <see cref="ArgumentException"/> saying what it lacks.</summary>
    public ProfileRequest Check()
    {
        var needs = Verb switch
        {
            "list" => [],
            "show" or "apply" or "save" or "delete" => [Name],
            "rename" => [Name, To],
            "export" => [Name, File],
            "import" => new[] { File },
            _ => null,
        };
        if (needs is null || needs.Any(part => part.Length == 0))
        {
            throw new ArgumentException("Usage: " + Usage);
        }

        return this;
    }
}

/// <summary>What a profile command did: the words for a person, and the same as data for a program.</summary>
public sealed record ProfileResult(string Words, JsonObject Data);

/// <summary>
/// The profile commands, the same whether the command line runs them on the files or the app runs
/// them for it over the pipe.
/// </summary>
public static class ProfileVerbs
{
    public static ProfileResult Run(ProfileBook book, ProfileRequest request, DateTimeOffset now)
    {
        switch (request.Verb)
        {
            case "list":
                return List(book);
            case "show":
            {
                var profile = book.Find(request.Name) ?? throw Missing(request.Name);
                var lines = profile.Settings.Select(p => $"  {p.Key} = {p.Value.ToJsonString()}");
                return new ProfileResult(
                    $"{profile.Name} ({Count(profile.Settings.Count)})" + Environment.NewLine + string.Join(Environment.NewLine, lines),
                    Describe(profile, preset: book.Store.Load(profile.Name) is null));
            }

            case "apply":
            {
                var applied = book.Apply(request.Name) ?? throw Missing(request.Name);
                return new ProfileResult(applied.Words, new JsonObject
                {
                    ["name"] = applied.Profile.Name,
                    ["unknown"] = new JsonArray([.. applied.Unknown.Select(path => JsonValue.Create(path))]),
                });
            }

            case "save":
            {
                var profile = book.Save(request.Name, request.Groups, onlyChanged: !request.All, now);
                return new ProfileResult($"Saved {profile.Name} with {Count(profile.Settings.Count)}.", Describe(profile, preset: false));
            }

            case "rename":
                book.Rename(request.Name, request.To);
                return new ProfileResult($"{request.Name} is now {request.To}.", new JsonObject { ["name"] = request.To, ["was"] = request.Name });
            case "delete":
                book.Delete(request.Name);
                return new ProfileResult($"{request.Name} deleted.", new JsonObject { ["name"] = request.Name });
            case "export":
                book.Store.Export(request.Name, request.File);
                return new ProfileResult($"{request.Name} written to {request.File}.", new JsonObject { ["name"] = request.Name, ["file"] = request.File });
            default:
                var name = book.Store.Import(request.File, now);
                return new ProfileResult($"Imported as {name}.", new JsonObject { ["name"] = name });
        }
    }

    /// <summary>Every saved profile in order, the one in effect, and the presets.</summary>
    public static ProfileResult List(ProfileBook book)
    {
        var entries = book.Store.List();
        var automatic = book.ReadPutBack()?.Profile;
        var manual = book.State.Ui.Profile;
        var lines = entries.Select((e, i) => $"{i + 1}. {e.Name}" + (e.Profile is { } p ? $" · {Count(p.Settings.Count)}" : " · could not be read: " + e.Problem)).ToList();
        if (lines.Count == 0)
        {
            lines.Add("No profiles yet. Save one with: rex profile save <name>");
        }

        lines.Add("Presets: " + string.Join(", ", ProfilePresets.All.Select(p => p.Name)));
        var now = automatic ?? (manual.Length > 0 ? manual : null);
        lines.Add(now is null ? "Now: your own settings" : $"Now: {now}" + (automatic is not null ? " (switched on by itself)" : string.Empty));
        return new ProfileResult(string.Join(Environment.NewLine, lines), new JsonObject
        {
            ["current"] = now,
            ["manual"] = manual,
            ["automatic"] = automatic,
            ["profiles"] = new JsonArray([.. entries.Select(e => (JsonNode)new JsonObject
            {
                ["name"] = e.Name,
                ["settings"] = e.Profile?.Settings.Count,
                ["groups"] = e.Profile is { } p ? new JsonArray([.. p.Groups.Select(g => JsonValue.Create(g))]) : null,
                ["problem"] = e.Problem,
            })]),
            ["presets"] = new JsonArray([.. ProfilePresets.All.Select(p => JsonValue.Create(p.Name))]),
        });
    }

    private static JsonObject Describe(Profile profile, bool preset)
    {
        var settings = new JsonObject();
        foreach (var (path, value) in profile.Settings)
        {
            settings[path] = value.DeepClone();
        }

        return new JsonObject { ["name"] = profile.Name, ["preset"] = preset, ["settings"] = settings };
    }

    private static string Count(int settings) => settings == 1 ? "1 setting" : $"{settings} settings";

    private static KeyNotFoundException Missing(string name) => new($"There is no profile called \"{name.Trim()}\".");
}
