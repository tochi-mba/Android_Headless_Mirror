namespace Rex.AndroidMirror.Cli;

public sealed class DisplayManager
{
    private readonly AppPaths _paths;
    private readonly IProcessRunner _runner;
    private readonly IBridgeClient _bridge;
    private readonly ConfigStore _config;
    private readonly IDisplayHostProbe _hostProbe;
    private readonly DisplayVerificationStore _verification;

    public DisplayManager(
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge,
        ConfigStore config,
        IDisplayHostProbe? hostProbe = null,
        DisplayVerificationStore? verification = null)
    {
        _paths = paths;
        _runner = runner;
        _bridge = bridge;
        _config = config;
        _hostProbe = hostProbe ?? new WindowsDisplayHostProbe(runner, paths.Root);
        _verification = verification ?? new DisplayVerificationStore(paths.DisplayVerification);
    }

    public async Task<DisplayProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await _bridge.GetStatusAsync(cancellationToken);
        var host = await _hostProbe.ProbeAsync(cancellationToken);

        var authorized = status.Devices.Where(x => x.State == "device").ToArray();
        var adbControl = authorized.Length > 0
            ? CapabilityState.Reported
            : CapabilityState.Unknown;

        var samsungDex = authorized.Length == 0
            ? CapabilityState.Unknown
            : authorized.Any(x => x.Manufacturer.Contains("samsung", StringComparison.OrdinalIgnoreCase))
                ? CapabilityState.Reported
                : CapabilityState.Unsupported;

        var scrcpyAvailable =
            status.SetupComplete &&
            !string.IsNullOrWhiteSpace(status.ScrcpyPath)
                ? CapabilityState.Reported
                : CapabilityState.Unsupported;

        var wirelessAvailable = CombineWirelessAvailability(host);
        var wirelessVerification = _verification.Get(DisplayTransportIds.WindowsMiracast);

        var protectedOutput = wirelessVerification.ProtectedPlayback switch
        {
            VerificationOutcome.Passed => CapabilityState.Verified,
            VerificationOutcome.Failed => CapabilityState.Unsupported,
            _ => CapabilityState.Unknown
        };

        var normalVideo = wirelessVerification.NormalPlayback switch
        {
            VerificationOutcome.Passed => CapabilityState.Verified,
            VerificationOutcome.Failed => CapabilityState.Unsupported,
            _ => wirelessAvailable
        };

        var transports = new[]
        {
            new DisplayTransportCapabilities(
                DisplayTransportIds.Scrcpy,
                "scrcpy",
                "capture",
                scrcpyAvailable,
                scrcpyAvailable,
                scrcpyAvailable,
                scrcpyAvailable,
                scrcpyAvailable,
                scrcpyAvailable,
                CapabilityState.Unsupported,
                VerificationOutcome.Passed,
                VerificationOutcome.Failed,
                new[]
                {
                    "Default low-latency capture transport.",
                    "Android secure/protected surfaces are intentionally unavailable to ordinary capture."
                }),
            new DisplayTransportCapabilities(
                DisplayTransportIds.WindowsMiracast,
                "Windows Wireless Display",
                "external-display",
                wirelessAvailable,
                normalVideo,
                wirelessAvailable,
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                CapabilityState.Unsupported,
                protectedOutput,
                wirelessVerification.NormalPlayback,
                wirelessVerification.ProtectedPlayback,
                new[]
                {
                    "REX orchestrates the Windows receiver; it does not implement Miracast or HDCP itself.",
                    "ADB remains the independent control plane while external display is active.",
                    "Protected playback is unknown until manually verified on the exact phone/PC/driver/application chain."
                })
        };

