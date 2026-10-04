using Rex.Core;

namespace Rex.Mirror;

/// <summary>
/// Profiles in the window: the moments that switch automatic ones (fullscreen, a phone connecting,
/// the power on the window's tick), the number keys, and saying what switched.
/// </summary>
public partial class MainWindow
{
    private void AttachProfiles()
    {
        var profiles = _host.Profiles;
        profiles.Said += words =>
        {
            SetStatus(words);
            if (!IsVisible)
            {
                Notify("Profiles", words);
            }
        };
        profiles.Changed += () => _host.Tray?.Refresh();
        _host.Session.MirrorReady += scrcpy => profiles.PhoneConnected(scrcpy.Serial);
    }

    /// <summary>Called once a fullscreen change has happened, so an automatic profile starts or ends after it.</summary>
    private void ProfilesFollowFullscreen()
    {
        var fullscreen = _fullscreen;
        Dispatcher.BeginInvoke(() => _host.Profiles.FullscreenChanged(fullscreen));
    }

    /// <summary>Ctrl+Alt+F1 to F9; off, the key goes on to the phone like any other.</summary>
    private Action? ProfileKey(int number) =>
        _host.Config.Profiles.Keys ? () => SetStatus(_host.Profiles.ApplyNumber(number)) : null;

    /// <summary>The tray and the pipe apply a profile by name.</summary>
    internal string ApplyProfile(string name) => _host.Profiles.Apply(name);
}
