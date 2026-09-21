namespace Rex.AndroidMirror.Cli;

public enum SmartLaunchDecision
{
    SetupRequired,
    FocusMirror,
    StartMirror,
    StartAndWaitForDevice,
    WaitForDevice,
}

public sealed record SmartLaunchOutcome(
    SmartLaunchDecision Decision,
    bool OpenInteractiveCli,
    string Message);

public static class SmartLaunchPlanner
{
    public static SmartLaunchDecision Decide(RexStatus status)
    {
        if (!status.SetupComplete)
            return SmartLaunchDecision.SetupRequired;

        var hasAuthorizedDevice = status.Devices.Any(x => x.State == "device");

        // Clicking the REX desktop shortcut is an explicit start request, so a
        // previous persistent STOP is intentionally cleared.
        if (status.PersistentOff)
        {
            return hasAuthorizedDevice
                ? SmartLaunchDecision.StartMirror
                : SmartLaunchDecision.StartAndWaitForDevice;
        }

        if (status.MirrorRunning)
            return SmartLaunchDecision.FocusMirror;

        if (hasAuthorizedDevice)
            return SmartLaunchDecision.StartMirror;

        return status.SupervisorRunning
            ? SmartLaunchDecision.WaitForDevice
            : SmartLaunchDecision.StartAndWaitForDevice;
    }
}

public sealed class SmartLauncher
{
    private readonly AppPaths _paths;
    private readonly IProcessRunner _runner;
    private readonly IBridgeClient _bridge;

    public SmartLauncher(
        AppPaths paths,
        IProcessRunner runner,
        IBridgeClient bridge)
    {
        _paths = paths;
        _runner = runner;
        _bridge = bridge;
    }

    public async Task<SmartLaunchOutcome> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await _bridge.GetStatusAsync(cancellationToken);
        var decision = SmartLaunchPlanner.Decide(status);

        switch (decision)
        {
            case SmartLaunchDecision.SetupRequired:
                return new(
                    decision,
                    OpenInteractiveCli: true,
                    "Setup is incomplete. Opening the guided REX setup.");

            case SmartLaunchDecision.FocusMirror:
            {
                using var result = await _bridge.InvokeAsync(
                    "focus-active-mirror",
                    cancellationToken: cancellationToken);

                return new(
                    decision,
                    OpenInteractiveCli: false,
                    result.RootElement.TryGetProperty("Text", out var text)
                        ? text.GetString() ?? "Mirror focused."
                        : "Mirror focused.");
            }

            case SmartLaunchDecision.StartMirror:
                await ExplicitStartAsync(cancellationToken);
                return new(
                    decision,
                    OpenInteractiveCli: false,
                    "Android device detected. Starting the mirror.");

            case SmartLaunchDecision.StartAndWaitForDevice:
                await ExplicitStartAsync(cancellationToken);
                return new(
                    decision,
                    OpenInteractiveCli: true,
                    "Supervisor started. Waiting for an authorized Android device.");

            case SmartLaunchDecision.WaitForDevice:
                return new(
                    decision,
                    OpenInteractiveCli: true,
                    "Supervisor is already running. Waiting for an authorized Android device.");

            default:
                throw new InvalidOperationException(
                    $"Unsupported smart-launch decision '{decision}'.");
        }
    }

    private async Task ExplicitStartAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_paths.StopFlag))
            File.Delete(_paths.StopFlag);

        var exitCode = await _runner.OpenAsync(_paths.StartBatch, _paths.Root);
        if (exitCode != 0)
            throw new InvalidOperationException("Could not request Android Headless Mirror startup.");

        cancellationToken.ThrowIfCancellationRequested();
    }
}