        return new DisplayProbeResult(
            status.MirrorRunning ? DisplayTransportIds.Scrcpy : null,
            adbControl,
            samsungDex,
            ProtectedContentPolicy(),
            host,
            transports);
    }

    public async Task<DisplayActionResult> StartAsync(
        string transport,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeTransport(transport);

        if (normalized == DisplayTransportIds.Scrcpy)
        {
            if (File.Exists(_paths.StopFlag))
                File.Delete(_paths.StopFlag);

            var code = await _runner.OpenAsync(_paths.StartBatch, _paths.Root);
            if (code != 0)
                throw new InvalidOperationException("Could not request scrcpy mirror startup.");

            return new DisplayActionResult(
                normalized,
                true,
                "scrcpy mirror startup requested.");
        }

        EnsureWindowsWirelessDisplayEnabled();

        if (!ShouldAutoOpenReceiver())
        {
            return new DisplayActionResult(
                normalized,
                false,
                "Windows Wireless Display selected, but receiver auto-open is disabled. Run 'rex display receiver open', then start the phone's wireless display flow.");
        }

        return await OpenReceiverCoreAsync(cancellationToken);
    }

    public async Task<DisplayActionResult> OpenReceiverAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindowsWirelessDisplayEnabled();
        return await OpenReceiverCoreAsync(cancellationToken);
    }

    public DisplayVerification Verify(
        string transport,
        string target,
        string outcome,
        string note = "")
    {
        var normalized = NormalizeTransport(transport);
        if (normalized != DisplayTransportIds.WindowsMiracast)
            throw new ArgumentException(
                "Manual display verification is currently supported for windows-miracast only.");

        if (outcome.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return _verification.Clear(normalized);

        var value = outcome.ToLowerInvariant() switch
        {
            "pass" or "passed" => VerificationOutcome.Passed,
            "fail" or "failed" => VerificationOutcome.Failed,
            _ => throw new ArgumentException("Verification outcome must be pass, fail, or clear.")
        };

        return _verification.Set(normalized, target, value, note);
    }

    public string DefaultTransport()
    {
        try
        {
            return NormalizeTransport(_config.Get("Display.DefaultTransport").Value);
        }
        catch (KeyNotFoundException)
        {
            return DisplayTransportIds.Scrcpy;
        }
    }

    public static string NormalizeTransport(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "scrcpy" => DisplayTransportIds.Scrcpy,
            "miracast" or "wireless-display" or "windows-miracast" =>
                DisplayTransportIds.WindowsMiracast,
            _ => throw new ArgumentException(
                "Display transport must be scrcpy or windows-miracast.")
        };
    }

    public string ProtectedContentPolicy()
    {
        string value;
        try
        {
            value = _config.Get("Display.ProtectedContentPolicy").Value;
        }
        catch (KeyNotFoundException)
        {
            return "prompt";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "prompt" or "ignore" or "prefer-external" => normalized,
            _ => throw new InvalidOperationException(
                "Display.ProtectedContentPolicy must be prompt, ignore, or prefer-external.")
        };
    }

    private async Task<DisplayActionResult> OpenReceiverCoreAsync(
        CancellationToken cancellationToken)
    {
        var receiverCode = await _hostProbe.OpenReceiverSetupAsync(cancellationToken);
        if (receiverCode != 0)
            throw new InvalidOperationException(
                "Could not open Windows Projecting to this PC settings.");

        var phoneFlow = IsSamsungDexGuidanceEnabled()
            ? "Start Smart View or Wireless DeX on Android and select this PC."
            : "Start Smart View on Android and select this PC.";

        return new DisplayActionResult(
            DisplayTransportIds.WindowsMiracast,
            true,
            $"Opened Windows Projecting to this PC. {phoneFlow}");
    }

    private void EnsureWindowsWirelessDisplayEnabled()
    {
        if (!ReadBoolConfig("Display.WindowsWirelessDisplay.Enabled", true))
            throw new InvalidOperationException(
                "Windows Wireless Display support is disabled in REX config.");
    }

    private bool ShouldAutoOpenReceiver() =>
        ReadBoolConfig("Display.WindowsWirelessDisplay.AutoOpenReceiver", true);

    private bool IsSamsungDexGuidanceEnabled() =>
        ReadBoolConfig("Display.SamsungDex.Enabled", true);

    private bool ReadBoolConfig(string path, bool fallback)
    {
        try
        {
            return bool.Parse(_config.Get(path).Value);
        }
        catch (KeyNotFoundException)
        {
            return fallback;
        }
    }

    private static CapabilityState CombineWirelessAvailability(
        DisplayHostCapabilities host)
    {
        if (host.WirelessDisplayFeature == CapabilityState.Unsupported ||
            host.MiracastReceive == CapabilityState.Unsupported)
            return CapabilityState.Unsupported;

        if (host.WirelessDisplayFeature == CapabilityState.Reported &&
            host.MiracastReceive == CapabilityState.Reported)
            return CapabilityState.Reported;

        return CapabilityState.Unknown;
    }
}
