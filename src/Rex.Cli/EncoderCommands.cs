using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex encoders: the phone's video encoders, to choose one for Mirror.VideoEncoder. While the app
/// runs it reads them itself, so the read waits its turn behind the mirror and its copies starting.
/// </summary>
public static class EncoderCommands
{
    public const string Usage = "rex encoders [--serial S]";

    private static async Task<JsonObject> RunAsync(string[] args, CliContext context)
    {
        var serial = Arguments.Option(args, "--serial");
        var fields = new Dictionary<string, string>();
        if (serial is not null)
        {
            fields["serial"] = serial;
        }

        // Reading them can wait behind the mirror starting, and scrcpy itself takes up to a minute.
        var response = await context.Ipc.SendAsync(new IpcRequest("encoders", fields), answerWithin: EncoderList.Timeout + TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (response is not null)
        {
            return response.Ok ? response.Data!.DeepClone().AsObject() : throw new InvalidOperationException(response.Error);
        }

        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        var tools = ToolLocator.Find(context.Paths) ?? throw new InvalidOperationException("scrcpy/adb are not installed. Run 'rex setup' or open the app.");
        var read = await EncoderList.ReadAsync(context.Runner, tools.Scrcpy, target).ConfigureAwait(false);
        return read.Ok
            ? EncoderList.ToJson(target, ConfigFile.Load(context.Paths.Config).Mirror, read.Encoders)
            : throw new InvalidOperationException("Could not read the phone's encoders: " + read.Error);
    }

    public static async Task<int> HumanAsync(string[] args, CliContext context)
    {
        var data = await RunAsync(args, context).ConfigureAwait(false);
        var chosen = data["chosen"]!.GetValue<string>();
        var codec = data["codec"]!.GetValue<string>();
        Console.WriteLine($"Video encoders on {data["serial"]!.GetValue<string>()}:");
        foreach (var encoder in data["encoders"]!.AsArray().OfType<JsonObject>())
        {
            var shown = new VideoEncoder(encoder["codec"]!.GetValue<string>(), encoder["name"]!.GetValue<string>(), encoder["kind"]!.GetValue<string>());
            var marks = shown.Name == chosen && shown.Codec == codec ? "  · chosen" : string.Empty;
            Console.WriteLine($"  {ScrcpyArguments.CodecName(shown.Codec),-6} {shown.Label}{marks}");
        }

        Console.WriteLine(chosen.Length == 0
            ? $"The phone chooses for {ScrcpyArguments.CodecName(codec)}. Pick one with: rex config set Mirror.VideoEncoder <name>"
            : "Let the phone choose again with: rex config set Mirror.VideoEncoder \"\"");
        return 0;
    }

    public static async Task<MachineResult> MachineAsync(string[] args, CliContext context) =>
        MachineMode.Success("encoders", await RunAsync(args, context).ConfigureAwait(false));
}
