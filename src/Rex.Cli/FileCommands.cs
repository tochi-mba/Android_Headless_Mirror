using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>Pushes files and installs APKs without needing the desktop app.</summary>
public static class FileCommands
{
    public const string PushUsage = "rex push <files or folders…> [--to /sdcard/folder/] [--serial S]";
    public const string InstallUsage = "rex install <apk…> [--downgrade] [--grant] [--test] [--no-replace] [--serial S]";

    /// <summary>What happened to one file: where it went, or why it did not.</summary>
    private sealed record Sent(string Path, string Name, bool Ok, string? To, string? Error);

    public static async Task<int> PushAsync(string[] args, CliContext context)
    {
        var (_, sent) = await RunAsync("push", args, context).ConfigureAwait(false);
        foreach (var file in sent)
        {
            Console.WriteLine(file.Ok ? $"Sent {file.Name} to {file.To}." : $"Could not send {file.Name}: {file.Error}");
        }

        return sent.All(file => file.Ok) ? 0 : 1;
    }

    public static async Task<int> InstallAsync(string[] args, CliContext context)
    {
        var (_, sent) = await RunAsync("install", args, context).ConfigureAwait(false);
        foreach (var file in sent)
        {
            Console.WriteLine(file.Ok ? $"Installed {file.Name}." : $"Could not install {file.Name}: {file.Error}");
        }

        return sent.All(file => file.Ok) ? 0 : 1;
    }

    public static async Task<MachineResult> MachineAsync(string command, string[] args, CliContext context)
    {
        var (serial, sent) = await RunAsync(command, args, context).ConfigureAwait(false);
        var data = new JsonObject
        {
            ["serial"] = serial,
            ["items"] = new JsonArray([.. sent.Select(file => (JsonNode)new JsonObject
            {
                ["path"] = file.Path,
                ["ok"] = file.Ok,
                ["to"] = file.To,
                ["error"] = file.Error,
            })]),
        };
        return sent.All(file => file.Ok)
            ? MachineMode.Success(command, data)
            : new MachineResult(1, MachineMode.FailureJson(command, new InvalidOperationException("One or more files failed.")));
    }

    /// <summary>
    /// Sends or installs each file named, one after another, and says how each went. Every path is
    /// checked before anything is sent, so a typo never leaves half a batch on the phone.
    /// </summary>
    private static async Task<(string Serial, IReadOnlyList<Sent> Sent)> RunAsync(string command, string[] args, CliContext context)
    {
        var install = command == "install";
        var positional = Arguments.Positional(args);
        Arguments.Require(positional, 2, install ? InstallUsage : PushUsage);
        var folder = install ? null : Arguments.Option(args, "--to") ?? context.Config.Load().Transfer.Folder;
        if (folder is not null)
        {
            if (TransferSettings.WhyNotFolder(folder) is { } why)
            {
                throw new ArgumentException(why);
            }

            folder = folder.EndsWith('/') ? folder : folder + "/";
        }

        var entries = positional[1..].Select(path => install
            ? File.Exists(path) && TransferPlan.IsApk(path) ? LocalEntry.Read(path)! : throw new FileNotFoundException("An APK file is required: " + path, path)
            : LocalEntry.Read(path) ?? throw new FileNotFoundException("No file or folder exists at " + path, path)).ToArray();
        var serial = await context.ResolveSerialAsync(Arguments.Option(args, "--serial")).ConfigureAwait(false);
        var adb = context.RequireAdb();
        var flags = new InstallFlags(!Has(args, "--no-replace"), Has(args, "--downgrade"), Has(args, "--grant"), Has(args, "--test"));
        var sent = new List<Sent>(entries.Length);
        foreach (var entry in entries)
        {
            if (install)
            {
                var result = await adb.InstallAsync(serial, entry.Path, flags).ConfigureAwait(false);
                sent.Add(new Sent(entry.Path, entry.Name, result.Ok, null, result.Ok ? null : TransferProgress.Why(result.Text)));
            }
            else
            {
                var result = await adb.PushAsync(serial, entry.Path, folder!, entry.Size).ConfigureAwait(false);
                sent.Add(new Sent(entry.Path, entry.Name, result.Ok, folder, result.Ok ? null : TransferProgress.Why(result.FailureText)));
            }
        }

        return (serial, sent);
    }

    private static bool Has(string[] args, string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
