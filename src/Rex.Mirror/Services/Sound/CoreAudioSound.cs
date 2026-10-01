using System.Runtime.InteropServices;

namespace Rex.Mirror.Services.Sound;

/// <summary>
/// scrcpy's own audio session, found among the sessions of every active output device by its
/// process id. The app's changes carry an event context of its own, so the session's change events
/// tell them apart from the mixer's. Any COM failure (the device unplugged) marks the session gone,
/// and the controller looks again.
/// </summary>
[ComVisible(true)]
internal sealed class CoreAudioSound : IPhoneSound, IAudioSessionEvents
{
    private const int RenderFlow = 0;
    private const int ActiveDevices = 1;
    private const int AllContexts = 0x17;
    private static readonly Guid SessionManagerId = typeof(IAudioSessionManager2).GUID;

    /// <summary>Marks the app's own changes, so the session's events can tell them from the mixer's.</summary>
    private static Guid _ownContext = new("5B0D0B47-6E3F-4F1A-9C55-52455820534E");

    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume _volume;
    private readonly IChannelAudioVolume? _channels;
    private readonly IAudioMeterInformation? _meter;
    private readonly object _gate = new();
    private (float Volume, bool Muted)? _outside;
    private volatile bool _alive = true;

    private CoreAudioSound(IAudioSessionControl2 control)
    {
        _control = control;
        _volume = (ISimpleAudioVolume)control;
        _channels = control as IChannelAudioVolume;
        _meter = control as IAudioMeterInformation;
        _control.RegisterAudioSessionNotification(this);
    }

    /// <summary>The session of this process on any active output, preferring one that is playing; null when there is none yet.</summary>
    public static IPhoneSound? Find(int processId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
        if (enumerator.EnumAudioEndpoints(RenderFlow, ActiveDevices, out var devices) != 0 || devices.GetCount(out var count) != 0)
        {
            return null;
        }

        IAudioSessionControl2? best = null;
        for (var i = 0; i < count; i++)
        {
            if (devices.Item(i, out var device) != 0)
            {
                continue;
            }

            var iid = SessionManagerId;
            if (device.Activate(ref iid, AllContexts, IntPtr.Zero, out var activated) != 0 ||
                ((IAudioSessionManager2)activated).GetSessionEnumerator(out var sessions) != 0 ||
                sessions.GetCount(out var sessionCount) != 0)
            {
                continue;
            }

            for (var s = 0; s < sessionCount; s++)
            {
                if (sessions.GetSession(s, out var session) != 0 || session.GetProcessId(out var pid) < 0 || pid != processId ||
                    session.GetState(out var state) != 0 || state == AudioSessionState.Expired)
                {
                    continue;
                }

                if (best is null || state == AudioSessionState.Active)
                {
                    best = session;
                }
            }
        }

        return best is null ? null : new CoreAudioSound(best);
    }

    public bool Alive => _alive;

    public float Volume
    {
        get => Call(() => _volume.GetMasterVolume(out var level) == 0 ? level : 0f, 0f);
        set => Call(() => _volume.SetMasterVolume(value, ref _ownContext), 0);
    }

    public bool Muted
    {
        get => Call(() => _volume.GetMute(out var muted) == 0 && muted != 0, false);
        set => Call(() => _volume.SetMute(value ? 1 : 0, ref _ownContext), 0);
    }

    public int Channels => _channels is null ? 0 : Call(() => _channels.GetChannelCount(out var count) == 0 ? count : 0, 0);

    public void SetChannels(float left, float right)
    {
        if (Channels != 2)
        {
            return;
        }

        Call(() => _channels!.SetChannelVolume(0, left, ref _ownContext) | _channels.SetChannelVolume(1, right, ref _ownContext), 0);
    }

    public float Peak => _meter is null ? 0f : Call(() => _meter.GetPeakValue(out var peak) == 0 ? peak : 0f, 0f);

    public bool TryTakeOutsideChange(out float volume, out bool muted)
    {
        lock (_gate)
        {
            (volume, muted) = _outside ?? (0f, false);
            var had = _outside is not null;
            _outside = null;
            return had;
        }
    }

    private T Call<T>(Func<T> call, T fallback)
    {
        if (!_alive)
        {
            return fallback;
        }

        try
        {
            return call();
        }
        catch (COMException)
        {
            _alive = false;
            return fallback;
        }
        catch (InvalidComObjectException)
        {
            _alive = false;
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_alive)
        {
            Call(() => _control.UnregisterAudioSessionNotification(this), 0);
        }

        _alive = false;
    }

    // ----- IAudioSessionEvents, called on Windows' own threads: record, and nothing else -----

    int IAudioSessionEvents.OnSimpleVolumeChanged(float volume, int muted, IntPtr eventContext)
    {
        if (eventContext == IntPtr.Zero || Marshal.PtrToStructure<Guid>(eventContext) != _ownContext)
        {
            lock (_gate)
            {
                _outside = (volume, muted != 0);
            }
        }

        return 0;
    }

    int IAudioSessionEvents.OnStateChanged(AudioSessionState state)
    {
        if (state == AudioSessionState.Expired)
        {
            _alive = false;
        }

        return 0;
    }

    int IAudioSessionEvents.OnSessionDisconnected(int reason)
    {
        _alive = false;
        return 0;
    }

    int IAudioSessionEvents.OnDisplayNameChanged(IntPtr name, IntPtr eventContext) => 0;

    int IAudioSessionEvents.OnIconPathChanged(IntPtr path, IntPtr eventContext) => 0;

    int IAudioSessionEvents.OnChannelVolumeChanged(int channelCount, IntPtr volumes, int changedChannel, IntPtr eventContext) => 0;

    int IAudioSessionEvents.OnGroupingParamChanged(IntPtr grouping, IntPtr eventContext) => 0;
}
