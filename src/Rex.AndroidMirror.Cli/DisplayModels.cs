namespace Rex.AndroidMirror.Cli;

public enum CapabilityState
{
    Unknown,
    Unsupported,
    Reported,
    Verified
}

public enum VerificationOutcome
{
    Unknown,
    Passed,
    Failed
}

public static class DisplayTransportIds
{
    public const string Scrcpy = "scrcpy";
    public const string WindowsMiracast = "windows-miracast";
}

public sealed record DisplayVerification(
    VerificationOutcome NormalPlayback,
    VerificationOutcome ProtectedPlayback,
    DateTimeOffset? UpdatedAt,
    string Note);

public sealed record DisplayTransportCapabilities(
    string Id,
    string Label,
    string Kind,
    CapabilityState Availability,
    CapabilityState Video,
    CapabilityState Audio,
    CapabilityState Control,
    CapabilityState Screenshots,
    CapabilityState Recording,
    CapabilityState ProtectedOutput,
    VerificationOutcome NormalPlaybackVerification,
    VerificationOutcome ProtectedPlaybackVerification,
    IReadOnlyList<string> Notes);

public sealed record DisplayHostCapabilities(
    CapabilityState WirelessDisplayFeature,
    CapabilityState MiracastReceive,
    string Detail);

public sealed record DisplayProbeResult(
    string? CurrentTransport,
    CapabilityState AdbControl,
    CapabilityState SamsungDexCandidate,
    string ProtectedContentPolicy,
    DisplayHostCapabilities Host,
    IReadOnlyList<DisplayTransportCapabilities> Transports);

public sealed record DisplayActionResult(
    string Transport,
    bool Requested,
    string Message);
