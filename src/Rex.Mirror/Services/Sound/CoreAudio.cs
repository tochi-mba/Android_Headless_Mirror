using System.Runtime.InteropServices;

namespace Rex.Mirror.Services.Sound;

// The Windows Core Audio interfaces the app uses to set the volume of scrcpy's own audio session,
// as the Windows volume mixer does. Ids and method order are those of the Windows SDK headers
// (mmdeviceapi.h, audiopolicy.h, audioclient.h, endpointvolume.h); every method of an interface is
// declared up to the last one used, because COM calls go by position.

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorClass;

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);

    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);

    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);

    // IAudioSessionManager2
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);

    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

/// <summary>AudioSessionState: inactive, active or expired.</summary>
internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig] int GetState(out AudioSessionState state);

    [PreserveSig] int GetDisplayName(out IntPtr name);

    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eventContext);

    [PreserveSig] int GetIconPath(out IntPtr path);

    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr eventContext);

    [PreserveSig] int GetGroupingParam(out Guid grouping);

    [PreserveSig] int SetGroupingParam(ref Guid grouping, IntPtr eventContext);

    [PreserveSig] int RegisterAudioSessionNotification(IAudioSessionEvents events);

    [PreserveSig] int UnregisterAudioSessionNotification(IAudioSessionEvents events);

    // IAudioSessionControl2
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);

    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);

    [PreserveSig] int GetProcessId(out int processId);
}

[ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig] int OnDisplayNameChanged(IntPtr name, IntPtr eventContext);

    [PreserveSig] int OnIconPathChanged(IntPtr path, IntPtr eventContext);

    [PreserveSig] int OnSimpleVolumeChanged(float volume, int muted, IntPtr eventContext);

    [PreserveSig] int OnChannelVolumeChanged(int channelCount, IntPtr volumes, int changedChannel, IntPtr eventContext);

    [PreserveSig] int OnGroupingParamChanged(IntPtr grouping, IntPtr eventContext);

    [PreserveSig] int OnStateChanged(AudioSessionState state);

    [PreserveSig] int OnSessionDisconnected(int reason);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);

    [PreserveSig] int GetMasterVolume(out float level);

    [PreserveSig] int SetMute(int muted, ref Guid eventContext);

    [PreserveSig] int GetMute(out int muted);
}

[ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IChannelAudioVolume
{
    [PreserveSig] int GetChannelCount(out int count);

    [PreserveSig] int SetChannelVolume(int index, float level, ref Guid eventContext);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
}
