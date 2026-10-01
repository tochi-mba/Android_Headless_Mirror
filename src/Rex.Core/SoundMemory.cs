namespace Rex.Core;

/// <summary>
/// Where the volume and mute a person chooses are kept, and what a mirror starts with: per phone in
/// state.json when <see cref="SoundSettings.RememberPerPhone"/> is on, otherwise in config.json.
/// </summary>
public static class SoundMemory
{
    /// <summary>True when a change for this phone belongs in its profile rather than in config.json.</summary>
    public static bool PerPhone(SoundSettings settings, string? serial) =>
        settings.RememberPerPhone && !string.IsNullOrEmpty(serial);

    /// <summary>The volume and mute a mirror of this phone starts with.</summary>
    public static (double Volume, bool Muted) StartWith(SoundSettings settings, DeviceProfile? profile)
    {
        var remembered = settings.RememberPerPhone ? profile : null;
        var volume = remembered?.SoundVolume is { } v ? SoundSettings.Fraction(v, settings.Volume) : settings.Volume;
        var muted = settings.StartMuted || (remembered?.SoundMuted ?? settings.Muted);
        return (volume, muted);
    }
}
