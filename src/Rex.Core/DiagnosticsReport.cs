using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

public sealed record DiagnosedDevice(AdbDevice Device, DeviceIdentity? Identity, string Advice);

public sealed record DiagnosticsReport(
    string Root,
    string? ScrcpyPath,
    string? ScrcpyVersion,
    string? AdbPath,
    string? AdbVersion,
    bool StartWithWindows,
    bool AppRunning,
    IReadOnlyList<DiagnosedDevice> Devices,
    IReadOnlyList<AdbInterface> UsbInterfaces,
    IReadOnlyList<string> RecentLog)
{
    public string AppVersion { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string Framework { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string CurrentDirectory { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string ConfigPath { get; init; } = string.Empty;
    public string ConfigStatus { get; init; } = string.Empty;
    public string StatePath { get; init; } = string.Empty;
    public string StateStatus { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public string? StartupCommand { get; init; }
    public bool? StartupCommandMatchesThisExecutable { get; init; }
    public string? ScrcpyCheck { get; init; }
    public string? AdbCheck { get; init; }

    public bool SetupComplete => ScrcpyPath is not null && AdbPath is not null;

    public JsonObject ToJson() => new()
    {
        ["root"] = Root,
        ["appVersion"] = AppVersion,
        ["operatingSystem"] = OperatingSystem,
        ["framework"] = Framework,
        ["architecture"] = Architecture,
        ["executablePath"] = ExecutablePath,
        ["currentDirectory"] = CurrentDirectory,
        ["processId"] = ProcessId,
        ["configPath"] = ConfigPath,
        ["configStatus"] = ConfigStatus,
        ["statePath"] = StatePath,
        ["stateStatus"] = StateStatus,
        ["logPath"] = LogPath,
        ["setupComplete"] = SetupComplete,
        ["scrcpyPath"] = ScrcpyPath,
        ["scrcpyVersion"] = ScrcpyVersion,
        ["adbPath"] = AdbPath,
        ["adbVersion"] = AdbVersion,
        ["startWithWindows"] = StartWithWindows,
        ["startupCommand"] = StartupCommand,
        ["startupCommandMatchesThisExecutable"] = StartupCommandMatchesThisExecutable,
        ["appRunning"] = AppRunning,
        ["scrcpyCheck"] = ScrcpyCheck,
        ["adbCheck"] = AdbCheck,
        ["devices"] = new JsonArray(Devices.Select(d => (JsonNode)new JsonObject
        {
            ["serial"] = d.Device.Serial,
            ["state"] = d.Device.State,
            ["transport"] = d.Device.Transport,
            ["name"] = d.Identity?.DisplayName,
            ["model"] = d.Identity?.Model,
            ["android"] = d.Identity?.AndroidVersion,
            ["advice"] = d.Advice,
        }).ToArray()),
        ["usbInterfaces"] = new JsonArray(UsbInterfaces.Select(u => (JsonNode)new JsonObject
        {
            ["instanceId"] = u.InstanceId,
            ["description"] = u.Description,
            ["driver"] = u.Driver,
            ["present"] = u.Present,
            ["registered"] = u.Registered,
            ["unreachable"] = u.Unreachable,
        }).ToArray()),
        ["recentLog"] = new JsonArray(RecentLog.Select(x => (JsonNode)x).ToArray()),
    };

    public string ToText()
    {
        var lines = new List<string>
        {
            "Android Headless Mirror diagnostics",
            $"App version:   {ValueOrUnknown(AppVersion)}",
            $"Windows:       {ValueOrUnknown(OperatingSystem)}",
            $".NET:          {ValueOrUnknown(Framework)} ({ValueOrUnknown(Architecture)})",
            $"Process:       {ProcessId}  {ValueOrUnknown(ExecutablePath)}",
            $"Working dir:   {ValueOrUnknown(CurrentDirectory)}",
            $"Folder:        {Root}",
            $"Config:        {ValueOrUnknown(ConfigStatus)}  ({ValueOrUnknown(ConfigPath)})",
            $"State:         {ValueOrUnknown(StateStatus)}  ({ValueOrUnknown(StatePath)})",
            $"Log:           {ValueOrUnknown(LogPath)}",
            $"scrcpy:        {(ScrcpyPath is null ? "NOT INSTALLED" : $"{ScrcpyVersion ?? "?"}  ({ScrcpyPath})")}",
            $"ADB:           {(AdbPath is null ? "NOT INSTALLED" : $"{AdbVersion ?? "?"}  ({AdbPath})")}",
            $"Start with Windows: {(StartWithWindows ? "on" : "off")}",
            $"Startup command:    {StartupCommand ?? "not registered"}",
            $"App running:   {(AppRunning ? "yes" : "no")}",
        };

        if (StartupCommandMatchesThisExecutable == false)
        {
            lines.Add("Startup warning: Windows is registered to launch a different executable than this one.");
        }

        if (ScrcpyCheck is not null)
        {
            lines.Add("scrcpy check:  " + ScrcpyCheck);
        }

        if (AdbCheck is not null)
        {
            lines.Add("ADB check:     " + AdbCheck);
        }

        if (Devices.Count == 0)
        {
            lines.Add("Devices:       none detected");
        }

        var unreachable = UsbInterfaces.Count(u => u.Unreachable);
        if (unreachable > 0)
        {
            lines.Add($"USB:           {unreachable} ADB interface(s) plugged in but not registered for adb. Run 'rex usb repair' (asks for administrator approval).");
        }

        foreach (var d in Devices)
        {
            lines.Add(string.Empty);
            lines.Add($"Device:        {d.Identity?.DisplayName ?? d.Device.Serial}");
            lines.Add($"Serial:        {d.Device.Serial} ({d.Device.Transport})");
            lines.Add($"ADB state:     {d.Device.State}");
            if (d.Identity is not null)
            {
                lines.Add($"Android:       {d.Identity.AndroidVersion} (API {d.Identity.ApiLevel})");
            }

            lines.Add($"Advice:        {d.Advice}");
        }

        if (RecentLog.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Recent log:");
            lines.AddRange(RecentLog.Select(x => "  " + x));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ValueOrUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;
}

public static class Diagnostics
{
    public static async Task<DiagnosticsReport> BuildAsync(AppPaths paths, RexLog log, IProcessRunner runner, CancellationToken cancellationToken = default)
    {
        var tools = ToolLocator.Find(paths);
        string? scrcpyVersion = null, adbVersion = null, scrcpyCheck = null, adbCheck = null;
        var devices = new List<DiagnosedDevice>();

        if (tools is not null)
        {
            var scrcpy = await runner.RunAsync(tools.Scrcpy, ["--version"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            scrcpyVersion = FirstLine(scrcpy.StdOut);
            scrcpyCheck = CommandCheck(scrcpy);
            if (!scrcpy.Ok)
            {
                log.Warn("The scrcpy diagnostics check failed: " + scrcpy.FailureText);
            }

            var adb = await runner.RunAsync(tools.Adb, ["version"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            adbVersion = FirstLine(adb.StdOut);
            adbCheck = CommandCheck(adb);
            if (!adb.Ok)
            {
                log.Warn("The adb diagnostics check failed: " + adb.FailureText);
            }

            var client = new AdbClient(tools.Adb, runner);
            await client.StartServerAsync(cancellationToken).ConfigureAwait(false);
            foreach (var device in await client.ListDevicesAsync(cancellationToken).ConfigureAwait(false))
            {
                DeviceIdentity? identity = null;
                if (device.IsReady)
                {
                    try
                    {
                        identity = await client.GetIdentityAsync(device.Serial, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException)
                    {
                        log.Warn($"Diagnostics could not read the identity of phone '{device.Serial}'", ex);
                        identity = null;
                    }
                }

                devices.Add(new DiagnosedDevice(device, identity, DeviceStateText.Describe([device])));
            }
        }

        var running = await new IpcClient().IsAppRunningAsync(cancellationToken).ConfigureAwait(false);

        var executable = Environment.ProcessPath ?? string.Empty;
        var startupCommand = StartupRegistration.RegisteredCommand();
        return new DiagnosticsReport(
            paths.Root,
            tools?.Scrcpy,
            scrcpyVersion,
            tools?.Adb,
            adbVersion,
            StartupRegistration.IsEnabled(),
            running,
            devices,
            UsbAdbInterfaces.Scan(),
            log.Tail(200))
        {
            AppVersion = typeof(Diagnostics).Assembly.GetName().Version?.ToString() ?? string.Empty,
            OperatingSystem = RuntimeInformation.OSDescription,
            Framework = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ExecutablePath = executable,
            CurrentDirectory = Environment.CurrentDirectory,
            ProcessId = Environment.ProcessId,
            ConfigPath = paths.Config,
            ConfigStatus = InspectJsonFile(paths.Config, isConfig: true),
            StatePath = paths.State,
            StateStatus = InspectJsonFile(paths.State, isConfig: false),
            LogPath = log.Path,
            StartupCommand = startupCommand,
            StartupCommandMatchesThisExecutable = startupCommand is null ? null : StartupMatches(startupCommand, executable),
            ScrcpyCheck = scrcpyCheck,
            AdbCheck = adbCheck,
        };
    }

    private static string? FirstLine(string text)
    {
        var line = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
        return line;
    }

    private static string CommandCheck(ProcessResult result) =>
        result.Ok ? "ok" : result.FailureText;

    private static string InspectJsonFile(string path, bool isConfig)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return isConfig ? "missing" : "not created yet";
            }

            if (isConfig)
            {
                _ = ConfigFile.Load(path);
            }
            else
            {
                _ = JsonNode.Parse(File.ReadAllText(path));
            }

            return $"valid JSON, {info.Length} bytes, changed {info.LastWriteTime:yyyy-MM-dd HH:mm:ss zzz}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return "unreadable: " + ExceptionDiagnostics.Summary(ex);
        }
    }

    private static bool StartupMatches(string command, string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        var expected = '"' + Path.GetFullPath(executable) + '"';
        return command.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
    }
}
