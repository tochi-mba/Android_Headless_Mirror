using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class WindowsDisplayHostProbeTests
{
    [Theory]
    [InlineData("Installed\r\n", CapabilityState.Reported)]
    [InlineData("NotPresent\r\n", CapabilityState.Unsupported)]
    [InlineData("Removed\r\n", CapabilityState.Unsupported)]
    [InlineData("", CapabilityState.Unknown)]
    public async Task Probe_MapsWirelessDisplayFeatureState(
        string featureOutput,
        CapabilityState expected)
    {
        var runner = new QueueProcessRunner(
            new ProcessResult(0, featureOutput, ""),
            new ProcessResult(
                0,
                "Wireless Display Supported: Yes (Graphics Driver: Yes, Wi-Fi Driver: Yes)",
                ""));

        var result = await new WindowsDisplayHostProbe(runner, "C:\\test").ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.WirelessDisplayFeature);
    }

    [Theory]
    [InlineData("Wireless Display Supported: Yes (Graphics Driver: Yes, Wi-Fi Driver: Yes)", CapabilityState.Reported)]
    [InlineData("Wireless Display Supported: No (Graphics Driver: Yes, Wi-Fi Driver: No)", CapabilityState.Unsupported)]
    [InlineData("driver output without marker", CapabilityState.Unknown)]
    public async Task Probe_MapsMiracastDriverState(
        string driverOutput,
        CapabilityState expected)
    {
        var runner = new QueueProcessRunner(
            new ProcessResult(0, "Installed", ""),
            new ProcessResult(0, driverOutput, ""));

        var result = await new WindowsDisplayHostProbe(runner, "C:\\test").ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.MiracastReceive);
    }

    [Fact]
    public async Task Probe_CommandFailureIsInconclusiveNotFatal()
    {
        var runner = new QueueProcessRunner(
            new ProcessResult(1, "", "access denied"),
            new ProcessResult(1, "", "no wlan"));

        var result = await new WindowsDisplayHostProbe(runner, "C:\\test").ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityState.Unknown, result.WirelessDisplayFeature);
        Assert.Equal(CapabilityState.Unknown, result.MiracastReceive);
    }

    [Fact]
    public async Task OpenReceiverSetup_UsesProjectingToThisPcSettingsUri()
    {
        var runner = new QueueProcessRunner();
        var probe = new WindowsDisplayHostProbe(runner, "C:\\test");

        var code = await probe.OpenReceiverSetupAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, code);
        Assert.Equal("ms-settings:project", runner.OpenedTarget);
    }
}

internal sealed class QueueProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results;

    public string? OpenedTarget { get; private set; }

    public QueueProcessRunner(params ProcessResult[] results) =>
        _results = new Queue<ProcessResult>(results);

    public Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        bool captureOutput = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _results.Count > 0
                ? _results.Dequeue()
                : new ProcessResult(0, "", ""));

    public Task<ProcessResult> RunPowerShellAsync(
        string scriptPath,
        IEnumerable<string>? scriptArguments = null,
        bool captureOutput = true,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            "powershell.exe",
            scriptArguments ?? Array.Empty<string>(),
            Path.GetDirectoryName(scriptPath) ?? "",
            captureOutput,
            cancellationToken);

    public Task<int> OpenAsync(string target, string? workingDirectory = null)
    {
        OpenedTarget = target;
        return Task.FromResult(0);
    }

    public Task<int> StartDetachedAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory) =>
        Task.FromResult(0);
}
