using System.Text.Json;
using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

internal sealed class TempPackage : IDisposable
{
    public string Root { get; }
    public AppPaths Paths { get; }
    public ConfigStore Config { get; }

    public TempPackage()
    {
        Root = Path.Combine(Path.GetTempPath(), "rex-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        File.WriteAllText(Path.Combine(Root, "Setup.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Start-PhoneMirror.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Stop-PhoneMirror.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Diagnostics.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Install-Autostart.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Remove-Autostart.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Install-Rex-Shortcut.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Remove-Rex-Shortcut.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "Reset-LockScreenChoices.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "ControlCenter.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "RexBridge.ps1"), "# test");
        File.WriteAllText(Path.Combine(Root, "START_NOW.bat"), "@echo off");

        File.WriteAllText(Path.Combine(Root, "config.json"), """
        {
          "WindowTitle": "Android Device",
          "PollSeconds": 1,
          "RetrySeconds": 4,
          "TurnPhysicalScreenOff": true,
          "StayAwakeWhenUsb": true,
          "KeepActiveDuringMirror": true,
          "DismissKeyguardWhenPossible": true,
          "PreferUsb": true,
          "RestartOnUnexpectedExit": true,
          "MaxSize": 1920,
          "MaxFps": 60,
          "VideoBitRate": "12M",
          "Wireless": {
            "Enabled": false,
            "Port": 5555,
            "ManualHosts": []
          },
          "PatternOverlay": {
            "Enabled": true,
            "PromptPerDevice": true
          },
          "MirrorChrome": {
            "NativeTouchpadGestures": true,
            "MaxZoom": 4
          },
          "ControlCenter": {
            "OpenOnLaunch": false,
            "ScreenshotDirectory": "captures/screenshots"
          },
          "ScrcpySession": {
            "VideoCodec": "h264",
            "AudioEnabled": true,
            "AudioBufferMs": 50
          },
          "Display": {
            "DefaultTransport": "scrcpy",
            "ProtectedContentPolicy": "prompt",
            "WindowsWirelessDisplay": {
              "Enabled": true,
              "AutoOpenReceiver": true
            },
            "SamsungDex": {
              "Enabled": true
            }
          },
          "Root": {
            "Enabled": true,
            "ProbeOnConnect": true,
            "RequestAutomatically": false,
            "AllowReadOnly": true,
            "AllowReversible": false,
            "AllowSystemChanges": false,
            "AllowDeviceCritical": false,
            "RawShellEnabled": false,
            "RequestTimeoutSeconds": 15,
            "CommandTimeoutSeconds": 10,
            "MaxOutputCharacters": 262144
          },
          "ExtraScrcpyArgs": ""
        }
        """);

        Paths = AppPaths.FromCandidate(Root);
        Config = new ConfigStore(Paths.Config);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}

internal sealed record ProcessCall(
    string Kind,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessCall> Calls { get; } = [];
    public ProcessResult Result { get; set; } = new(0, "", "");
    public int OpenExitCode { get; set; } = 0;
    public int DetachedExitCode { get; set; } = 0;

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        bool captureOutput = true,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new("run", fileName, arguments.ToArray(), workingDirectory));
        return Task.FromResult(Result);
    }

    public Task<ProcessResult> RunPowerShellAsync(
        string scriptPath,
        IEnumerable<string>? scriptArguments = null,
        bool captureOutput = true,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new(
            "powershell",
            scriptPath,
            scriptArguments?.ToArray() ?? [],
            Path.GetDirectoryName(scriptPath) ?? ""));
        return Task.FromResult(Result);
    }

    public Task<int> OpenAsync(string target, string? workingDirectory = null)
    {
        Calls.Add(new("open", target, [], workingDirectory ?? ""));
        return Task.FromResult(OpenExitCode);
    }

    public Task<int> StartDetachedAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        Calls.Add(new("detached", fileName, arguments.ToArray(), workingDirectory));
        return Task.FromResult(DetachedExitCode);
    }
}

internal sealed record BridgeCall(
    string Action,
    string? Serial,
    string? Name,
    string? Namespace,
    string? Key,
    string? Value);

internal sealed class FakeBridgeClient : IBridgeClient
{
    private readonly Queue<RexStatus> _statuses = new();

    public List<BridgeCall> Calls { get; } = [];
    public List<RexDevice> Devices { get; } = [];
    public int DeviceQueryCount { get; private set; }
    public List<(string Key, string Value, string Risk)> AndroidSettings { get; } = [];

    public RexStatus DefaultStatus { get; set; } = new(
        true, false, false, false, false, "", "", []);

    public void EnqueueStatus(RexStatus status) => _statuses.Enqueue(status);

    public Task<RexStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = _statuses.Count > 0 ? _statuses.Dequeue() : DefaultStatus;
        return Task.FromResult(status);
    }

    public Task<IReadOnlyList<RexDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        DeviceQueryCount++;
        return Task.FromResult<IReadOnlyList<RexDevice>>(Devices.ToArray());
    }

    public Task<IReadOnlyList<(string Key, string Value, string Risk)>> ListAndroidSettingsAsync(
        string serial,
        string ns,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(string Key, string Value, string Risk)>>(AndroidSettings.ToArray());

    public Task<JsonDocument> InvokeAsync(
        string action,
        string? serial = null,
        string? name = null,
        string? ns = null,
        string? key = null,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new(action, serial, name, ns, key, value));

        var payload = action == "screenshot"
            ? """{"Ok":true,"Text":"Screenshot saved.","Path":"captures/screenshots/test.png"}"""
            : """{"Ok":true,"Text":"OK"}""";

        return Task.FromResult(JsonDocument.Parse(payload));
    }
}


internal sealed record AndroidShellCall(
    string AdbPath,
    string Serial,
    PrivilegedCommand Command,
    RootExecutionMode? RootMode);

internal sealed class FakeAndroidShellRunner : IAndroidShellRunner
{
    private readonly Dictionary<string, Queue<AndroidCommandResult>> _results =
        new(StringComparer.Ordinal);

    public List<AndroidShellCall> Calls { get; } = [];

    public AndroidCommandResult DefaultResult { get; set; } =
        new(1, "", "not configured", false, false);

    public void Enqueue(
        string commandId,
        AndroidCommandResult result)
    {
        if (!_results.TryGetValue(commandId, out var queue))
        {
            queue = new Queue<AndroidCommandResult>();
            _results[commandId] = queue;
        }

        queue.Enqueue(result);
    }

    public void Success(string commandId, string stdout = "") =>
        Enqueue(commandId, new AndroidCommandResult(0, stdout, "", false, false));

    public void Failure(string commandId, string stderr = "failed", int exitCode = 1) =>
        Enqueue(commandId, new AndroidCommandResult(exitCode, "", stderr, false, false));

    public void Timeout(string commandId) =>
        Enqueue(commandId, new AndroidCommandResult(-1, "", "timed out", true, false));

    public Task<AndroidCommandResult> RunAsync(
        string adbPath,
        string serial,
        PrivilegedCommand command,
        RootExecutionMode? rootMode = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new(adbPath, serial, command, rootMode));

        if (_results.TryGetValue(command.Id, out var queue) && queue.Count > 0)
            return Task.FromResult(queue.Dequeue());

        return Task.FromResult(DefaultResult);
    }
}
