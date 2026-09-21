using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rex.AndroidMirror.Cli;

public sealed record MachineCommandResult(int ExitCode, string Json);

public static class MachineMode
{
    public const int ProtocolVersion = 1;

    private static readonly string[] RuntimeActions =
    [
        "fullscreen", "fit", "pixel-perfect", "rotate-left", "rotate-right",
        "flip-horizontal", "flip-vertical", "pause", "resume", "reset-capture",
        "fps", "home", "back", "apps", "menu", "power", "sleep", "wake",
        "rotate-device", "notifications", "quick-settings", "collapse-panels",
        "volume-down", "volume-up", "copy", "cut", "paste-sync",
        "paste-inject", "keyboard-settings"
    ];

    private static readonly string[] MirrorCommands =
    [
        "zoom-in", "zoom-out", "reset-zoom"
    ];

    private static readonly string[] FriendlyDeviceSettings =
    [
        "brightness", "brightness-mode", "screen-timeout-ms", "auto-rotate",
        "user-rotation", "font-scale", "show-touches", "stay-awake",
        "animation-window", "animation-transition", "animation-duration",
        "animation-scale", "dark-mode", "wifi", "mobile-data",
        "airplane-mode", "wm-size", "wm-density"
    ];

    public static async Task<MachineCommandResult> RunAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config)
    {
        try
        {
            if (args.Length == 0)
                return Success("help", Capabilities(config));

            var command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "help":
                case "capabilities":
                    return Success(command, Capabilities(config));

                case "status":
                    return Success("status", await bridge.GetStatusAsync());

                case "devices":
                    return Success("devices", await bridge.GetDevicesAsync());

                case "start":
                    if (File.Exists(paths.StopFlag))
                        File.Delete(paths.StopFlag);
                    var startCode = await runner.OpenAsync(paths.StartBatch, paths.Root);
                    if (startCode != 0)
                        throw new InvalidOperationException("Could not request mirror startup.");
                    return Success("start", new { requested = true });

                case "stop":
                    return await RunScriptAsync("stop", runner, paths.Stop);

                case "setup":
                    return await RunScriptAsync(
                        "setup",
                        runner,
                        paths.Setup,
                        args.Contains("--skip-autostart", StringComparer.OrdinalIgnoreCase)
                            ? new[] { "-SkipAutostart" }
                            : null);

                case "repair":
                {
                    var before = await bridge.GetStatusAsync();
                    var setup = await runner.RunPowerShellAsync(paths.Setup, new[] { "-SkipAutostart" });
                    if (!setup.Ok)
                        return ProcessFailure("repair", setup);

                    var startup = await runner.RunPowerShellAsync(
                        before.AutostartEnabled ? paths.InstallAutostart : paths.RemoveAutostart);
                    if (!startup.Ok)
                        return ProcessFailure("repair", startup);

                    return Success("repair", new
                    {
                        repaired = true,
                        preservedAutostart = before.AutostartEnabled
                    });
                }

                case "autostart":
                {
                    Require(args, 2, "autostart <on|off>");
                    var enabled = args[1].ToLowerInvariant() switch
                    {
                        "on" or "enable" => true,
                        "off" or "disable" => false,
                        _ => throw new ArgumentException("autostart expects on or off.")
                    };

                    var result = await runner.RunPowerShellAsync(
                        enabled ? paths.InstallAutostart : paths.RemoveAutostart);

                    if (!result.Ok)
                        return ProcessFailure("autostart", result);

                    return Success("autostart", new { enabled });
                }

                case "shortcut":
                {
                    Require(args, 2, "shortcut <install|remove>");
                    var install = args[1].ToLowerInvariant() switch
                    {
                        "install" or "on" => true,
                        "remove" or "off" => false,
                        _ => throw new ArgumentException("shortcut expects install or remove.")
                    };

                    var result = await runner.RunPowerShellAsync(
                        install ? paths.InstallRexShortcut : paths.RemoveRexShortcut);

                    if (!result.Ok)
                        return ProcessFailure("shortcut", result);

                    return Success("shortcut", new { installed = install });
                }

                case "smart":
                {
                    var outcome = await new SmartLauncher(paths, runner, bridge)
                        .RunAsync(cancellationToken: default);

                    if (outcome.OpenInteractiveCli)
                    {
                        return Success("smart", new
                        {
                            decision = outcome.Decision.ToString(),
                            interactiveRequired = true,
                            message = outcome.Message
                        });
                    }

                    return Success("smart", new
                    {
                        decision = outcome.Decision.ToString(),
                        interactiveRequired = false,
                        message = outcome.Message
                    });
                }

                case "diagnostics":
                {
                    var result = await runner.RunPowerShellAsync(paths.Diagnostics);
                    if (!result.Ok)
                        return ProcessFailure("diagnostics", result);
                    return Success("diagnostics", new { text = result.StdOut.Trim() });
                }

                case "controls":
                {
                    var serial = await ResolveSerialAsync(bridge, Option(args, "--serial"));
                    var psArgs = new[]
                    {
                        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
                        "-WindowStyle", "Hidden", "-File", paths.ControlCenter,
                        "-Serial", serial
                    };

                    var code = await runner.StartDetachedAsync("powershell.exe", psArgs, paths.Root);
                    if (code != 0)
                        throw new InvalidOperationException("Could not open the Control Center.");

                    return Success("controls", new { serial, opened = true });
                }

                case "action":
                {
                    Require(args, 2, "action <name> [--serial SERIAL]");
                    var name = args[1].ToLowerInvariant();
                    if (!RuntimeActions.Contains(name, StringComparer.Ordinal))
                        throw new ArgumentException($"Unknown runtime action '{args[1]}'.");

                    var serial = await ResolveSerialAsync(bridge, Option(args, "--serial"));
                    using var result = await bridge.InvokeAsync(
                        "scrcpy-action", serial: serial, name: name);

                    return Success("action", new
                    {
                        serial,
                        action = name,
                        result = Clone(result.RootElement)
                    });
                }

                case "mirror":
                {
                    Require(args, 2, "mirror <zoom-in|zoom-out|reset-zoom> [--serial SERIAL]");
                    var name = args[1].ToLowerInvariant();
                    if (!MirrorCommands.Contains(name, StringComparer.Ordinal))
                        throw new ArgumentException("mirror expects zoom-in, zoom-out, or reset-zoom.");

                    var serial = await ResolveSerialAsync(bridge, Option(args, "--serial"));
                    using var result = await bridge.InvokeAsync(
                        "mirror-command", serial: serial, name: name);

                    return Success("mirror", new
                    {
                        serial,
                        command = name,
                        result = Clone(result.RootElement)
                    });
                }

                case "display":
                    return await DisplayAsync(args, paths, runner, bridge, config);

                case "device":
                    return await DeviceAsync(args, bridge);

                case "android":
                    return await AndroidAsync(args, bridge);

                case "config":
                    return Config(args, config);

                case "screenshot":
                {
                    var serial = await ResolveSerialAsync(bridge, Option(args, "--serial"));
                    using var result = await bridge.InvokeAsync("screenshot", serial: serial);
                    return Success("screenshot", new
                    {
                        serial,
                        result = Clone(result.RootElement)
                    });
                }

                case "lock-mode":
                {
                    Require(args, 3, "lock-mode <serial> <pattern|other|none>");
                    var mode = args[2].ToLowerInvariant();
                    if (mode is not ("pattern" or "other" or "none"))
                        throw new ArgumentException("lock-mode expects pattern, other, or none.");

                    using var result = await bridge.InvokeAsync(
                        "set-lock-mode", serial: args[1], value: mode);

                    return Success("lock-mode", new
                    {
                        serial = args[1],
                        mode,
                        result = Clone(result.RootElement)
                    });
                }

                case "reset-lock":
                {
                    var serial = args.Length >= 2 ? args[1] : "ALL";
                    var result = await runner.RunPowerShellAsync(
                        paths.ResetLockChoices,
                        new[] { "-Serial", serial });

                    if (!result.Ok)
                        return ProcessFailure("reset-lock", result);

                    return Success("reset-lock", new { serial, reset = true });
                }

                default:
                    throw new ArgumentException(
                        $"Unknown machine command '{args[0]}'. Use '--json capabilities'.");
            }
        }
        catch (Exception ex)
        {
            return Failure(ex);
        }
    }

    private static async Task<MachineCommandResult> DisplayAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config)
    {
        Require(
            args,
            2,
            "display <status|probe|capabilities|transports|start|receiver|verify> ...");

        var manager = new DisplayManager(paths, runner, bridge, config);
        var verb = args[1].ToLowerInvariant();

        switch (verb)
        {
            case "status":
            case "probe":
            case "capabilities":
            case "transports":
                return Success($"display.{verb}", await manager.ProbeAsync());

            case "start":
            {
                var transport = Option(args, "--transport") ?? manager.DefaultTransport();
                var result = await manager.StartAsync(transport);
                return Success("display.start", result);
            }

            case "receiver":
            {
                Require(args, 3, "display receiver open");
                if (!args[2].Equals("open", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("display receiver currently supports: open.");

                return Success("display.receiver", await manager.OpenReceiverAsync());
            }

            case "verify":
            {
                Require(
                    args,
                    4,
                    "display verify <normal|protected> <pass|fail|clear> [--transport windows-miracast] [--note TEXT]");

                var transport =
                    Option(args, "--transport") ?? DisplayTransportIds.WindowsMiracast;
                var note = Option(args, "--note") ?? string.Empty;
                var verification = manager.Verify(transport, args[2], args[3], note);

                return Success("display.verify", new
                {
                    transport = DisplayManager.NormalizeTransport(transport),
                    target = args[2].ToLowerInvariant(),
                    verification
                });
            }

            default:
                throw new ArgumentException(
                    "display expects status, probe, capabilities, transports, start, receiver, or verify.");
        }
    }

    private static async Task<MachineCommandResult> DeviceAsync(
        string[] args,
        IBridgeClient bridge)
    {
        Require(args, 4, "device set <setting> <value> [--serial SERIAL]");
        if (!args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("device expects set.");

        var setting = args[2].ToLowerInvariant();
        var value = args[3];
        if (!FriendlyDeviceSettings.Contains(setting, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown friendly device setting '{args[2]}'.");

        var serial = await ResolveSerialAsync(bridge, Option(args, "--serial"));

        if (setting == "animation-scale")
        {
            var results = new JsonArray();
            foreach (var id in new[]
            {
                "animation-window", "animation-transition", "animation-duration"
            })
            {
                using var result = await bridge.InvokeAsync(
                    "friendly-set", serial: serial, name: id, value: value);
                results.Add(Clone(result.RootElement));
            }

            return Success("device", new
            {
                serial,
                setting,
                value,
                results
            });
        }

        using var single = await bridge.InvokeAsync(
            "friendly-set", serial: serial, name: setting, value: value);

        return Success("device", new
        {
            serial,
            setting,
            value,
            result = Clone(single.RootElement)
        });
    }

    private static async Task<MachineCommandResult> AndroidAsync(
        string[] args,
        IBridgeClient bridge)
    {
        Require(args, 3, "android <list|get|set|delete> <namespace> ...");
        var verb = args[1].ToLowerInvariant();
        var ns = NormalizeNamespace(args[2]);
        var serial = await ResolveSerialAsync(bridge, Option(args, "--serial"));

        switch (verb)
        {
            case "list":
            {
                var rows = await bridge.ListAndroidSettingsAsync(serial, ns);
                var filter = Option(args, "--filter");
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    rows = rows.Where(x =>
                        x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        x.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                }

                return Success("android.list", new
                {
                    serial,
                    @namespace = ns,
                    rows = rows.Select(x => new
                    {
                        key = x.Key,
                        value = x.Value,
                        risk = x.Risk
                    }).ToArray()
                });
            }

            case "get":
            {
                Require(args, 4, "android get <namespace> <key> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-get", serial: serial, ns: ns, key: args[3]);
                return Success("android.get", new
                {
                    serial,
                    @namespace = ns,
                    key = args[3],
                    result = Clone(result.RootElement)
                });
            }

            case "set":
            {
                Require(args, 5, "android set <namespace> <key> <value> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-set",
                    serial: serial,
                    ns: ns,
                    key: args[3],
                    value: args[4]);
                return Success("android.set", new
                {
                    serial,
                    @namespace = ns,
                    key = args[3],
                    value = args[4],
                    result = Clone(result.RootElement)
                });
            }

            case "delete":
            {
                Require(args, 4, "android delete <namespace> <key> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-delete",
                    serial: serial,
                    ns: ns,
                    key: args[3]);
                return Success("android.delete", new
                {
                    serial,
                    @namespace = ns,
                    key = args[3],
                    result = Clone(result.RootElement)
                });
            }

            default:
                throw new ArgumentException("android expects list, get, set, or delete.");
        }
    }

    private static MachineCommandResult Config(string[] args, ConfigStore config)
    {
        Require(args, 2, "config <list|get|set|restore> ...");

        switch (args[1].ToLowerInvariant())
        {
            case "list":
            {
                var filter = Option(args, "--filter");
                var rows = config.Flatten()
                    .Where(x =>
                        string.IsNullOrWhiteSpace(filter) ||
                        x.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(x => new
                    {
                        path = x.Path,
                        value = x.Value,
                        type = x.Kind.ToString()
                    })
                    .ToArray();

                return Success("config.list", new { rows });
            }

            case "get":
            {
                Require(args, 3, "config get <path>");
                var leaf = config.Get(args[2]);
                return Success("config.get", new
                {
                    path = leaf.Path,
                    value = leaf.Value,
                    type = leaf.Kind.ToString()
                });
            }

            case "set":
            {
                Require(args, 4, "config set <path> <value>");
                config.Set(args[2], args[3]);
                var leaf = config.Get(args[2]);
                return Success("config.set", new
                {
                    path = leaf.Path,
                    value = leaf.Value,
                    type = leaf.Kind.ToString(),
                    backup = config.BackupPath
                });
            }

            case "restore":
                config.RestoreBackup();
                return Success("config.restore", new { restored = true });

            default:
                throw new ArgumentException("config expects list, get, set, or restore.");
        }
    }

    private static object Capabilities(ConfigStore config) => new
    {
        protocolVersion = ProtocolVersion,
        mode = "non-interactive-json",
        commands = new[]
        {
            "status", "devices", "start", "stop", "setup", "repair",
            "autostart", "shortcut", "smart", "diagnostics", "controls", "action", "mirror",
            "display", "device", "android", "config", "screenshot", "lock-mode",
            "reset-lock"
        },
        runtimeActions = RuntimeActions,
        mirrorCommands = MirrorCommands,
        displayCommands = new[]
        {
            "display status",
            "display probe",
            "display capabilities",
            "display transports",
            "display start --transport <scrcpy|windows-miracast>",
            "display receiver open",
            "display verify <normal|protected> <pass|fail|clear>"
        },
        friendlyDeviceSettings = FriendlyDeviceSettings,
        androidNamespaces = new[] { "system", "secure", "global" },
        configPaths = config.Flatten().Select(x => x.Path).ToArray(),
        serialRule = "Omit --serial only when exactly one authorized Android device is connected.",
        output = new
        {
            stdout = "exactly one JSON document",
            prompts = false,
            ansi = false,
            successExitCode = 0,
            failureExitCode = 1
        }
    };

    private static async Task<MachineCommandResult> RunScriptAsync(
        string command,
        IProcessRunner runner,
        string script,
        IEnumerable<string>? args = null)
    {
        var result = await runner.RunPowerShellAsync(script, args);
        return result.Ok
            ? Success(command, new
            {
                exitCode = result.ExitCode,
                stdout = result.StdOut.Trim(),
                stderr = result.StdErr.Trim()
            })
            : ProcessFailure(command, result);
    }

    private static MachineCommandResult ProcessFailure(
        string command,
        ProcessResult result) =>
        Failure(new InvalidOperationException(
            string.IsNullOrWhiteSpace(result.StdErr)
                ? result.StdOut.Trim()
                : result.StdErr.Trim()),
            command,
            result.ExitCode == 0 ? 1 : result.ExitCode);

    private static MachineCommandResult Success(string command, object data) =>
        Build(0, new
        {
            ok = true,
            protocolVersion = ProtocolVersion,
            command,
            data
        });

    private static MachineCommandResult Failure(
        Exception ex,
        string? command = null,
        int exitCode = 1) =>
        Build(exitCode, new
        {
            ok = false,
            protocolVersion = ProtocolVersion,
            command,
            error = new
            {
                type = ex.GetType().Name,
                message = ex.Message
            }
        });

    private static MachineCommandResult Build(int exitCode, object payload) =>
        new(exitCode, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        }));

    private static JsonNode? Clone(JsonElement element) =>
        JsonNode.Parse(element.GetRawText());

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
            0 => throw new InvalidOperationException(
                "No authorized Android device is connected."),
            _ => throw new InvalidOperationException(
                "Multiple authorized Android devices are connected. Pass --serial <SERIAL>.")
        };
    }

    private static string NormalizeNamespace(string value)
    {
        var normalized = value.ToLowerInvariant();
        if (normalized is not ("system" or "secure" or "global"))
            throw new ArgumentException(
                "Namespace must be system, secure, or global.");
        return normalized;
    }

    private static string? Option(string[] args, string option)
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
}
