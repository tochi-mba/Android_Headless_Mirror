namespace Rex.Core;

/// <summary>
/// Which settings mean anything, given the others. A setting under a parent that is switched off
/// stays on screen, so it can be seen and found, but cannot be changed until the parent is on:
/// a wheel speed for a zoom that is off only invites a change that does nothing.
/// </summary>
/// <param name="Sensitivity">Gesture sensitivity: two-finger gestures go to the phone.</param>
/// <param name="Zoom">The zoom rows: the PC view can be zoomed.</param>
/// <param name="Navigator">The navigator's own rows: zoom is on and the navigator shows.</param>
/// <param name="PatternOptions">The pattern guide's options: the guide is on.</param>
/// <param name="WirelessTcpip">Opening the phone's wireless port: reconnecting over Wi-Fi is on.</param>
/// <param name="Audio">What to capture and its quality: phone audio plays here.</param>
/// <param name="AudioDup">Keeping the audio on the phone too: audio is on and its source can be duplicated.</param>
/// <param name="Ambient">The soft background's options: the soft background is on.</param>
/// <param name="Hud">The fullscreen controls' options: they are shown.</param>
public sealed record SettingsDependencies(
    bool Sensitivity,
    bool Zoom,
    bool Navigator,
    bool PatternOptions,
    bool WirelessTcpip,
    bool Audio,
    bool AudioDup,
    bool Ambient,
    bool Hud)
{
    public static SettingsDependencies Of(RexConfig config) => new(
        Sensitivity: config.Touchpad.Enabled && config.Touchpad.TwoFingerToAndroid,
        Zoom: config.Zoom.Enabled,
        Navigator: config.Zoom.Enabled && config.Zoom.ShowNavigator,
        PatternOptions: config.PatternGuide.Enabled,
        WirelessTcpip: config.Wireless.Enabled,
        Audio: config.Mirror.Audio,
        AudioDup: config.Mirror.Audio && config.Mirror.AudioDupPossible,
        Ambient: config.Ambient.Enabled,
        Hud: config.Hud.Enabled);
}
