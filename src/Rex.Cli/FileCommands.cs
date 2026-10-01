using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>Pushes files and installs APKs without needing the desktop app.</summary>
public static class FileCommands
{
    public const string PushUsage = "rex push <files or folders…> [--to /sdcard/folder/] [--serial S]";
    public const string InstallUsage = "rex install <apk…> [--downgrade] [--grant] [--test] [--no-replace] [--serial S]";

    public static async Task<int> PushAsync(string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        Arguments.Require(positional, 2, PushUsage);
        var folder = Arguments.Option(args, "--to") ?? context.Config.Load().Transfer.Folder;
        if (TransferSettings.WhyNotFolder(folder) is { } why) throw new ArgumentException(why);
        if (!folder.EndsWith('/')) folder += "/";
        var serial = await context.ResolveSerialAsync(Arguments.Option(args, "--serial")).ConfigureAwait(false);
        var adb = context.RequireAdb();
        var failed = false;
        foreach (var path in positional[1..])
        {
            var entry = LocalEntry.Read(path) ?? throw new FileNotFoundException("No file or folder exists at " + path, path);
            var result = await adb.PushAsync(serial, entry.Path, folder, entry.Size).ConfigureAwait(false);
            Console.WriteLine(result.Ok ? $"Sent {entry.Name} to {folder}." : $"Could not send {entry.Name}: {TransferProgress.Why(result.FailureText)}");
            failed |= !result.Ok;
        }

        return failed ? 1 : 0;
    }

    public static async Task<int> InstallAsync(string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        Arguments.Require(positional, 2, InstallUsage);
        var serial = await context.ResolveSerialAsync(Arguments.Option(args, "--serial")).ConfigureAwait(false);
        var adb = context.RequireAdb();
        var flags = new InstallFlags(!args.Contains("--no-replace", StringComparer.OrdinalIgnoreCase),
            args.Contains("--downgrade", StringComparer.OrdinalIgnoreCase), args.Contains("--grant", StringComparer.OrdinalIgnoreCase),
            args.Contains("--test", StringComparer.OrdinalIgnoreCase));
        var failed = false;
        foreach (var path in positional[1..])
        {
            if (!File.Exists(path) || !TransferPlan.IsApk(path)) throw new FileNotFoundException("An APK file is required: " + path, path);
            var result = await adb.InstallAsync(serial, path, flags).ConfigureAwait(false);
            Console.WriteLine(result.Ok ? $"Installed {Path.GetFileName(path)}." : $"Could not install {Path.GetFileName(path)}: {TransferProgress.Why(result.Text)}");
            failed |= !result.Ok;
        }

        return failed ? 1 : 0;
    }

    public static async Task<MachineResult> MachineAsync(string command, string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        Arguments.Require(positional, 2, command == "push" ? PushUsage : InstallUsage);
        var serial = await context.ResolveSerialAsync(Arguments.Option(args, "--serial")).ConfigureAwait(false);
        var adb = context.RequireAdb();
        var results = new JsonArray();
        var ok = true;
        if (command == "push")
        {
            var folder = Arguments.Option(args, "--to") ?? context.Config.Load().Transfer.Folder;
            if (TransferSettings.WhyNotFolder(folder) is { } why) throw new ArgumentException(why);
            if (!folder.EndsWith('/')) folder += "/";
            foreach (var path in positional[1..])
            {
                var entry = LocalEntry.Read(path) ?? throw new FileNotFoundException("No file or folder exists at " + path, path);
                var result = await adb.PushAsync(serial, path, folder, entry.Size).ConfigureAwait(false);
                ok &= result.Ok;
                results.Add(new JsonObject { ["path"] = path, ["ok"] = result.Ok, ["to"] = folder, ["error"] = result.Ok ? null : TransferProgress.Why(result.FailureText) });
            }
        }
        else
        {
            var flags = new InstallFlags(!args.Contains("--no-replace", StringComparer.OrdinalIgnoreCase),
                args.Contains("--downgrade", StringComparer.OrdinalIgnoreCase), args.Contains("--grant", StringComparer.OrdinalIgnoreCase),
                args.Contains("--test", StringComparer.OrdinalIgnoreCase));
            foreach (var path in positional[1..])
            {
                if (!File.Exists(path) || !TransferPlan.IsApk(path)) throw new FileNotFoundException("An APK file is required: " + path, path);
                var result = await adb.InstallAsync(serial, path, flags).ConfigureAwait(false);
                ok &= result.Ok;
                results.Add(new JsonObject { ["path"] = path, ["ok"] = result.Ok, ["error"] = result.Ok ? null : TransferProgress.Why(result.Text) });
            }
        }

        var data = new JsonObject { ["serial"] = serial, ["items"] = results };
        return ok ? MachineMode.Success(command, data) : new MachineResult(1, MachineMode.FailureJson(command, new InvalidOperationException("One or more files failed.")));
    }
}
