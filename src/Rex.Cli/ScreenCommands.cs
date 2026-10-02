using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex screen: open an app on a second screen, switch its app, close it, or say what it is doing.
/// The second screen lives in the desktop app's window, so these need the app.
/// </summary>
public static class ScreenCommands
{
    public const string Usage = "rex screen [open <app> [--instead|--beside] [--size follow|phone|720p|1080p|1440p|WxH] [--fresh] | app <app> | close]";

    private static IpcRequest Request(string[] args)
    {
        var positional = Arguments.Positional(args);
        var verb = positional.Length >= 2 ? positional[1].ToLowerInvariant() : string.Empty;
        var request = new Dictionary<string, string> { ["verb"] = verb };
        if (verb is "open" or "app")
        {
            Arguments.Require(positional, 3, Usage);
            request["app"] = string.Join(' ', positional[2..]);
        }
        else if (verb is not ("" or "close"))
        {
            throw new ArgumentException("Usage: " + Usage);
        }

        if (args.Contains("--instead", StringComparer.OrdinalIgnoreCase))
        {
            request["placement"] = "instead";
        }
        else if (args.Contains("--beside", StringComparer.OrdinalIgnoreCase))
        {
            request["placement"] = "beside";
        }

        if (Arguments.Option(args, "--size") is { } size)
        {
            request["size"] = ScreenSize.Parse(size) is null ? throw new ArgumentException(ScreenSize.Usage) : size;
        }

        if (args.Contains("--fresh", StringComparer.OrdinalIgnoreCase))
        {
            request["fresh"] = "true";
        }

        return new IpcRequest("screen", request);
    }

    public static async Task<int> RunAsync(string[] args, CliContext context)
    {
        var response = await context.Ipc.SendAsync(Request(args), TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (response is null)
        {
            Console.WriteLine("The app is not running. Start it with 'rex open'.");
            return 1;
        }

        Console.WriteLine(response.Ok ? response.Data!["words"]!.GetValue<string>() : response.Error);
        return response.Ok ? 0 : 1;
    }

    public static async Task<MachineResult> MachineAsync(string[] args, CliContext context)
    {
        var response = await context.Ipc.SendAsync(Request(args), TimeSpan.FromSeconds(60)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The app is not running. Start it with 'rex open'.");
        return response.Ok ? MachineMode.Success("screen", response.Data) : throw new InvalidOperationException(response.Error);
    }
}
