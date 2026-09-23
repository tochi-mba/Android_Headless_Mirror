using System.Diagnostics;
using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

/// <summary>Human-readable commands. Each one is a thin wrapper over Core or the running app.</summary>
public static class Commands
{
    public static async Task<int> RunAsync(string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        var command = positional.Length == 0 ? "help" : positional[0].ToLowerInvariant();

        switch (command)
        {
            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;

            case "open":
            case "start":
                return await OpenAppAsync(context).ConfigureAwait(false);

            case "stop":
                return await StopAsync(context).ConfigureAwait(false);

            case "quit":
                return await SendAsync(context, new IpcRequest("quit"), "The app is not running.").ConfigureAwait(false);

            case "status":
                return await StatusAsync(context).ConfigureAwait(false);

            case "devices":
            {
                var devices = await context.RequireAdb().ListDevicesAsync().ConfigureAwait(false);
                PrintDevices(devices);
                return 0;
            }

            case "diagnostics":
            {
                var report = await Diagnostics.BuildAsync(context.Paths, context.Log, context.Runner).ConfigureAwait(false);
                Console.WriteLine(report.ToText());
                return 0;
            }

            case "setup":
            {
                var lastStage = string.Empty;
                var progress = new Progress<InstallProgress>(p =>
                {
                    if (p.Stage != lastStage)
                    {
                        lastStage = p.Stage;
                        Console.WriteLine("  " + p.Stage);
                    }
                });
                var tools = await new ScrcpyInstaller().InstallLatestAsync(context.Paths, progress, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"Installed scrcpy {tools.Version}.");
                return 0;
            }

            case "action":
                Arguments.Require(positional, 2, "rex action <name> [--serial S]   (rex action list shows the names)");
                if (positional[1] == "list")
                {
                    PrintActions();
                    return 0;
                }

                return await ActionAsync(context, positional[1], Arguments.Option(args, "--serial")).ConfigureAwait(false);

            case "zoom":
                Arguments.Require(positional, 2, "rex zoom <in|out|reset>");
                return await SendAsync(context, new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = positional[1] }), "The mirror is not open.").ConfigureAwait(false);

            case "screenshot":
                return await ScreenshotAsync(context, Arguments.Option(args, "--serial")).ConfigureAwait(false);

            case "phone":
                return await PhoneAsync(context, positional, Arguments.Option(args, "--serial")).ConfigureAwait(false);

            case "android":
                return await AndroidAsync(context, positional, Arguments.Option(args, "--serial"), Arguments.Option(args, "--filter")).ConfigureAwait(false);

            case "config":
                return ConfigCommand(context, positional);

            case "autostart":
                Arguments.Require(positional, 2, "rex autostart <on|off>");
                return Autostart(context, positional[1]);

            case "lock-mode":
            {
                Arguments.Require(positional, 3, "rex lock-mode <serial> <pattern|other|none>");
                var mode = positional[2].ToLowerInvariant();
                if (!LockScreenModes.IsValid(mode))
                {
                    throw new ArgumentException("lock-mode expects pattern, other or none.");
                }

                new StateStore(context.Paths.State).SetLockScreenMode(positional[1], mode);
                Console.WriteLine($"Saved lock type '{mode}' for {positional[1]}.");
                return 0;
            }

            case "reset-lock":
            {
                var target = positional.Length >= 2 ? positional[1] : "ALL";
                var count = new StateStore(context.Paths.State).ResetLockScreen(target);
                Console.WriteLine($"Cleared lock-screen answers for {count} phone(s).");
                return 0;
            }

