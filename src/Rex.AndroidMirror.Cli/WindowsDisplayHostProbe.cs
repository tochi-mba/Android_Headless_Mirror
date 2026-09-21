namespace Rex.AndroidMirror.Cli;

public interface IDisplayHostProbe
{
    Task<DisplayHostCapabilities> ProbeAsync(CancellationToken cancellationToken = default);
    Task<int> OpenReceiverSetupAsync(CancellationToken cancellationToken = default);
}

public sealed class WindowsDisplayHostProbe : IDisplayHostProbe
{
    private readonly IProcessRunner _runner;
    private readonly string _workingDirectory;

    public WindowsDisplayHostProbe(IProcessRunner runner, string workingDirectory)
    {
        _runner = runner;
        _workingDirectory = workingDirectory;
    }

    public async Task<DisplayHostCapabilities> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var feature = await ProbeWirelessDisplayFeatureAsync(cancellationToken);
        var receive = await ProbeMiracastReceiveAsync(cancellationToken);

        var detail = feature switch
        {
            CapabilityState.Reported when receive == CapabilityState.Reported =>
                "Windows Wireless Display is installed and the Wi-Fi driver reports Miracast support.",
            CapabilityState.Unsupported =>
                "Windows Wireless Display is not installed. Install the optional Wireless Display feature first.",
            _ when receive == CapabilityState.Unsupported =>
                "The current Wi-Fi/display driver stack reports that Wireless Display is unsupported.",
            _ =>
                "Windows could not conclusively verify the complete Miracast receive path."
        };

        return new DisplayHostCapabilities(feature, receive, detail);
    }

    public Task<int> OpenReceiverSetupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _runner.OpenAsync("ms-settings:project", _workingDirectory);
    }

    private async Task<CapabilityState> ProbeWirelessDisplayFeatureAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(
                "powershell.exe",
                new[]
                {
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "(Get-WindowsCapability -Online -Name 'App.WirelessDisplay.Connect~~~~0.0.1.0' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty State)"
                },
                _workingDirectory,
                captureOutput: true,
                cancellationToken);

            if (!result.Ok)
                return CapabilityState.Unknown;

            var output = result.StdOut.Trim();
            if (output.Equals("Installed", StringComparison.OrdinalIgnoreCase))
                return CapabilityState.Reported;

            if (output.Equals("NotPresent", StringComparison.OrdinalIgnoreCase) ||
                output.Equals("Removed", StringComparison.OrdinalIgnoreCase))
                return CapabilityState.Unsupported;

            return CapabilityState.Unknown;
        }
        catch
        {
            return CapabilityState.Unknown;
        }
    }

    private async Task<CapabilityState> ProbeMiracastReceiveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(
                "netsh.exe",
                new[] { "wlan", "show", "drivers" },
                _workingDirectory,
                captureOutput: true,
                cancellationToken);

            if (!result.Ok)
                return CapabilityState.Unknown;

            var output = result.StdOut;
            var marker = "Wireless Display Supported";
            var index = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return CapabilityState.Unknown;

            var lineEnd = output.IndexOfAny(new[] { '\r', '\n' }, index);
            var line = lineEnd < 0 ? output[index..] : output[index..lineEnd];

            if (line.Contains("Yes", StringComparison.OrdinalIgnoreCase))
                return CapabilityState.Reported;

            if (line.Contains("No", StringComparison.OrdinalIgnoreCase))
                return CapabilityState.Unsupported;

            return CapabilityState.Unknown;
        }
        catch
        {
            return CapabilityState.Unknown;
        }
    }
}
