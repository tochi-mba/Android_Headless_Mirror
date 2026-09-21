using System.Text.Json;

namespace Rex.AndroidMirror.Cli;

/// <summary>
/// Strict non-interactive command surface for coding agents and shell automation.
/// Writes exactly one JSON document to stdout per invocation and never prompts.
/// </summary>
public static class AgentCli
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> RunAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config)
    {
        var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();

        try
        {
            object? data = command switch
            {
                "help" => Help(),
                "status" => await bridge.GetStatusAsync(),
                "devices" => await bridge.GetDevicesAsync(),
                "start" => await StartAsync(paths, runner),
                "stop" => await ScriptAsync(runner, paths.Stop),
                "setup" => await ScriptAsync(
                    runner,
                    paths.Setup,
                    args.Contains("--skip-autostart", StringComparer.OrdinalIgnoreCase)
                        ? new[] { "-SkipAutostart" }
                        : null),
                "repair" => await RepairAsync(paths, runner, bridge),
                "diagnostics" => await ScriptAsync(runner, paths.Diagnostics),
                "autostart" => await AutostartAsync(args, paths, runner),
                "controls" => await OpenControlsAsync(args, paths, runner, bridge),
                "action" => await ActionAsync(args, bridge),
                "mirror" => await MirrorAsync(args, bridge),
                "device" => await DeviceAsync(args, bridge),
                "android" => await AndroidAsync(args, bridge),
                "config" => Config(args, config),
                "screenshot" => await ScreenshotAsync(args, bridge),
                "lock-mode" => await LockModeAsync(args, bridge),
                "reset-lock" => await ResetLockAsync(args, paths, runner),
                "captures" => new { path = paths.Captures },
                _ => throw new ArgumentException($"Unknown agent command '{command}'.")
            };

            Write(new
            {
                ok = true,
                command,
                data
            });
            return 0;
        }
        catch (Exception ex)
        {
            Write(new
            {
                ok = false,
                command,
                error = ex.Message,
                errorType = ex.GetType().Name
            });
            return 1;
        }
    }

    private static object Help() => new
    {
        mode = "agent",
        interactive = false,
        stdout = "exactly one JSON document",
        commands = new[]
        {
            "status",
            "devices",
            "start",
            "stop",
            "setup [--skip-autostart]",
            "repair",
            "diagnostics",
            "autostart <on|off>",
            "controls [--serial SERIAL]",
            "action <name> [--serial SERIAL]",
            "mirror <zoom-in|zoom-out|reset-zoom> [--serial SERIAL]",
            "device set <setting> <value> [--serial SERIAL]",
            "android list <system|secure|global> [--filter TEXT] [--serial SERIAL]",
            "android get <namespace> <key> [--serial SERIAL]",
            "android set <namespace> <key> <value> [--serial SERIAL]",
            "android delete <namespace> <key> [--serial SERIAL]",
            "config list [--filter TEXT]",
            "config get <path>",
            "config set <path> <value>",
            "config restore",
            "screenshot [--serial SERIAL]",
            "lock-mode <serial> <pattern|other|none>",
            "reset-lock [serial|ALL]",
            "captures"
        }
    };

    private static async Task<object> StartAsync(AppPaths paths, IProcessRunner runner)
    {
        if (File.Exists(paths.StopFlag))
            File.Delete(paths.StopFlag);

        var exitCode = await runner.OpenAsync(paths.StartBatch, paths.Root);
        if (exitCode != 0)
            throw new InvalidOperationException($"Start launcher failed with exit code {exitCode}.");

        return new { requested = true, persistentOffCleared = true };
    }

    private static async Task<object> ScriptAsync(
        IProcessRunner runner,
        string script,
        IEnumerable<string>? arguments = null)
    {
        var result = await runner.RunPowerShellAsync(script, arguments);
        if (!result.Ok)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdOut
                : result.StdErr;
            throw new InvalidOperationException(error.Trim());
        }

        return new
        {
            exitCode = result.ExitCode,
            stdout = result.StdOut.Trim(),
            stderr = result.StdErr.Trim()
        };
    }

    private static async Task<object> RepairAsync(
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge)
    {
        var before = await bridge.GetStatusAsync();
        _ = await ScriptAsync(runner, paths.Setup, new[] { "-SkipAutostart" });
        _ = await ScriptAsync(
            runner,
            before.AutostartEnabled ? paths.InstallAutostart : paths.RemoveAutostart);

        return new
        {
            repaired = true,
            autostartPreserved = before.AutostartEnabled
        };
    }

    private static async Task<object> AutostartAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner)
    {
        Require(args, 2, "agent autostart <on|off>");
        var enabled = args[1].ToLowerInvariant() switch
        {
            "on" or "enable" => true,
            "off" or "disable" => false,
            _ => throw new ArgumentException("autostart expects on or off.")
        };

        _ = await ScriptAsync(
            runner,
            enabled ? paths.InstallAutostart : paths.RemoveAutostart);

        return new { enabled };
    }

    private static async Task<object> OpenControlsAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge)
    {
        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
        var psArgs = new[]
        {
            "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
            "-File", paths.ControlCenter,
            "-Serial", serial
        };

        var exitCode = await runner.StartDetachedAsync("powershell.exe", psArgs, paths.Root);
        if (exitCode != 0)
            throw new InvalidOperationException($"Control Center launch failed with exit code {exitCode}.");

        return new { serial, opened = true };
    }

    private static async Task<object> ActionAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 2, "agent action <name> [--serial SERIAL]");
        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
        using var result = await bridge.InvokeAsync(
            "scrcpy-action",
            serial: serial,
            name: args[1]);

        return JsonElementToObject(result.RootElement);
    }

    private static async Task<object> MirrorAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 2, "agent mirror <zoom-in|zoom-out|reset-zoom> [--serial SERIAL]");
        var name = args[1].ToLowerInvariant();
        if (name is not ("zoom-in" or "zoom-out" or "reset-zoom"))
            throw new ArgumentException("mirror expects zoom-in, zoom-out, or reset-zoom.");

        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
        using var result = await bridge.InvokeAsync(
            "mirror-command",
            serial: serial,
            name: name);

        return JsonElementToObject(result.RootElement);
    }

    private static async Task<object> DeviceAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 4, "agent device set <setting> <value> [--serial SERIAL]");
        if (!args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("device currently supports: set.");

        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
        if (args[2].Equals("animation-scale", StringComparison.OrdinalIgnoreCase))
        {
            var calls = new List<object>();
            foreach (var id in new[] { "animation-window", "animation-transition", "animation-duration" })
            {
                using var result = await bridge.InvokeAsync(
                    "friendly-set",
                    serial: serial,
                    name: id,
                    value: args[3]);
                calls.Add(JsonElementToObject(result.RootElement));
            }
            return new { serial, setting = "animation-scale", value = args[3], results = calls };
        }

        using var single = await bridge.InvokeAsync(
            "friendly-set",
            serial: serial,
            name: args[2],
            value: args[3]);

        return JsonElementToObject(single.RootElement);
    }

    private static async Task<object> AndroidAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 3, "agent android <list|get|set|delete> <namespace> ...");
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
                    rows = rows.Where(x =>
                        x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        x.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                }

                return rows.Select(x => new
                {
                    key = x.Key,
                    value = x.Value,
                    risk = x.Risk
                }).ToArray();
            }

            case "get":
                Require(args, 4, "agent android get <namespace> <key> [--serial SERIAL]");
                using (var result = await bridge.InvokeAsync(
                    "settings-get",
                    serial: serial,
                    ns: ns,
                    key: args[3]))
                {
                    return JsonElementToObject(result.RootElement);
                }

            case "set":
                Require(args, 5, "agent android set <namespace> <key> <value> [--serial SERIAL]");
                using (var result = await bridge.InvokeAsync(
                    "settings-set",
                    serial: serial,
                    ns: ns,
                    key: args[3],
                    value: args[4]))
                {
                    return JsonElementToObject(result.RootElement);
                }

            case "delete":
                Require(args, 4, "agent android delete <namespace> <key> [--serial SERIAL]");
                using (var result = await bridge.InvokeAsync(
                    "settings-delete",
                    serial: serial,
                    ns: ns,
                    key: args[3]))
                {
                    return JsonElementToObject(result.RootElement);
                }

            default:
                throw new ArgumentException("android expects list, get, set, or delete.");
        }
    }

    private static object Config(string[] args, ConfigStore config)
    {
        Require(args, 2, "agent config <list|get|set|restore> ...");
        var verb = args[1].ToLowerInvariant();

        return verb switch
        {
            "list" => config.Flatten()
                .Where(x =>
                    string.IsNullOrWhiteSpace(GetOption(args, "--filter")) ||
                    x.Path.Contains(GetOption(args, "--filter")!, StringComparison.OrdinalIgnoreCase))
                .Select(x => new { path = x.Path, value = x.Value, type = x.Kind.ToString() })
                .ToArray(),

            "get" => ConfigGet(args, config),
            "set" => ConfigSet(args, config),
            "restore" => ConfigRestore(config),
            _ => throw new ArgumentException("config expects list, get, set, or restore.")
        };
    }

    private static object ConfigGet(string[] args, ConfigStore config)
    {
        Require(args, 3, "agent config get <path>");
        var leaf = config.Get(args[2]);
        return new { path = leaf.Path, value = leaf.Value, type = leaf.Kind.ToString() };
    }

    private static object ConfigSet(string[] args, ConfigStore config)
    {
        Require(args, 4, "agent config set <path> <value>");
        config.Set(args[2], args[3]);
        var leaf = config.Get(args[2]);
        return new
        {
            path = leaf.Path,
            value = leaf.Value,
            type = leaf.Kind.ToString(),
            backup = config.BackupPath
        };
    }

    private static object ConfigRestore(ConfigStore config)
    {
        config.RestoreBackup();
        return new { restored = true, backup = config.BackupPath };
    }

    private static async Task<object> ScreenshotAsync(string[] args, IBridgeClient bridge)
    {
        var serial = await ResolveSerialAsync(bridge, GetOption(args, "--serial"));
        using var result = await bridge.InvokeAsync("screenshot", serial: serial);
        return JsonElementToObject(result.RootElement);
    }

    private static async Task<object> LockModeAsync(string[] args, IBridgeClient bridge)
    {
        Require(args, 3, "agent lock-mode <serial> <pattern|other|none>");
        var mode = args[2].ToLowerInvariant();
        if (mode is not ("pattern" or "other" or "none"))
            throw new ArgumentException("lock-mode expects pattern, other, or none.");

        using var result = await bridge.InvokeAsync(
            "set-lock-mode",
            serial: args[1],
            value: mode);

        return JsonElementToObject(result.RootElement);
    }

    private static async Task<object> ResetLockAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner)
    {
        var serial = args.Length >= 2 ? args[1] : "ALL";
        var result = await ScriptAsync(
            runner,
            paths.ResetLockChoices,
            new[] { "-Serial", serial });

        return new { serial, result };
    }

    private static async Task<string> ResolveSerialAsync(
        IBridgeClient bridge,
        string? requested)
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

    private static string NormalizeNamespace(string ns)
    {
        var normalized = ns.ToLowerInvariant();
        if (normalized is not ("system" or "secure" or "global"))
            throw new ArgumentException("Namespace must be system, secure, or global.");
        return normalized;
    }

    private static string? GetOption(string[] args, string option)
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
            throw new ArgumentException($"Usage: rex agent {usage}");
    }

    private static object JsonElementToObject(JsonElement element) =>
        JsonSerializer.Deserialize<object>(element.GetRawText(), JsonOptions)
        ?? new { };

    private static void Write(object value) =>
        Console.Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}
