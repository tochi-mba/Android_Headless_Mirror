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
    IReadOnlyList<string> RecentLog)
{
    public bool SetupComplete => ScrcpyPath is not null && AdbPath is not null;

    public JsonObject ToJson() => new()
    {
        ["root"] = Root,
        ["setupComplete"] = SetupComplete,
        ["scrcpyPath"] = ScrcpyPath,
        ["scrcpyVersion"] = ScrcpyVersion,
        ["adbPath"] = AdbPath,
        ["adbVersion"] = AdbVersion,
        ["startWithWindows"] = StartWithWindows,
        ["appRunning"] = AppRunning,
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
        ["recentLog"] = new JsonArray(RecentLog.Select(x => (JsonNode)x).ToArray()),
    };

    public string ToText()
    {
        var lines = new List<string>
        {
            "Android Headless Mirror diagnostics",
            $"Folder:        {Root}",
            $"scrcpy:        {(ScrcpyPath is null ? "NOT INSTALLED" : $"{ScrcpyVersion ?? "?"}  ({ScrcpyPath})")}",
            $"ADB:           {(AdbPath is null ? "NOT INSTALLED" : $"{AdbVersion ?? "?"}  ({AdbPath})")}",
            $"Start with Windows: {(StartWithWindows ? "on" : "off")}",
            $"App running:   {(AppRunning ? "yes" : "no")}",
        };

        if (Devices.Count == 0)
        {
            lines.Add("Devices:       none detected");
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
}

public static class Diagnostics
{
    public static async Task<DiagnosticsReport> BuildAsync(AppPaths paths, RexLog log, IProcessRunner runner, CancellationToken cancellationToken = default)
    {
        var tools = ToolLocator.Find(paths);
        string? scrcpyVersion = null, adbVersion = null;
        var devices = new List<DiagnosedDevice>();

        if (tools is not null)
        {
            var scrcpy = await runner.RunAsync(tools.Scrcpy, ["--version"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            scrcpyVersion = FirstLine(scrcpy.StdOut);

            var adb = await runner.RunAsync(tools.Adb, ["version"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            adbVersion = FirstLine(adb.StdOut);

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
                        identity = null;
                    }
                }

                devices.Add(new DiagnosedDevice(device, identity, DeviceStateText.Describe([device])));
            }
        }

        var running = await new IpcClient().IsAppRunningAsync(cancellationToken).ConfigureAwait(false);

        return new DiagnosticsReport(
            paths.Root,
            tools?.Scrcpy,
            scrcpyVersion,
            tools?.Adb,
            adbVersion,
            StartupRegistration.IsEnabled(),
            running,
            devices,
            log.Tail(30));
    }

    private static string? FirstLine(string text)
    {
        var line = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
        return line;
    }
}