            default:
                throw new ArgumentException($"Unknown command '{command}'. Run 'rex help'.");
        }
    }

    private static async Task<int> OpenAppAsync(CliContext context)
    {
        if (await context.Ipc.IsAppRunningAsync().ConfigureAwait(false))
        {
            await context.Ipc.SendAsync(new IpcRequest("show")).ConfigureAwait(false);
            Console.WriteLine("Android Headless Mirror is already running; brought it to the front.");
            return 0;
        }

        if (!File.Exists(context.AppExecutable))
        {
            throw new InvalidOperationException($"RexMirror.exe was not found next to rex.exe ({context.AppExecutable}).");
        }

        Process.Start(new ProcessStartInfo(context.AppExecutable) { UseShellExecute = true, WorkingDirectory = context.Paths.Root });
        Console.WriteLine("Android Headless Mirror started.");
        return 0;
    }

    private static async Task<int> StopAsync(CliContext context)
    {
        var response = await context.Ipc.SendAsync(new IpcRequest("session-stop")).ConfigureAwait(false);
        Console.WriteLine(response is null ? "The app is not running." : response.Ok ? "Mirror stopped. The app keeps waiting; use 'rex quit' to exit it." : response.Error);
        return response is null or { Ok: false } ? 1 : 0;
    }

    private static async Task<int> StatusAsync(CliContext context)
    {
        var response = await context.Ipc.SendAsync(new IpcRequest("status")).ConfigureAwait(false);
        if (response is { Ok: true, Data: JsonObject data })
        {
            Console.WriteLine($"App:      running ({data["phase"]})");
            Console.WriteLine($"Message:  {data["message"]}");
            if (data["device"] is JsonObject device)
            {
                Console.WriteLine($"Phone:    {device["name"]} · {device["serial"]} · {device["transport"]} · Android {device["android"]}" + (device["battery"] is null ? string.Empty : $" · {device["battery"]}%"));
            }

            Console.WriteLine($"Zoom:     {data["zoom"]}");
            return 0;
        }

        Console.WriteLine("App:      not running (rex open starts it)");
        var adb = context.Adb();
        if (adb is null)
        {
            Console.WriteLine("Tools:    scrcpy is not installed (rex setup, or open the app)");
            return 0;
        }

        PrintDevices(await adb.ListDevicesAsync().ConfigureAwait(false));
        return 0;
    }

    private static async Task<int> ActionAsync(CliContext context, string id, string? serial)
    {
        var action = MirrorActions.Find(id) ?? throw new ArgumentException($"Unknown action '{id}'. Run 'rex action list'.");
        if (action.Kind == ActionKind.Adb)
        {
            var adb = context.RequireAdb();
            var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
            var command = MirrorActions.AdbCommand(action.Id)!.Value;
            var result = command.Kind == "key"
                ? await adb.KeyEventAsync(target, command.Argument).ConfigureAwait(false)
                : command.Kind == "rotation"
                ? await adb.SetRotationOverrideAsync(target, command.Argument).ConfigureAwait(false)
                : await adb.StatusBarAsync(target, command.Argument).ConfigureAwait(false);
            Console.WriteLine(result.Ok ? action.Label : result.Text);
            return result.Ok ? 0 : 1;
        }

        return await SendAsync(context, new IpcRequest("action", new Dictionary<string, string> { ["name"] = action.Id }), "The mirror is not open.").ConfigureAwait(false);
    }

    private static async Task<int> ScreenshotAsync(CliContext context, string? serial)
    {
        var adb = context.RequireAdb();
        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        var bytes = await adb.ScreencapAsync(target).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The phone did not return a screenshot.");
        var directory = context.Paths.ScreenshotFolder(context.Config.Load().App.ScreenshotDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "android-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".png");
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        Console.WriteLine(path);
        return 0;
    }

    private static async Task<int> PhoneAsync(CliContext context, string[] positional, string? serial)
    {
        Arguments.Require(positional, 2, "rex phone <get|set> ...");
        var adb = context.RequireAdb();
        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);

        if (positional[1] == "get")
        {
            foreach (var pair in await adb.GetFriendlyStateAsync(target).ConfigureAwait(false))
            {
                Console.WriteLine($"{pair.Key,-18} {pair.Value}");
            }

            return 0;
        }

        if (positional[1] == "set")
        {
            Arguments.Require(positional, 4, "rex phone set <setting> <value> [--serial S]   (settings: " + string.Join(", ", FriendlySettings.Ids) + ")");
            var result = positional[2] == "rotation"
                ? await adb.SetRotationOverrideAsync(target, positional[3]).ConfigureAwait(false)
                : await adb.ApplyFriendlySettingAsync(target, positional[2], positional[3]).ConfigureAwait(false);
            Console.WriteLine(result.Ok ? (string.IsNullOrWhiteSpace(result.Text) ? "Done." : result.Text) : result.Text);
            return result.Ok ? 0 : 1;
        }

        throw new ArgumentException("phone expects get or set.");
    }

    private static async Task<int> AndroidAsync(CliContext context, string[] positional, string? serial, string? filter)
    {
        Arguments.Require(positional, 3, "rex android <list|get|set|delete> <system|secure|global> [key] [value] [--serial S] [--filter text]");
        var adb = context.RequireAdb();
        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        var ns = positional[2].ToLowerInvariant();
        if (!AndroidSettings.IsValidNamespace(ns))
        {
            throw new ArgumentException("Namespace must be system, secure or global.");
        }

        switch (positional[1].ToLowerInvariant())
        {
            case "list":
            {
                var (ok, error, rows) = await adb.ListSettingsAsync(target, ns).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException(error);
                }

                foreach (var row in rows.Where(r => string.IsNullOrEmpty(filter) || r.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"{row.Key,-44} {row.Value,-24} {row.Risk}");
                }

                return 0;
            }

            case "get":
            {
                Arguments.Require(positional, 4, "rex android get <namespace> <key>");
                var result = await adb.GetSettingAsync(target, ns, positional[3]).ConfigureAwait(false);
                Console.WriteLine(result.Text);
                return result.Ok ? 0 : 1;
            }

            case "set":
            {
                Arguments.Require(positional, 5, "rex android set <namespace> <key> <value>");
                var result = await adb.PutSettingAsync(target, ns, positional[3], positional[4]).ConfigureAwait(false);
                Console.WriteLine(result.Ok ? "Updated." : result.Text);
                return result.Ok ? 0 : 1;
            }

            case "delete":
            {
                Arguments.Require(positional, 4, "rex android delete <namespace> <key>");
                var result = await adb.DeleteSettingAsync(target, ns, positional[3]).ConfigureAwait(false);
                Console.WriteLine(result.Ok ? "Deleted." : result.Text);
                return result.Ok ? 0 : 1;
            }

            default:
                throw new ArgumentException("android expects list, get, set or delete.");
        }
    }

    private static int ConfigCommand(CliContext context, string[] positional)
    {
        Arguments.Require(positional, 2, "rex config <list|get|set|restore> ...");
        switch (positional[1].ToLowerInvariant())
        {
            case "list":
                foreach (var leaf in context.Config.Flatten())
                {
                    Console.WriteLine($"{leaf.Path,-40} {leaf.Value}");
                }

                return 0;
            case "get":
                Arguments.Require(positional, 3, "rex config get <path>");
                Console.WriteLine(context.Config.Get(positional[2]).Value);
                return 0;
            case "set":
            {
                Arguments.Require(positional, 4, "rex config set <path> <value>");
                var leaf = context.Config.Set(positional[2], positional[3]);
                Console.WriteLine($"{leaf.Path} = {leaf.Value}");
                return 0;
            }

            case "restore":
                Console.WriteLine(context.Config.RestoreBackup() ? "Previous configuration restored." : "There is no backup to restore.");
                return 0;
            default:
                throw new ArgumentException("config expects list, get, set or restore.");
        }
    }

    private static int Autostart(CliContext context, string verb)
    {
        switch (verb.ToLowerInvariant())
        {
            case "on":
                StartupRegistration.Enable(context.AppExecutable);
                Console.WriteLine("Android Headless Mirror will start with Windows.");
                return 0;
            case "off":
                StartupRegistration.Disable();
                Console.WriteLine("Android Headless Mirror will no longer start with Windows.");
                return 0;
            default:
                throw new ArgumentException("autostart expects on or off.");
        }
    }

    private static async Task<int> SendAsync(CliContext context, IpcRequest request, string notRunning)
    {
        var response = await context.Ipc.SendAsync(request).ConfigureAwait(false);
        if (response is null)
        {
            Console.WriteLine(notRunning);
            return 1;
        }

        if (!response.Ok)
        {
            Console.WriteLine(response.Error);
            return 1;
        }

        var text = response.Data is JsonObject data && data["text"] is JsonNode node ? node.ToString() : "Done.";
        Console.WriteLine(text);
        return 0;
    }

    private static void PrintDevices(IReadOnlyList<AdbDevice> devices)
    {
        if (devices.Count == 0)
        {
            Console.WriteLine("Devices:  none detected");
            return;
        }

        foreach (var device in devices)
        {
            Console.WriteLine($"Device:   {device.Serial,-22} {device.State,-14} {device.Transport}" + (device.Model.Length > 0 ? "  " + device.Model : string.Empty));
        }
    }

    private static void PrintActions()
    {
        foreach (var action in MirrorActions.All)
        {
            Console.WriteLine($"{action.Id,-16} {action.Label,-18} {action.Detail}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            REX · Android Headless Mirror command line

              rex open                          Open the app (or bring it to the front)
              rex status                        App, mirror and phone state
              rex devices                       Phones visible to ADB
              rex stop | rex quit               Stop the mirror | exit the app
              rex action <name> [--serial S]    Send an action (rex action list)
              rex zoom <in|out|reset>           PC-side zoom of the open mirror
              rex screenshot [--serial S]       Save a PNG of the phone screen
              rex phone get|set ...             Friendly phone settings (brightness, rotation, dark-mode...)
              rex android list|get|set|delete   Raw Android settings provider keys
              rex config list|get|set|restore   App settings (config.json)
              rex autostart on|off              Start with Windows
              rex lock-mode <serial> <mode>     pattern | other | none
              rex reset-lock [serial|ALL]       Forget lock-screen answers
              rex setup                         Install scrcpy without the app
              rex diagnostics                   Full report

            Machine mode (one JSON document, never prompts): rex agent <command>, rex --json <command>
            """);
    }
}
