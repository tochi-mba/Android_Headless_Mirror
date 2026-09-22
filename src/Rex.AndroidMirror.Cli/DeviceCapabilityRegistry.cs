namespace Rex.AndroidMirror.Cli;

public sealed record DeviceCapabilitySnapshot(
    string? Serial,
    CapabilityState Adb,
    IReadOnlyDictionary<string, CapabilityState> Capabilities,
    RootStatus? Root);

public sealed class DeviceCapabilityRegistry
{
    private readonly IBridgeClient _bridge;
    private readonly RootManager _root;

    public DeviceCapabilityRegistry(IBridgeClient bridge, RootManager root)
    {
        _bridge = bridge;
        _root = root;
    }

    public async Task<DeviceCapabilitySnapshot> ProbeAsync(
        string? serial = null,
        CancellationToken cancellationToken = default)
    {
        var status = await _bridge.GetStatusAsync(cancellationToken);
        var authorized = status.Devices.Where(x => x.State == "device").ToArray();
        var selected = serial ??
            (authorized.Length == 1 ? authorized[0].Serial : null);

        var values = new Dictionary<string, CapabilityState>(StringComparer.Ordinal)
        {
            ["device.adb"] = authorized.Length > 0
                ? CapabilityState.Verified
                : CapabilityState.Unknown
        };

        RootStatus? root = null;
        if (selected is not null &&
            authorized.Any(x => x.Serial.Equals(selected, StringComparison.Ordinal)))
        {
            try
            {
                root = await _root.ProbePassiveAsync(selected, cancellationToken);
                values["device.root"] = root.State switch
                {
                    RootAccessState.AdbdRoot or
                    RootAccessState.Granted or
                    RootAccessState.GrantedRestricted => CapabilityState.Verified,
                    RootAccessState.SuDetected => CapabilityState.Reported,
                    RootAccessState.Unavailable => CapabilityState.Unsupported,
                    _ => CapabilityState.Unknown
                };

                foreach (var capability in root.Capabilities)
                    values[capability.Id] = capability.State;
            }
            catch (InvalidOperationException)
            {
                values["device.root"] = CapabilityState.Unknown;
            }
        }
        else if (selected is not null)
        {
            values["device.root"] = CapabilityState.Unknown;
        }

        return new DeviceCapabilitySnapshot(
            selected,
            authorized.Length > 0 ? CapabilityState.Verified : CapabilityState.Unknown,
            values,
            root);
    }
}
