using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// rex app: list the phone's apps, open one by name or package, close one, open its info page, or
/// star it. While the desktop app runs, listing and opening go through it, so its rules hold (one
/// scrcpy server starting at a time, the recent list); without it they go straight to the phone.
/// </summary>
public static class AppCommands
{
    public const string ListUsage = "rex app list [search] [--system] [--serial S]";
    public const string OpenUsage = "rex app open <name|package> [--fresh] [--serial S]";
    public const string ChoreUsage = "rex app close|info <package> [--serial S]";
    public const string FavouriteUsage = "rex app favourite <package> on|off [--serial S]";

    private static string Verb(string[] positional) => positional.Length >= 2 ? positional[1].ToLowerInvariant() : "list";

    private static bool Has(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    // ----- Human -----

    public static async Task<int> RunAsync(string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        var serial = Arguments.Option(args, "--serial");
        switch (Verb(positional))
        {
            case "list":
            {
                var apps = await ListAsync(context, serial, positional.Length >= 3 ? positional[2] : null, Has(args, "--system")).ConfigureAwait(false);
                if (apps.Count == 0)
                {
                    Console.WriteLine("No app matches that.");
                    return 0;
                }

                foreach (var app in apps)
                {
                    Console.WriteLine($"  {app.Name,-30} {app.Package}" + (app.System ? "  (system)" : string.Empty));
                }

                return 0;
            }

            case "open":
            {
                Arguments.Require(positional, 3, OpenUsage);
                var (app, result) = await OpenAsync(context, serial, string.Join(' ', positional[2..]), Has(args, "--fresh")).ConfigureAwait(false);
                Console.WriteLine(result.Ok ? $"Opened {app!.Name}." : result.Text);
                return result.Ok ? 0 : 1;
            }

            case "close":
            case "info":
            {
                Arguments.Require(positional, 3, ChoreUsage);
                var result = await ChoreAsync(context, serial, positional[1].ToLowerInvariant(), positional[2]).ConfigureAwait(false);
                Console.WriteLine(result.Ok ? (positional[1] == "close" ? $"Closed {positional[2]}." : $"{positional[2]}'s info is open on the phone.") : result.Text);
                return result.Ok ? 0 : 1;
            }

            case "favourite":
            {
                Arguments.Require(positional, 4, FavouriteUsage);
                var on = OnOff(positional[3]);
                await FavouriteAsync(context, serial, positional[2], on).ConfigureAwait(false);
                Console.WriteLine(on ? $"{positional[2]} is a favourite." : $"{positional[2]} is not a favourite any more.");
                return 0;
            }

            default:
                throw new ArgumentException($"Usage: {ListUsage} | {OpenUsage} | {ChoreUsage} | {FavouriteUsage}");
        }
    }

    // ----- Machine -----

    public static async Task<MachineResult> MachineAsync(CliContext context, string[] args)
    {
        var positional = Arguments.Positional(args);
        var serial = Arguments.Option(args, "--serial");
        switch (Verb(positional))
        {
            case "list":
            {
                var apps = await ListAsync(context, serial, positional.Length >= 3 ? positional[2] : null, Has(args, "--system")).ConfigureAwait(false);
                return MachineMode.Success("app", new JsonObject
                {
                    ["apps"] = new JsonArray([.. apps.Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["package"] = a.Package, ["system"] = a.System })]),
                });
            }

            case "open":
            {
                Arguments.Require(positional, 3, OpenUsage);
                var (app, result) = await OpenAsync(context, serial, string.Join(' ', positional[2..]), Has(args, "--fresh")).ConfigureAwait(false);
                return result.Ok
                    ? MachineMode.Success("app", new JsonObject { ["opened"] = app!.Package, ["name"] = app.Name })
                    : throw new InvalidOperationException(result.Text);
            }

            case "close":
            case "info":
            {
                Arguments.Require(positional, 3, ChoreUsage);
                var result = await ChoreAsync(context, serial, positional[1].ToLowerInvariant(), positional[2]).ConfigureAwait(false);
                return result.Ok
                    ? MachineMode.Success("app", new JsonObject { ["package"] = positional[2], ["done"] = positional[1].ToLowerInvariant() })
                    : throw new InvalidOperationException(result.Text);
            }

            case "favourite":
            {
                Arguments.Require(positional, 4, FavouriteUsage);
                var on = OnOff(positional[3]);
                await FavouriteAsync(context, serial, positional[2], on).ConfigureAwait(false);
                return MachineMode.Success("app", new JsonObject { ["package"] = positional[2], ["favourite"] = on });
            }

            default:
                throw new ArgumentException($"Usage: {ListUsage} | {OpenUsage} | {ChoreUsage} | {FavouriteUsage}");
        }
    }

    // ----- Shared -----

    private static bool OnOff(string value) => value.ToLowerInvariant() switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException("Usage: " + FavouriteUsage),
    };

    /// <summary>
    /// The running app's answer when it shows the phone named (it does nothing for another phone),
    /// else null: the app is not running, shows no phone, or shows a different one.
    /// </summary>
    private static async Task<JsonObject?> AskAppAsync(CliContext context, string? serial, IpcRequest request)
    {
        if (serial is not null)
        {
            request = request with { Args = new Dictionary<string, string>(request.Args) { ["serial"] = serial } };
        }

        var response = await context.Ipc.SendAsync(request).ConfigureAwait(false);
        return response is { Ok: true, Data: JsonObject data } && data["serial"] is not null ? data : null;
    }

    /// <summary>The phone's apps by name, all of them or those matching a search; system apps only when asked.</summary>
    private static async Task<IReadOnlyList<PhoneApp>> ListAsync(CliContext context, string? serial, string? search, bool system)
    {
        IReadOnlyList<PhoneApp> apps;
        var request = new IpcRequest("apps", new Dictionary<string, string> { ["read"] = "true", ["search"] = search ?? string.Empty });
        if (await AskAppAsync(context, serial, request).ConfigureAwait(false) is { } answer)
        {
            apps = answer["apps"]!.AsArray().Select(a => new PhoneApp(a!["name"]!.GetValue<string>(), a["package"]!.GetValue<string>(), a["system"]!.GetValue<bool>())).ToArray();
        }
        else
        {
            var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
            var read = await ReadAsync(context, target).ConfigureAwait(false);
            apps = AppList.Search(AppOrder.Sort(read, [], "name"), search);
        }

        // A search reaches the phone's own apps; a plain list leaves them out unless asked.
        return string.IsNullOrWhiteSpace(search) && !system ? apps.Where(a => !a.System).ToArray() : apps;
    }

    /// <summary>Reads the phone's apps with scrcpy and remembers them, as the app does.</summary>
    private static async Task<IReadOnlyList<PhoneApp>> ReadAsync(CliContext context, string serial)
    {
        var tools = ToolLocator.Find(context.Paths) ?? throw new InvalidOperationException("scrcpy/adb are not installed. Run 'rex setup' or open the app.");
        var read = await AppLister.ReadAsync(context.Runner, tools.Scrcpy, serial).ConfigureAwait(false);
        if (!read.Ok)
        {
            throw new InvalidOperationException("Could not read the phone's apps: " + read.Error);
        }

        new StateStore(context.Paths.State).SetApps(serial, read.Apps, DateTimeOffset.UtcNow);
        return read.Apps;
    }

    /// <summary>Opens an app by name or package: through the app when it runs, else straight on the phone.</summary>
    private static async Task<(PhoneApp? App, AndroidResult Result)> OpenAsync(CliContext context, string? serial, string wanted, bool fresh)
    {
        var request = new IpcRequest("open-app", new Dictionary<string, string> { ["app"] = wanted, ["fresh"] = fresh ? "true" : "false" });
        var response = await context.Ipc.SendAsync(request).ConfigureAwait(false);
        if (response is not null && serial is null)
        {
            return response is { Ok: true, Data: JsonObject opened }
                ? (new PhoneApp(opened["name"]!.GetValue<string>(), opened["package"]!.GetValue<string>(), false), AndroidResult.Success())
                : (null, AndroidResult.Failure(response.Error ?? "The app could not open it."));
        }

        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        var state = new StateStore(context.Paths.State);
        var known = state.GetDevice(target)?.Apps;
        // A package is opened as it is, even one the remembered list does not have yet.
        var app = PackageName.IsValid(wanted) ? known?.FirstOrDefault(a => a.Package == wanted) ?? new PhoneApp(wanted, wanted, false) : null;
        if (app is null)
        {
            var match = AppList.Resolve(known ?? await ReadAsync(context, target).ConfigureAwait(false), wanted);
            if (match.App is null)
            {
                return (null, AndroidResult.Failure(match.Error!));
            }

            app = match.App;
        }

        var result = await context.RequireAdb().LaunchAppAsync(target, app.Package, fresh).ConfigureAwait(false);
        if (result.Ok)
        {
            state.NoteAppOpened(target, app.Package, DateTimeOffset.UtcNow, context.Config.Load().Apps.RecentCount);
        }

        return (app, result);
    }

    private static async Task<AndroidResult> ChoreAsync(CliContext context, string? serial, string verb, string package)
    {
        var adb = context.RequireAdb();
        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        return verb == "close"
            ? await adb.ForceStopAsync(target, package).ConfigureAwait(false)
            : await adb.OpenAppInfoAsync(target, package).ConfigureAwait(false);
    }

    /// <summary>Stars an app or takes its star away: through the app when it runs, so it shows at once.</summary>
    private static async Task FavouriteAsync(CliContext context, string? serial, string package, bool on)
    {
        if (!PackageName.IsValid(package))
        {
            throw new ArgumentException($"\"{package}\" is not an app's package name.");
        }

        var request = new IpcRequest("apps", new Dictionary<string, string> { ["favourite"] = package, ["on"] = on ? "true" : "false" });
        if (await AskAppAsync(context, serial, request).ConfigureAwait(false) is not null)
        {
            return;
        }

        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        new StateStore(context.Paths.State).SetFavourite(target, package, on);
    }
}
