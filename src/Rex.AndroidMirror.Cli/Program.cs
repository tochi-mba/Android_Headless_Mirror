using System.Text.Json;
using Spectre.Console;

namespace Rex.AndroidMirror.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Android Headless Mirror REX CLI currently supports Windows only.");
            return 2;
        }

        try
        {
            var paths = AppPaths.Discover();
            var runner = new ProcessRunner();
            var bridge = new BridgeClient(paths, runner);
            var config = new ConfigStore(paths.Config);

            if (
                args.Length > 0 &&
                args[0].Equals("--json", StringComparison.OrdinalIgnoreCase)
            )
            {
                var machineArgs = args.Skip(1).ToArray();
                var result = await MachineMode.RunAsync(
                    machineArgs,
                    paths,
                    runner,
                    bridge,
                    config);

                Console.Out.WriteLine(result.Json);
                return result.ExitCode;
            }

            if (args.Length == 0 || args[0].Equals("tui", StringComparison.OrdinalIgnoreCase) || args[0].Equals("wizard", StringComparison.OrdinalIgnoreCase))
            {
                return await new RexApp(paths, runner, bridge, config).RunAsync();
            }

            return await RunCommandAsync(args, paths, runner, bridge, config);
        }
        catch (Exception ex)
        {
            RexBrand.Error(ex.Message);
            return 1;
        }
    }

    internal static async Task<int> RunCommandAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config)
    {
        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;

            case "status":
                PrintStatus(await bridge.GetStatusAsync());
                return 0;

            case "devices":
            {
                var devices = await bridge.GetDevicesAsync();
                PrintDevices(devices);
                return 0;
            }

            case "start":
                if (File.Exists(paths.StopFlag))
                    File.Delete(paths.StopFlag);
                await runner.OpenAsync(paths.StartBatch, paths.Root);
                RexBrand.Success("Start requested.");
                return 0;

            case "stop":
                return await RunScriptCommandAsync(runner, paths.Stop);

            case "setup":
                return await RunScriptCommandAsync(
                    runner,
                    paths.Setup,
                    args.Contains("--skip-autostart", StringComparer.OrdinalIgnoreCase)
                        ? new[] { "-SkipAutostart" }
                        : null);

            case "repair":
            {
                var before = await bridge.GetStatusAsync();
                var result = await runner.RunPowerShellAsync(paths.Setup, new[] { "-SkipAutostart" });
                if (!result.Ok)
                    return PrintProcessFailure(result);

                return await RunScriptCommandAsync(
                    runner,
                    before.AutostartEnabled ? paths.InstallAutostart : paths.RemoveAutostart);
            }

            case "diagnostics":
                return await RunScriptCommandAsync(runner, paths.Diagnostics);

            case "autostart":
            {
                Require(args, 2, "rex autostart <on|off>");
                return args[1].ToLowerInvariant() switch
                {
                    "on" or "enable" => await RunScriptCommandAsync(runner, paths.InstallAutostart),
                    "off" or "disable" => await RunScriptCommandAsync(runner, paths.RemoveAutostart),
                    _ => throw new ArgumentException("autostart expects on or off.")
                };
            }

            case "controls":
            {
                var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
                var psArgs = new[]
                {
                    "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
                    "-File", paths.ControlCenter,
                    "-Serial", serial
                };

                _ = await runner.StartDetachedAsync(
                    "powershell.exe",
                    psArgs,
                    paths.Root);

                RexBrand.Success($"Opened Control Center for {serial}.");
                return 0;
            }

            case "action":
            {
                Require(args, 2, "rex action <name> [--serial SERIAL]");
                var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));

                using var result = await bridge.InvokeAsync(
                    "scrcpy-action",
                    serial: serial,
                    name: args[1]);

                PrintBridgeText(result, $"Sent {args[1]}.");
                return 0;
            }

            case "mirror":
            {
                Require(args, 2, "rex mirror <zoom-in|zoom-out|reset-zoom> [--serial SERIAL]");
                var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
                var name = args[1].ToLowerInvariant();
                if (name is not ("zoom-in" or "zoom-out" or "reset-zoom"))
                    throw new ArgumentException("mirror expects zoom-in, zoom-out, or reset-zoom.");

                using var result = await bridge.InvokeAsync(
                    "mirror-command",
                    serial: serial,
                    name: name);

                PrintBridgeText(result, $"Queued {name}.");
                return 0;
            }

            case "device":
                return await RunDeviceCommandAsync(args, bridge);

            case "android":
                return await RunAndroidCommandAsync(args, bridge);

            case "config":
                return RunConfigCommand(args, config);

            case "screenshot":
            {
                var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
                using var result = await bridge.InvokeAsync("screenshot", serial: serial);
                PrintBridgeText(result, "Screenshot saved.");
                return 0;
            }

            case "lock-mode":
            {
                Require(args, 3, "rex lock-mode <serial> <pattern|other|none>");
                var mode = args[2].ToLowerInvariant();
                if (mode is not ("pattern" or "other" or "none"))
                    throw new ArgumentException("lock-mode expects pattern, other, or none.");

                using var result = await bridge.InvokeAsync(
                    "set-lock-mode",
                    serial: args[1],
                    value: mode);

                RexBrand.Success($"Saved lock-screen mode {mode} for {args[1]}.");
                return 0;
            }

            case "reset-lock":
            {
                var serial = args.Length >= 2 ? args[1] : "ALL";
                return await RunScriptCommandAsync(
                    runner,
                    paths.ResetLockChoices,
                    new[] { "-Serial", serial });
            }

            case "captures":
                Directory.CreateDirectory(paths.Captures);
                await runner.OpenAsync(paths.Captures, paths.Root);
                return 0;

            default:
                throw new ArgumentException($"Unknown command '{args[0]}'. Run 'rex help'.");
        }
    }

    private static async Task<int> RunDeviceCommandAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 2, "rex device set <setting> <value> [--serial SERIAL]");

        if (!args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("device currently supports: set.");

        Require(args, 4, "rex device set <setting> <value> [--serial SERIAL]");
        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));

        if (args[2].Equals("animation-scale", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var id in new[] { "animation-window", "animation-transition", "animation-duration" })
            {
                using var _ = await bridge.InvokeAsync(
                    "friendly-set",
                    serial: serial,
                    name: id,
                    value: args[3]);
            }

            RexBrand.Success("Android animation scales updated.");
            return 0;
        }

        using var result = await bridge.InvokeAsync(
            "friendly-set",
            serial: serial,
            name: args[2],
            value: args[3]);

        PrintBridgeText(result, "Device setting updated.");
        return 0;
    }

    private static async Task<int> RunAndroidCommandAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 3, "rex android <list|get|set|delete> <namespace> ...");
        var verb = args[1].ToLowerInvariant();
        var ns = NormalizeNamespace(args[2]);
        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));

        switch (verb)
        {
            case "list":
            {
                var rows = await bridge.ListAndroidSettingsAsync(serial, ns);
                var filter = GetOption(args, "--filter");

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    rows = rows
                        .Where(x =>
                            x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(RexBrand.LineColor)
                    .AddColumn("Key")
                    .AddColumn("Value")
                    .AddColumn("Risk");

                foreach (var row in rows)
                {
                    table.AddRow(
                        Markup.Escape(row.Key),
                        Markup.Escape(row.Value),
                        Markup.Escape(row.Risk));
                }

                AnsiConsole.Write(table);
                return 0;
            }

            case "get":
            {
                Require(args, 4, "rex android get <namespace> <key> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-get",
                    serial: serial,
                    ns: ns,
                    key: args[3]);

                PrintBridgeText(result, string.Empty);
                return 0;
            }

            case "set":
            {
                Require(args, 5, "rex android set <namespace> <key> <value> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-set",
                    serial: serial,
                    ns: ns,
                    key: args[3],
                    value: args[4]);

                PrintBridgeText(result, "Android setting updated.");
                return 0;
            }

            case "delete":
            {
                Require(args, 4, "rex android delete <namespace> <key> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-delete",
                    serial: serial,
                    ns: ns,
                    key: args[3]);

                PrintBridgeText(result, "Android setting deleted.");
                return 0;
            }

            default:
                throw new ArgumentException("android expects list, get, set, or delete.");
        }
    }

    private static int RunConfigCommand(string[] args, ConfigStore config)
    {
        Require(args, 2, "rex config <list|get|set|restore> ...");
        var verb = args[1].ToLowerInvariant();

        switch (verb)
        {
            case "list":
            {
                var filter = GetOption(args, "--filter");
                var rows = config.Flatten()
                    .Where(x =>
                        string.IsNullOrWhiteSpace(filter) ||
                        x.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(RexBrand.LineColor)
                    .AddColumn("Setting")
                    .AddColumn("Value")
                    .AddColumn("Type");

                foreach (var row in rows)
                {
                    table.AddRow(
                        Markup.Escape(row.Path),
                        Markup.Escape(row.Value),
                        Markup.Escape(row.Kind.ToString()));
                }

                AnsiConsole.Write(table);
                return 0;
            }

            case "get":
            {
                Require(args, 3, "rex config get <path>");
                var leaf = config.Get(args[2]);
                AnsiConsole.MarkupLine(
                    $"[#D7FF3F]{Markup.Escape(leaf.Path)}[/] = {Markup.Escape(leaf.Value)}");
                return 0;
            }

            case "set":
            {
                Require(args, 4, "rex config set <path> <value>");
                config.Set(args[2], args[3]);
                RexBrand.Success($"{args[2]} saved.");
                return 0;
            }

            case "restore":
                config.RestoreBackup();
                RexBrand.Success("Restored the previous config.json backup.");
                return 0;

            default:
                throw new ArgumentException("config expects list, get, set, or restore.");
        }
    }

    internal static async Task<string> ResolveSerialAsync(IBridgeClient bridge, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        var devices = (await bridge.GetDevicesAsync())
            .Where(x => x.State == "device")
            .ToArray();

        return devices.Length switch
        {
            1 => devices[0].Serial,
            0 => throw new InvalidOperationException("No authorized Android device is connected."),
            _ => throw new InvalidOperationException(
                "Multiple authorized Android devices are connected. Pass --serial <SERIAL>.")
        };
    }

    internal static string NormalizeNamespace(string ns)
    {
        var normalized = ns.ToLowerInvariant();
        if (normalized is not ("system" or "secure" or "global"))
            throw new ArgumentException("Namespace must be system, secure, or global.");
        return normalized;
    }

    private static async Task<int> RunScriptCommandAsync(
        IProcessRunner runner,
        string script,
        IEnumerable<string>? args = null)
    {
        var result = await runner.RunPowerShellAsync(script, args);

        if (!string.IsNullOrWhiteSpace(result.StdOut))
            Console.WriteLine(result.StdOut.TrimEnd());

        return result.Ok ? 0 : PrintProcessFailure(result);
    }

    private static int PrintProcessFailure(ProcessResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StdErr)
            ? result.StdOut
            : result.StdErr;

        RexBrand.Error(text.Trim());
        return result.ExitCode == 0 ? 1 : result.ExitCode;
    }

    private static void PrintStatus(RexStatus status)
    {
        RexBrand.Header("STATUS");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(RexBrand.LineColor)
            .AddColumn("Component")
            .AddColumn("State");

        table.AddRow("Setup", RexBrand.State(status.SetupComplete, "ready", "required"));
        table.AddRow("Autostart", RexBrand.State(status.AutostartEnabled, "enabled", "disabled"));
        table.AddRow("Service", status.PersistentOff ? "[#FF774D]persistent OFF[/]" : "[#D7FF3F]enabled[/]");
        table.AddRow("Supervisor", RexBrand.State(status.SupervisorRunning, "running", "stopped"));
        table.AddRow("Mirror", RexBrand.State(status.MirrorRunning, "running", "waiting"));

        AnsiConsole.Write(table);
        PrintDevices(status.Devices);
    }

    private static void PrintDevices(IReadOnlyList<RexDevice> devices)
    {
        if (devices.Count == 0)
        {
            AnsiConsole.MarkupLine($"[{RexBrand.Muted}]No Android devices detected.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(RexBrand.LineColor)
            .AddColumn("Device")
            .AddColumn("Serial")
            .AddColumn("Transport")
            .AddColumn("ADB state");

        foreach (var device in devices)
        {
            table.AddRow(
                Markup.Escape(string.IsNullOrWhiteSpace(device.DisplayName) ? "Android device" : device.DisplayName),
                Markup.Escape(device.Serial),
                device.IsTcp ? "wireless" : "USB",
                Markup.Escape(device.State));
        }

        AnsiConsole.Write(table);
    }

    private static void PrintBridgeText(JsonDocument result, string fallback)
    {
        var root = result.RootElement;

        if (root.TryGetProperty("Text", out var textNode) &&
            !string.IsNullOrWhiteSpace(textNode.GetString()))
        {
            Console.WriteLine(textNode.GetString());
            return;
        }

        if (root.TryGetProperty("Path", out var pathNode) &&
            !string.IsNullOrWhiteSpace(pathNode.GetString()))
        {
            Console.WriteLine(pathNode.GetString());
            return;
        }

        if (!string.IsNullOrWhiteSpace(fallback))
            RexBrand.Success(fallback);
    }

    internal static string? GetOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void Require(string[] args, int minimum, string usage)
    {
        if (args.Length < minimum)
            throw new ArgumentException($"Usage: {usage}");
    }

    private static void PrintHelp()
    {
        RexBrand.Header("CLI");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(RexBrand.LineColor)
            .AddColumn("Command")
            .AddColumn("Purpose");

        foreach (var row in new[]
        {
            ("rex", "Open the guided REX terminal app"),
            ("rex --json capabilities", "Machine-readable capability discovery for coding agents"),
            ("rex --json <command> ...", "Prompt-free JSON protocol with deterministic exit codes"),
            ("rex status", "Show service, mirror, startup and device state"),
            ("rex devices", "List every ADB-visible Android device"),
            ("rex setup", "Install/verify scrcpy + ADB"),
            ("rex repair", "Repair tools while preserving startup preference"),
            ("rex start | stop", "Start or persistently stop the mirror service"),
            ("rex controls [--serial S]", "Open the GUI Control Center"),
            ("rex action <name> [--serial S]", "Send a scrcpy runtime action"),
            ("rex mirror <zoom-in|zoom-out|reset-zoom>", "Control PC-only host zoom"),
            ("rex device set <setting> <value> [--serial S]", "Change a friendly Android setting"),
            ("rex android list <system|secure|global> [--filter X]", "Browse live Android Settings Provider keys"),
            ("rex android get/set/delete ...", "Read or change an Android Settings Provider key"),
            ("rex config list [--filter X]", "Browse every PC/mirror config value"),
            ("rex config get/set <path> ...", "Read or change any PC/mirror config value"),
            ("rex config restore", "Restore the previous config backup"),
            ("rex autostart on|off", "Control Windows sign-in startup"),
            ("rex lock-mode <serial> <pattern|other|none>", "Set per-device lock-screen behavior"),
            ("rex reset-lock [serial|ALL]", "Clear saved lock-screen choice/calibration"),
            ("rex screenshot [--serial S]", "Save an Android screenshot"),
            ("rex captures", "Open the captures directory"),
            ("rex diagnostics", "Run the full package diagnostics"),
        })
        {
            table.AddRow(
                Markup.Escape(row.Item1),
                Markup.Escape(row.Item2));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[{RexBrand.Muted}]Tip: use the interactive app for guided workflows and these commands for automation.[/]");
    }
}
