using System.Text.Json;

namespace Rex.AndroidMirror.Cli;

public static class AgentCli
{
    public const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task<int> RunAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config,
        TextWriter? output = null)
    {
        output ??= Console.Out;
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "spec";

        try
        {
            switch (command)
            {
                case "spec":
                {
                    WriteSuccess(output, "spec", BuildSpec());
                    return 0;
                }

                case "status":
                {
                    var status = await bridge.GetStatusAsync();
                    WriteSuccess(output, "status", status);
                    return 0;
                }

                case "devices":
                {
                    var devices = await bridge.GetDevicesAsync();
                    WriteSuccess(output, "devices", new { devices });
                    return 0;
                }

                case "config":
                    return RunConfig(args, config, output);

                case "action":
                {
                    Require(args, 2, "action <name> [--serial SERIAL]");
                    var serial = await Program.ResolveSerialAsync(bridge, Program.GetOption(args, "--serial"));
                    using var result = await bridge.InvokeAsync(
                        "scrcpy-action",
                        serial: serial,
                        name: args[1]);
                    WriteSuccess(output, "action", BridgeData(result));
                    return 0;
                }

                case "mirror":
                {
                    Require(args, 2, "mirror <zoom-in|zoom-out|reset-zoom> [--serial SERIAL]");
                    var name = args[1].ToLowerInvariant();
                    if (name is not ("zoom-in" or "zoom-out" or "reset-zoom"))
                        throw new AgentUsageException("mirror expects zoom-in, zoom-out, or reset-zoom.");

                    var serial = await Program.ResolveSerialAsync(bridge, Program.GetOption(args, "--serial"));
                    using var result = await bridge.InvokeAsync(
                        "mirror-command",
                        serial: serial,
                        name: name);
                    WriteSuccess(output, "mirror", BridgeData(result));
                    return 0;
                }

                case "device":
                    return await RunDeviceAsync(args, bridge, output);

                case "android":
                    return await RunAndroidAsync(args, bridge, output);

                case "screenshot":
                {
                    var serial = await Program.ResolveSerialAsync(bridge, Program.GetOption(args, "--serial"));
                    using var result = await bridge.InvokeAsync("screenshot", serial: serial);
                    WriteSuccess(output, "screenshot", BridgeData(result));
                    return 0;
                }

                case "lock-mode":
                {
                    Require(args, 3, "lock-mode <serial> <pattern|other|none>");
                    var mode = args[2].ToLowerInvariant();
                    if (mode is not ("pattern" or "other" or "none"))
                        throw new AgentUsageException("lock-mode expects pattern, other, or none.");

                    using var result = await bridge.InvokeAsync(
                        "set-lock-mode",
                        serial: args[1],
                        value: mode);
                    WriteSuccess(output, "lock-mode", BridgeData(result));
                    return 0;
                }

                case "start":
                {
                    if (File.Exists(paths.StopFlag))
                        File.Delete(paths.StopFlag);

                    var code = await runner.OpenAsync(paths.StartBatch, paths.Root);
                    if (code != 0)
                        throw new InvalidOperationException("Could not launch START_NOW.bat.");

                    WriteSuccess(output, "start", new { requested = true });
                    return 0;
                }

                case "stop":
                {
                    var result = await runner.RunPowerShellAsync(paths.Stop);
                    if (!result.Ok)
                        throw new AgentProcessException("stop", result);

                    WriteSuccess(output, "stop", ProcessData(result));
                    return 0;
                }

                case "autostart":
                {
                    Require(args, 2, "autostart <on|off>");
                    var enable = args[1].ToLowerInvariant() switch
                    {
                        "on" or "enable" => true,
                        "off" or "disable" => false,
                        _ => throw new AgentUsageException("autostart expects on or off."),
                    };

                    var result = await runner.RunPowerShellAsync(
                        enable ? paths.InstallAutostart : paths.RemoveAutostart);

                    if (!result.Ok)
                        throw new AgentProcessException("autostart", result);

                    WriteSuccess(output, "autostart", new
                    {
                        enabled = enable,
                        process = ProcessData(result),
                    });
                    return 0;
                }

                case "diagnostics":
                {
                    var result = await runner.RunPowerShellAsync(paths.Diagnostics);
                    if (!result.Ok)
                        throw new AgentProcessException("diagnostics", result);

                    WriteSuccess(output, "diagnostics", ProcessData(result));
                    return 0;
                }

                case "setup":
                {
                    var scriptArgs = args.Contains("--skip-autostart", StringComparer.OrdinalIgnoreCase)
                        ? new[] { "-SkipAutostart" }
                        : null;
                    var result = await runner.RunPowerShellAsync(paths.Setup, scriptArgs);
                    if (!result.Ok)
                        throw new AgentProcessException("setup", result);

                    WriteSuccess(output, "setup", ProcessData(result));
                    return 0;
                }

                default:
                    throw new AgentUsageException(
                        $"Unknown agent command '{command}'. Run 'rex agent spec' for the machine-readable command contract.");
            }
        }
        catch (AgentUsageException ex)
        {
            WriteError(output, command, "usage", ex.Message);
            return 2;
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("authorized Android device", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("--serial", StringComparison.OrdinalIgnoreCase))
        {
            WriteError(output, command, "device-selection", ex.Message);
            return 3;
        }
        catch (AgentProcessException ex)
        {
            WriteError(output, command, "process", ex.Message, new
            {
                exitCode = ex.Result.ExitCode,
                stdout = ex.Result.StdOut,
                stderr = ex.Result.StdErr,
            });
            return 4;
        }
        catch (Exception ex)
        {
            WriteError(output, command, "runtime", ex.Message);
            return 1;
        }
    }

    private static int RunConfig(string[] args, ConfigStore config, TextWriter output)
    {
        Require(args, 2, "config <list|get|set|restore> ...");
        var verb = args[1].ToLowerInvariant();

        switch (verb)
        {
            case "list":
            {
                var filter = Program.GetOption(args, "--filter");
                var rows = config.Flatten()
                    .Where(x =>
                        string.IsNullOrWhiteSpace(filter) ||
                        x.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(x => new
                    {
                        path = x.Path,
                        value = x.Value,
                        type = x.Kind.ToString(),
                    })
                    .ToArray();

                WriteSuccess(output, "config list", new { settings = rows });
                return 0;
            }

            case "get":
            {
                Require(args, 3, "config get <path>");
                var leaf = config.Get(args[2]);
                WriteSuccess(output, "config get", new
                {
                    path = leaf.Path,
                    value = leaf.Value,
                    type = leaf.Kind.ToString(),
                });
                return 0;
            }

            case "set":
            {
                Require(args, 4, "config set <path> <value>");
                config.Set(args[2], args[3]);
                var leaf = config.Get(args[2]);
                WriteSuccess(output, "config set", new
                {
                    path = leaf.Path,
                    value = leaf.Value,
                    type = leaf.Kind.ToString(),
                    backupPath = config.BackupPath,
                });
                return 0;
            }

            case "restore":
                config.RestoreBackup();
                WriteSuccess(output, "config restore", new
                {
                    restored = true,
                    backupPath = config.BackupPath,
                });
                return 0;

            default:
                throw new AgentUsageException("config expects list, get, set, or restore.");
        }
    }

    private static async Task<int> RunDeviceAsync(
        string[] args,
        IBridgeClient bridge,
        TextWriter output)
    {
        Require(args, 4, "device set <setting> <value> [--serial SERIAL]");
        if (!args[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            throw new AgentUsageException("device expects: set.");

        var serial = await Program.ResolveSerialAsync(bridge, Program.GetOption(args, "--serial"));

        if (args[2].Equals("animation-scale", StringComparison.OrdinalIgnoreCase))
        {
            var results = new List<JsonElement>();
            foreach (var id in new[] { "animation-window", "animation-transition", "animation-duration" })
            {
                using var result = await bridge.InvokeAsync(
                    "friendly-set",
                    serial: serial,
                    name: id,
                    value: args[3]);
                results.Add(BridgeData(result));
            }

            WriteSuccess(output, "device set", new
            {
                serial,
                setting = "animation-scale",
                value = args[3],
                writes = results,
            });
            return 0;
        }

        using (var result = await bridge.InvokeAsync(
            "friendly-set",
            serial: serial,
            name: args[2],
            value: args[3]))
        {
            WriteSuccess(output, "device set", BridgeData(result));
        }

        return 0;
    }

    private static async Task<int> RunAndroidAsync(
        string[] args,
        IBridgeClient bridge,
        TextWriter output)
    {
        Require(args, 3, "android <list|get|set|delete> <namespace> ...");
        var verb = args[1].ToLowerInvariant();
        var ns = Program.NormalizeNamespace(args[2]);
        var serial = await Program.ResolveSerialAsync(bridge, Program.GetOption(args, "--serial"));

        switch (verb)
        {
            case "list":
            {
                var rows = await bridge.ListAndroidSettingsAsync(serial, ns);
                var filter = Program.GetOption(args, "--filter");
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    rows = rows
                        .Where(x =>
                            x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                            x.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }

                WriteSuccess(output, "android list", new
                {
                    serial,
                    @namespace = ns,
                    settings = rows.Select(x => new
                    {
                        key = x.Key,
                        value = x.Value,
                        risk = x.Risk,
                    }).ToArray(),
                });
                return 0;
            }

            case "get":
            {
                Require(args, 4, "android get <namespace> <key> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-get",
                    serial: serial,
                    ns: ns,
                    key: args[3]);
                WriteSuccess(output, "android get", BridgeData(result));
                return 0;
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
                WriteSuccess(output, "android set", BridgeData(result));
                return 0;
            }

            case "delete":
            {
                Require(args, 4, "android delete <namespace> <key> [--serial SERIAL]");
                using var result = await bridge.InvokeAsync(
                    "settings-delete",
                    serial: serial,
                    ns: ns,
                    key: args[3]);
                WriteSuccess(output, "android delete", BridgeData(result));
                return 0;
            }

            default:
                throw new AgentUsageException("android expects list, get, set, or delete.");
        }
    }

    private static object BuildSpec()
    {
        return new
        {
            name = "rex",
            product = "Android Headless Mirror",
            vendor = "REX Technologies",
            schemaVersion = SchemaVersion,
            invocation = new
            {
                preferred = "rex agent <command>",
                jsonAlias = "rex <command> --json",
                output = "one JSON object on stdout",
                interactive = false,
                ansi = false,
            },
            exitCodes = new Dictionary<string, string>
            {
                ["0"] = "success",
                ["1"] = "runtime error",
                ["2"] = "usage/validation error",
                ["3"] = "device selection required or no authorized device",
                ["4"] = "underlying process failed",
            },
            commands = new[]
            {
                "spec",
                "status",
                "devices",
                "start",
                "stop",
                "setup [--skip-autostart]",
                "autostart <on|off>",
                "diagnostics",
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
                "lock-mode <serial> <pattern|other|none>",
                "screenshot [--serial SERIAL]",
            },
            runtimeActions = new[]
            {
                "sleep", "wake", "power", "home", "back", "apps", "menu",
                "rotate-device", "rotate-left", "rotate-right", "flip-horizontal",
                "flip-vertical", "fullscreen", "fit", "pixel-perfect", "pause",
                "resume", "reset-capture", "fps", "volume-up", "volume-down",
                "notifications", "quick-settings", "collapse-panels", "copy",
                "cut", "paste-sync", "paste-inject", "keyboard-settings",
            },
            friendlyDeviceSettings = new[]
            {
                "brightness", "brightness-mode", "screen-timeout-ms", "auto-rotate",
                "user-rotation", "font-scale", "show-touches", "stay-awake",
                "animation-scale", "dark-mode", "wifi", "mobile-data",
                "airplane-mode", "wm-size", "wm-density",
            },
            androidNamespaces = new[] { "system", "secure", "global" },
            safety = new
            {
                protectedAndroidKeysAreRejected = true,
                configWritesCreateBackup = true,
                multipleDevicesRequireExplicitSerial = true,
                usbDebuggingAuthorizationCannotBeBypassed = true,
            },
        };
    }

    private static JsonElement BridgeData(JsonDocument document)
    {
        return document.RootElement.Clone();
    }

    private static object ProcessData(ProcessResult result)
    {
        return new
        {
            exitCode = result.ExitCode,
            stdout = result.StdOut,
            stderr = result.StdErr,
        };
    }

    private static void WriteSuccess(TextWriter output, string command, object? data)
    {
        Write(output, new
        {
            schemaVersion = SchemaVersion,
            ok = true,
            command,
            data,
        });
    }

    private static void WriteError(
        TextWriter output,
        string command,
        string type,
        string message,
        object? details = null)
    {
        Write(output, new
        {
            schemaVersion = SchemaVersion,
            ok = false,
            command,
            error = new
            {
                type,
                message,
                details,
            },
        });
    }

    private static void Write(TextWriter output, object payload)
    {
        output.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static void Require(string[] args, int minimum, string usage)
    {
        if (args.Length < minimum)
            throw new AgentUsageException($"Usage: rex agent {usage}");
    }

    private sealed class AgentUsageException : Exception
    {
        public AgentUsageException(string message) : base(message) { }
    }

    private sealed class AgentProcessException : Exception
    {
        public ProcessResult Result { get; }

        public AgentProcessException(string command, ProcessResult result)
            : base(
                $"{command} failed with exit code {result.ExitCode}: " +
                (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut.Trim() : result.StdErr.Trim()))
        {
            Result = result;
        }
    }
}
