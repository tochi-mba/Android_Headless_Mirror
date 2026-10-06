using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex keys: every key the window answers to and every key of browse mode, as the person has them;
/// give one a key, take it away, or put keys back to how the app ships. A running app follows the
/// change by itself, as it follows any change to config.json.
/// </summary>
public static class KeyCommands
{
    public const string Usage = "rex keys [set <action> <key|none> | reset [action]] [--browse]";

    private static async Task<JsonObject> RunAsync(string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        var browse = args.Contains("--browse", StringComparer.Ordinal);
        var verb = positional.Length >= 2 ? positional[1].ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "":
                break;
            case "set":
                Arguments.Require(positional, 4, Usage);
                Change(context, config => Set(config, positional[2], positional[3], browse));
                break;
            case "reset":
                Change(context, config =>
                {
                    var bindings = browse ? config.Keys.Browse : config.Keys.Window;
                    if (positional.Length >= 3)
                    {
                        bindings.RemoveAll(b => b.Action == positional[2]);
                    }
                    else
                    {
                        bindings.Clear();
                    }
                });
                break;
            default:
                throw new ArgumentException("Usage: " + Usage);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return Describe(context.Config.Load());
    }

    private static void Change(CliContext context, Action<RexConfig> change)
    {
        var config = context.Config.Load();
        change(config);
        context.Config.Save(config);
    }

    /// <summary>Gives an action a key ("none" takes it away), refused in words when it cannot be that key.</summary>
    private static void Set(RexConfig config, string action, string key, bool browse)
    {
        var map = new KeyMap(config.Keys);
        key = key.Equals("none", StringComparison.OrdinalIgnoreCase) ? string.Empty : key;
        var why = browse ? map.WhyNotBrowse(action, key) : map.WhyNotWindow(action, key) ?? KeyMap.WhyNotFromAnywhere(config.GlobalKeys, key);
        if (why is not null)
        {
            throw new FormatException(why);
        }

        var shipped = browse ? new KeyMap(new KeysSettings()).BrowseChordFor(action) : new KeyMap(new KeysSettings()).ChordFor(action);
        KeyMap.Set(browse ? config.Keys.Browse : config.Keys.Window, action, key, shipped);
    }

    private static JsonObject Describe(RexConfig config)
    {
        var map = new KeyMap(config.Keys);
        static JsonNode? Text(KeyChord? chord) => chord?.ToString();
        return new JsonObject
        {
            ["window"] = new JsonArray([.. map.Window.Select(k => (JsonNode)new JsonObject
            {
                ["action"] = k.Id,
                ["label"] = k.Label,
                ["key"] = Text(k.Now),
                ["shipped"] = Text(k.Shipped),
            })]),
            ["browse"] = new JsonArray([.. map.Browse.Select(k => (JsonNode)new JsonObject
            {
                ["action"] = k.Action,
                ["label"] = k.Label,
                ["key"] = Text(k.Now),
                ["shipped"] = Text(k.Shipped),
            })]),
        };
    }

    public static async Task<int> HumanAsync(string[] args, CliContext context)
    {
        var data = await RunAsync(args, context).ConfigureAwait(false);
        void List(string title, string field)
        {
            Console.WriteLine(title);
            foreach (var key in data[field]!.AsArray().OfType<JsonObject>().Where(k => k["key"] is not null || k["shipped"] is not null))
            {
                var now = key["key"]?.GetValue<string>() ?? "(none)";
                var yours = key["key"]?.GetValue<string>() != key["shipped"]?.GetValue<string>() ? "  · yours" : string.Empty;
                Console.WriteLine($"  {now,-22} {key["label"]!.GetValue<string>()} ({key["action"]!.GetValue<string>()}){yours}");
            }
        }

        List("In the window:", "window");
        List("In browse mode:", "browse");
        Console.WriteLine("Give any action a key with: rex keys set <action> <key>. Every action: rex action list.");
        return 0;
    }

    public static async Task<MachineResult> MachineAsync(string[] args, CliContext context) =>
        MachineMode.Success("keys", await RunAsync(args, context).ConfigureAwait(false));
}
