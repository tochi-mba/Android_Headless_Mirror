namespace Rex.AndroidMirror.Cli;

public enum RootAccessState
{
    Unknown,
    AdbdRoot,
    Unavailable,
    SuDetected,
    AuthorizationPending,
    Denied,
    GrantedRestricted,
    Granted
}

public enum RootProvider
{
    None,
    Unknown,
    Magisk,
    KernelSU,
    APatch,
    Other
}

public enum PrivilegeRisk
{
    ReadOnly,
    Reversible,
    SystemChanging,
    DeviceCritical
}

public enum RootExecutionMode
{
    AdbdRoot,
    Su
}

public sealed record RootCapability(
    string Id,
    CapabilityState State,
    PrivilegeRisk Risk,
    string Detail);

public sealed record RootPrivilegeProfile(
    int? EffectiveUid,
    int? EffectiveGid,
    IReadOnlyList<int> Groups,
    string SelinuxContext,
    string CapabilityEffectiveHex,
    string CapabilityPermittedHex,
    string CapabilityBoundingHex);

public sealed record RootStatus(
    string Serial,
    RootAccessState State,
    RootProvider Provider,
    string ProviderVersion,
    int? AdbUid,
    int? EffectiveUid,
    bool SuVisible,
    string SuPath,
    string BootId,
    string SelinuxMode,
    RootPrivilegeProfile? PrivilegeProfile,
    IReadOnlyList<RootCapability> Capabilities,
    DateTimeOffset CheckedAt,
    bool FromCachedVerification,
    string Message);

public sealed record PrivilegedCommand(
    string Id,
    string Executable,
    IReadOnlyList<string> Arguments,
    PrivilegeRisk Risk = PrivilegeRisk.ReadOnly,
    TimeSpan? Timeout = null,
    int? MaxOutputCharacters = null);

public sealed record AndroidCommandResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    bool Truncated)
{
    public bool Ok => !TimedOut && ExitCode == 0;
}

public sealed record RootFeatureResult(
    string Operation,
    string Serial,
    PrivilegeRisk Risk,
    IReadOnlyDictionary<string, string> Sections);
