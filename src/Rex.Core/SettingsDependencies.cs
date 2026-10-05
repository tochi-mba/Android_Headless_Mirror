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
/// <param name="GlobalKeys">Every key from anywhere and what it does: keys from anywhere are on.</param>
/// <param name="Sound">The sound on this PC: phone audio is on.</param>
/// <param name="SoundLowering">How far and how long the sound is lowered: it is lowered while typing.</param>
/// <param name="AppsRecent">How many recent apps are remembered: they are shown.</param>
/// <param name="AppsOnControls">How many favourites the Controls tab shows: it shows them.</param>
/// <param name="Transfer">File-transfer choices: accepting files is on.</param>
/// <param name="ScreenCustom">The second screen's own width and height: its size is set to your own.</param>
/// <param name="ScreenFixed">Upright and the resolution limit: the second screen has a fixed size.</param>
/// <param name="ScreenBeside">Side, minimum width and the splitter: the second screen goes beside the phone.</param>
/// <param name="Touchpad">Two-finger gestures and pinch zoom: touchpad gestures are on.</param>
/// <param name="TransferInstall">APK install choices: accepting files and installing APKs are on.</param>
public sealed record SettingsDependencies(
    bool Sensitivity,
    bool Zoom,
    bool Navigator,
    bool PatternOptions,
    bool WirelessTcpip,
    bool Audio,
    bool AudioDup,
    bool Ambient,
    bool Hud,
    bool GlobalKeys,
    bool Sound,
    bool SoundLowering,
    bool AppsRecent,
    bool AppsOnControls,
    bool Transfer,
    bool TransferInstall,
    bool ScreenCustom,
    bool ScreenFixed,
    bool ScreenBeside,
    bool Touchpad)
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
        Hud: config.Hud.Enabled,
        GlobalKeys: config.GlobalKeys.Enabled,
        Sound: config.Mirror.Audio,
        SoundLowering: config.Mirror.Audio && config.Sound.LowerWhileTyping,
        AppsRecent: config.Apps.ShowRecent,
        AppsOnControls: config.Apps.FavouritesOnControls,
        Transfer: config.Transfer.Enabled,
        TransferInstall: config.Transfer.Enabled && config.Transfer.InstallApks,
        ScreenCustom: config.SecondScreen.Size == "custom",
        ScreenFixed: config.SecondScreen.Size != "follow",
        ScreenBeside: config.SecondScreen.Placement == "beside",
        Touchpad: config.Touchpad.Enabled);
}
