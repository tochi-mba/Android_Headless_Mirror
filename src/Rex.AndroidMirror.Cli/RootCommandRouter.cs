namespace Rex.AndroidMirror.Cli;

public sealed record RootCommandResponse(string Command, object Data);

public static class RootCommandRouter
{
    public static async Task<RootCommandResponse> RunAsync(
        string[] args,
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config,
        CancellationToken cancellationToken = default)
    {
        if (args.Length < 2)
            throw new ArgumentException(
                "Usage: root <status|probe|request|capabilities|clear|diagnostics|files|processes|process|app|hardware|network|logs|properties> ...");

        var serial = await ResolveSerialAsync(
            bridge,
            Option(args, "--serial"),
            cancellationToken);

        var manager = new RootManager(paths, runner, bridge, config);
        var features = new RootFeatureService(manager, config);
        var verb = args[1].ToLowerInvariant();

        switch (verb)
        {
            case "status":
            case "probe":
            case "capabilities":
                return new RootCommandResponse(
                    $"root.{verb}",
                    await manager.ProbePassiveAsync(serial, cancellationToken));

            case "request":
                return new RootCommandResponse(
                    "root.request",
                    await manager.RequestAsync(serial, cancellationToken));

            case "clear":
                manager.ClearCachedSession(serial);
                return new RootCommandResponse(
                    "root.clear",
                    new
                    {
                        serial,
                        cleared = true,
                        message = "Cached root verification cleared. The root provider itself was not modified."
                    });

            case "diagnostics":
                return new RootCommandResponse(
                    "root.diagnostics",
                    await features.DiagnosticsAsync(serial, cancellationToken));

            case "files":
                return await FilesAsync(args, serial, features, cancellationToken);

            case "processes":
                return new RootCommandResponse(
                    "root.processes",
                    await features.ProcessesAsync(serial, cancellationToken));

            case "process":
            {
                Require(args, 3, "root process <pid> [--serial SERIAL]");
                if (!int.TryParse(
                    args[2],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var pid))
                    throw new ArgumentException("PID must be an integer.");

                return new RootCommandResponse(
                    "root.process",
                    await features.ProcessAsync(serial, pid, cancellationToken));
            }

            case "app":
            {
                Require(args, 3, "root app <package> [--serial SERIAL]");
                return new RootCommandResponse(
                    "root.app",
                    await features.AppAsync(serial, args[2], cancellationToken));
            }

            case "hardware":
                return new RootCommandResponse(
                    "root.hardware",
                    await features.HardwareAsync(serial, cancellationToken));

            case "network":
                return new RootCommandResponse(
                    "root.network",
                    await features.NetworkAsync(serial, cancellationToken));

            case "logs":
            {
                Require(args, 3, "root logs kernel [--serial SERIAL]");
                if (!args[2].Equals("kernel", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Root v1 currently supports only 'root logs kernel'.");

                return new RootCommandResponse(
                    "root.logs.kernel",
                    await features.KernelLogsAsync(serial, cancellationToken));
            }

            case "properties":
                return new RootCommandResponse(
                    "root.properties",
                    await features.PropertiesAsync(serial, cancellationToken));

            default:
                throw new ArgumentException(
                    "root expects status, probe, request, capabilities, clear, diagnostics, files, processes, process, app, hardware, network, logs, or properties.");
        }
    }

    private static async Task<RootCommandResponse> FilesAsync(
        string[] args,
        string serial,
        RootFeatureService features,
        CancellationToken cancellationToken)
    {
        Require(args, 4, "root files <list|stat|read> <absolute-path> [--serial SERIAL]");
        return args[2].ToLowerInvariant() switch
        {
            "list" => new RootCommandResponse(
                "root.files.list",
                await features.ListFilesAsync(serial, args[3], cancellationToken)),
            "stat" => new RootCommandResponse(
                "root.files.stat",
                await features.StatFileAsync(serial, args[3], cancellationToken)),
            "read" => new RootCommandResponse(
                "root.files.read",
                await features.ReadFileAsync(serial, args[3], cancellationToken)),
            _ => throw new ArgumentException("root files expects list, stat, or read.")
        };
    }

    private static async Task<string> ResolveSerialAsync(
        IBridgeClient bridge,
        string? requested,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        var devices = (await bridge.GetDevicesAsync(cancellationToken))
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
