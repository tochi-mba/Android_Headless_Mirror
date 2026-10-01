namespace Rex.Mirror.Services.Sound;

/// <summary>
/// The audio session of one scrcpy process on this PC: its volume, mute and channel levels, as the
/// Windows volume mixer shows them. Calls come from the UI thread; a change made from outside (the
/// mixer) is kept until <see cref="TryTakeOutsideChange"/> collects it.
/// </summary>
public interface IPhoneSound : IDisposable
{
    /// <summary>False once the session has gone (the output device changed, the process ended).</summary>
    bool Alive { get; }

    float Volume { get; set; }

    bool Muted { get; set; }

    /// <summary>How many channels the output has; balance needs two.</summary>
    int Channels { get; }

    void SetChannels(float left, float right);

    /// <summary>The loudest the sound has been since the last look, 0 to 1.</summary>
    float Peak { get; }

    /// <summary>A volume or mute set by something other than this app since the last look, if any.</summary>
    bool TryTakeOutsideChange(out float volume, out bool muted);
}
