using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex phones: every connected phone and where it shows. While the app runs it says which phone is
/// the main one, which is beside it and which is in use, and can show one beside, stop it, switch
/// to the other, or swap them; without the app it lists what ADB sees.
/// </summary>
public static class PhoneCommands
{
    public const string Usage = "rex phones [beside [serial] | stop | switch | make-main]";

    private static IpcRequest Request(string[] args)
    {
        var positional = Arguments.Positional(args);
        var verb = positional.Length >= 2 ? positional[1].ToLowerInvariant() : string.Empty;
        if (verb is not ("" or "beside" or "stop" or "switch" or "make-main"))
        {
            throw new ArgumentException("Usage: " + Usage);
        }

        var fields = new Dictionary<string, string> { ["verb"] = verb };
        if (verb == "beside" && positional.Length >= 3)
        {
            fields["serial"] = positional[2];
        }

        return new IpcRequest("phones", fields);
    }

    /// <summary>The app's answer, or what ADB sees when the app is not running (only listing works then).</summary>
    private static async Task<JsonObject> RunAsync(string[] args, CliContext context)
    {
        var request = Request(args);
        var response = await context.Ipc.SendAsync(request, answerWithin: TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (response is null)
        {
            if (request.Arg("verb").Length > 0)
            {
                throw new InvalidOperationException("The app is not running. Start it with 'rex open'.");
            }

            var devices = await context.RequireAdb().ListDevicesAsync().ConfigureAwait(false);
            return new JsonObject
            {
                ["words"] = devices.Count == 0 ? "No phone is connected." : "The app is not running, so no phone is shown.",
                ["activeSerial"] = null,
                ["beside"] = null,
                ["list"] = new JsonArray([.. devices.Select(d => (JsonNode)new JsonObject
                {
                    ["serial"] = d.Serial,
                    ["name"] = d.Model.Length > 0 ? d.Model.Replace('_', ' ') : d.Serial,
                    ["state"] = d.State,
                    ["transport"] = d.Transport,
                    ["role"] = "connected",
                    ["active"] = false,
                })]),
            };
        }

        return response.Ok ? response.Data!.DeepClone().AsObject() : throw new InvalidOperationException(response.Error);
    }

    public static async Task<int> HumanAsync(string[] args, CliContext context)
    {
        var data = await RunAsync(args, context).ConfigureAwait(false);
        Console.WriteLine(data["words"]!.GetValue<string>());
        foreach (var phone in data["list"]!.AsArray().OfType<JsonObject>())
        {
            var role = phone["role"]!.GetValue<string>();
            var tags = new List<string> { phone["transport"]!.GetValue<string>(), phone["state"]!.GetValue<string>() };
            if (role != "connected")
            {
                tags.Add(role == "main" ? "main phone" : "beside");
            }

            if (phone["active"]!.GetValue<bool>())
            {
                tags.Add("in use");
            }

            Console.WriteLine($"  {phone["name"]!.GetValue<string>(),-28} {phone["serial"]!.GetValue<string>(),-24} {string.Join(" · ", tags)}");
        }

        return 0;
    }

    public static async Task<MachineResult> MachineAsync(string[] args, CliContext context) =>
        MachineMode.Success("phones", await RunAsync(args, context).ConfigureAwait(false));
}
