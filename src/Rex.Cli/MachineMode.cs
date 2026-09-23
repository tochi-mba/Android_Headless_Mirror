using System.Text.Json;
using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Cli;

public sealed record MachineResult(int ExitCode, string Json);

/// <summary>
/// The agent contract: <c>rex agent &lt;command&gt;</c>, <c>rex --json ...</c> and <c>rex --plain ...</c>
/// all emit exactly one JSON document and exit 0 on success, 1 on failure. Nothing here prompts.
/// </summary>
public static class MachineMode
{
    public const int ProtocolVersion = 2;

    /// <summary>Returns the command arguments when machine mode was requested, otherwise null.</summary>
    public static string[]? ExtractArguments(string[] args)
    {
        var hasFlag = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase) || a.Equals("--plain", StringComparison.OrdinalIgnoreCase));
        var agentAlias = args.Length > 0 && args[0].Equals("agent", StringComparison.OrdinalIgnoreCase);
        if (!hasFlag && !agentAlias)
        {
            return null;
        }

        var filtered = args.Where(a => !a.Equals("--json", StringComparison.OrdinalIgnoreCase) && !a.Equals("--plain", StringComparison.OrdinalIgnoreCase)).ToArray();
        return filtered.Length > 0 && filtered[0].Equals("agent", StringComparison.OrdinalIgnoreCase) ? filtered[1..] : filtered;
    }

    public static async Task<MachineResult> RunAsync(string[] args, CliContext context)
    {
        var positional = Arguments.Positional(args);
        var command = positional.Length == 0 ? "capabilities" : positional[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "capabilities" or "help" => Success(command, Capabilities(context)),
                "status" => Success(command, await StatusAsync(context).ConfigureAwait(false)),
                "devices" => Success(command, await DevicesAsync(context).ConfigureAwait(false)),
                "diagnostics" => Success(command, (await Diagnostics.BuildAsync(context.Paths, context.Log, context.Runner).ConfigureAwait(false)).ToJson()),
                "open" or "start" => await OpenAsync(context).ConfigureAwait(false),
                "stop" => await ForwardAsync(context, "stop", new IpcRequest("session-stop")).ConfigureAwait(false),
                "quit" => await ForwardAsync(context, "quit", new IpcRequest("quit")).ConfigureAwait(false),
                "action" => await ActionAsync(context, positional, Arguments.Option(args, "--serial")).ConfigureAwait(false),
                "zoom" => await ZoomAsync(context, positional).ConfigureAwait(false),
                "screenshot" => await ScreenshotAsync(context, Arguments.Option(args, "--serial")).ConfigureAwait(false),
                "phone" => await PhoneAsync(context, positional, Arguments.Option(args, "--serial")).ConfigureAwait(false),
                "android" => await AndroidAsync(context, positional, Arguments.Option(args, "--serial"), Arguments.Option(args, "--filter")).ConfigureAwait(false),
                "config" => Config(context, positional),
                "autostart" => Autostart(context, positional),
                "lock-mode" => LockMode(context, positional),
                "reset-lock" => Success("reset-lock", new JsonObject { ["cleared"] = new StateStore(context.Paths.State).ResetLockScreen(positional.Length >= 2 ? positional[1] : "ALL") }),
                _ => throw new ArgumentException($"Unknown command '{command}'. Use 'capabilities'."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException or FormatException or UnauthorizedAccessException)
        {
            return new MachineResult(1, FailureJson(command, ex));
        }
    }

    private static JsonObject Capabilities(CliContext context) => new()
    {
        ["protocolVersion"] = ProtocolVersion,
        ["mode"] = "non-interactive-json",
        ["commands"] = new JsonArray("capabilities", "status", "devices", "diagnostics", "open", "stop", "quit", "action", "zoom", "screenshot", "phone", "android", "config", "autostart", "lock-mode", "reset-lock"),
        ["actions"] = new JsonArray(MirrorActions.All.Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["label"] = a.Label, ["kind"] = a.Kind.ToString().ToLowerInvariant(), ["detail"] = a.Detail }).ToArray()),
        ["phoneSettings"] = new JsonArray(FriendlySettings.Ids.Concat(["rotation"]).Select(x => (JsonNode)x).ToArray()),
        ["androidNamespaces"] = new JsonArray(AndroidSettings.Namespaces.Select(x => (JsonNode)x).ToArray()),
        ["configPaths"] = new JsonArray(context.Config.Flatten().Select(x => (JsonNode)x.Path).ToArray()),
        ["serialRule"] = "Omit --serial only when exactly one authorized phone is connected.",
        ["output"] = new JsonObject { ["stdout"] = "exactly one JSON document", ["prompts"] = false, ["successExitCode"] = 0, ["failureExitCode"] = 1 },
    };

    private static async Task<JsonObject> StatusAsync(CliContext context)
    {
        var response = await context.Ipc.SendAsync(new IpcRequest("status")).ConfigureAwait(false);
        var tools = ToolLocator.Find(context.Paths);
        var result = new JsonObject
        {
            ["appRunning"] = response is { Ok: true },
            ["setupComplete"] = tools is not null,
            ["startWithWindows"] = StartupRegistration.IsEnabled(),
            ["app"] = response is { Ok: true } ? response.Data?.DeepClone() : null,
        };

        if (response is not { Ok: true } && tools is not null)
        {
            result["devices"] = await DevicesAsync(context).ConfigureAwait(false);
        }

        return result;
    }

    private static async Task<JsonArray> DevicesAsync(CliContext context)
    {
        var devices = await context.RequireAdb().ListDevicesAsync().ConfigureAwait(false);
        return new JsonArray(devices.Select(d => (JsonNode)new JsonObject
        {
            ["serial"] = d.Serial,
            ["state"] = d.State,
            ["transport"] = d.Transport,
            ["model"] = d.Model,
        }).ToArray());
    }

    private static async Task<MachineResult> OpenAsync(CliContext context)
    {
        if (await context.Ipc.IsAppRunningAsync().ConfigureAwait(false))
        {
            await context.Ipc.SendAsync(new IpcRequest("show")).ConfigureAwait(false);
            return Success("open", new JsonObject { ["started"] = false, ["alreadyRunning"] = true });
        }

        if (!File.Exists(context.AppExecutable))
        {
            throw new InvalidOperationException("RexMirror.exe was not found next to rex.exe.");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(context.AppExecutable) { UseShellExecute = true, WorkingDirectory = context.Paths.Root });
        return Success("open", new JsonObject { ["started"] = true, ["alreadyRunning"] = false });
    }

    private static async Task<MachineResult> ActionAsync(CliContext context, string[] positional, string? serial)
    {
        Arguments.Require(positional, 2, "action <name> [--serial S]");
        var action = MirrorActions.Find(positional[1]) ?? throw new ArgumentException($"Unknown action '{positional[1]}'.");
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
            return result.Ok
                ? Success("action", new JsonObject { ["action"] = action.Id, ["serial"] = target })
                : throw new InvalidOperationException(result.Text);
        }

        return await ForwardAsync(context, "action", new IpcRequest("action", new Dictionary<string, string> { ["name"] = action.Id })).ConfigureAwait(false);
    }

    private static Task<MachineResult> ZoomAsync(CliContext context, string[] positional)
    {
        Arguments.Require(positional, 2, "zoom <in|out|reset>");
        return ForwardAsync(context, "zoom", new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = positional[1] }));
    }

    private static async Task<MachineResult> ScreenshotAsync(CliContext context, string? serial)
    {
        var adb = context.RequireAdb();
        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);
        var bytes = await adb.ScreencapAsync(target).ConfigureAwait(false) ?? throw new InvalidOperationException("The phone did not return a screenshot.");
        var directory = context.Paths.ScreenshotFolder(context.Config.Load().App.ScreenshotDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "android-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".png");
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        return Success("screenshot", new JsonObject { ["serial"] = target, ["path"] = path });
    }

    private static async Task<MachineResult> PhoneAsync(CliContext context, string[] positional, string? serial)
    {
        Arguments.Require(positional, 2, "phone <get|set> ...");
        var adb = context.RequireAdb();
        var target = await context.ResolveSerialAsync(serial).ConfigureAwait(false);

        if (positional[1] == "get")
        {
            var state = await adb.GetFriendlyStateAsync(target).ConfigureAwait(false);
            return Success("phone.get", new JsonObject(state.Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, p.Value))));
        }

        Arguments.Require(positional, 4, "phone set <setting> <value> [--serial S]");
        var result = positional[2] == "rotation"
            ? await adb.SetRotationOverrideAsync(target, positional[3]).ConfigureAwait(false)
            : await adb.ApplyFriendlySettingAsync(target, positional[2], positional[3]).ConfigureAwait(false);
        return result.Ok
            ? Success("phone.set", new JsonObject { ["serial"] = target, ["setting"] = positional[2], ["value"] = positional[3], ["text"] = result.Text })
            : throw new InvalidOperationException(result.Text);
    }

    private static async Task<MachineResult> AndroidAsync(CliContext context, string[] positional, string? serial, string? filter)
    {
        Arguments.Require(positional, 3, "android <list|get|set|delete> <namespace> [key] [value]");
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

                var filtered = rows.Where(r => string.IsNullOrEmpty(filter) || r.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));
                return Success("android.list", new JsonObject
                {
                    ["serial"] = target,
                    ["namespace"] = ns,
                    ["rows"] = new JsonArray(filtered.Select(r => (JsonNode)new JsonObject { ["key"] = r.Key, ["value"] = r.Value, ["risk"] = r.Risk }).ToArray()),
                });
            }

            case "get":
            {
                Arguments.Require(positional, 4, "android get <namespace> <key>");
                var result = await adb.GetSettingAsync(target, ns, positional[3]).ConfigureAwait(false);
                return result.Ok ? Success("android.get", new JsonObject { ["key"] = positional[3], ["value"] = result.Text }) : throw new InvalidOperationException(result.Text);
            }

            case "set":
            {
                Arguments.Require(positional, 5, "android set <namespace> <key> <value>");
                var result = await adb.PutSettingAsync(target, ns, positional[3], positional[4]).ConfigureAwait(false);
                return result.Ok ? Success("android.set", new JsonObject { ["key"] = positional[3], ["value"] = positional[4] }) : throw new InvalidOperationException(result.Text);
            }

            case "delete":
            {
                Arguments.Require(positional, 4, "android delete <namespace> <key>");
                var result = await adb.DeleteSettingAsync(target, ns, positional[3]).ConfigureAwait(false);
                return result.Ok ? Success("android.delete", new JsonObject { ["key"] = positional[3] }) : throw new InvalidOperationException(result.Text);
            }

            default:
                throw new ArgumentException("android expects list, get, set or delete.");
        }
    }

    private static MachineResult Config(CliContext context, string[] positional)
    {
        Arguments.Require(positional, 2, "config <list|get|set|restore> ...");
        switch (positional[1].ToLowerInvariant())
        {
            case "list":
                return Success("config.list", new JsonObject
                {
                    ["rows"] = new JsonArray(context.Config.Flatten().Select(l => (JsonNode)new JsonObject { ["path"] = l.Path, ["value"] = l.Value, ["type"] = l.Kind.ToString() }).ToArray()),
                });
            case "get":
            {
                Arguments.Require(positional, 3, "config get <path>");
                var leaf = context.Config.Get(positional[2]);
                return Success("config.get", new JsonObject { ["path"] = leaf.Path, ["value"] = leaf.Value, ["type"] = leaf.Kind.ToString() });
            }

            case "set":
            {
                Arguments.Require(positional, 4, "config set <path> <value>");
                var leaf = context.Config.Set(positional[2], positional[3]);
                return Success("config.set", new JsonObject { ["path"] = leaf.Path, ["value"] = leaf.Value, ["type"] = leaf.Kind.ToString(), ["backup"] = context.Config.BackupPath });
            }

            case "restore":
                return Success("config.restore", new JsonObject { ["restored"] = context.Config.RestoreBackup() });
            default:
                throw new ArgumentException("config expects list, get, set or restore.");
        }
    }

    private static MachineResult Autostart(CliContext context, string[] positional)
    {
        Arguments.Require(positional, 2, "autostart <on|off>");
        var enabled = positional[1].ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => throw new ArgumentException("autostart expects on or off."),
        };

        if (enabled)
        {
            StartupRegistration.Enable(context.AppExecutable);
        }
        else
        {
            StartupRegistration.Disable();
        }

        return Success("autostart", new JsonObject { ["enabled"] = enabled });
    }

    private static MachineResult LockMode(CliContext context, string[] positional)
    {
        Arguments.Require(positional, 3, "lock-mode <serial> <pattern|other|none>");
        var mode = positional[2].ToLowerInvariant();
        if (!LockScreenModes.IsValid(mode))
        {
            throw new ArgumentException("lock-mode expects pattern, other or none.");
        }

        new StateStore(context.Paths.State).SetLockScreenMode(positional[1], mode);
        return Success("lock-mode", new JsonObject { ["serial"] = positional[1], ["mode"] = mode });
    }

    private static async Task<MachineResult> ForwardAsync(CliContext context, string command, IpcRequest request)
    {
        var response = await context.Ipc.SendAsync(request).ConfigureAwait(false);
        if (response is null)
        {
            throw new InvalidOperationException("Android Headless Mirror is not running. Use 'open' first.");
        }

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "The app reported a failure.");
        }

        return Success(command, response.Data?.DeepClone() ?? new JsonObject());
    }

    private static MachineResult Success(string command, JsonNode? data) =>
        new(0, new JsonObject { ["ok"] = true, ["protocolVersion"] = ProtocolVersion, ["command"] = command, ["data"] = data }.ToJsonString(Compact));

    public static string FailureJson(string command, Exception ex) =>
        new JsonObject
        {
            ["ok"] = false,
            ["protocolVersion"] = ProtocolVersion,
            ["command"] = command,
            ["error"] = new JsonObject { ["type"] = ex.GetType().Name, ["message"] = ex.Message },
        }.ToJsonString(Compact);

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
}
