using System.Windows;
using Microsoft.Win32;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services.Sound;

namespace Rex.Mirror;

/// <summary>
/// The phone's sound on this PC, from the window's side: the top bar's sound button and its panel,
/// the sound actions, and what the rules need to know about the window (hidden, behind others, this
/// PC locked, the last key typed into the phone). <see cref="PhoneSound"/> does the rest.
/// </summary>
public partial class MainWindow
{
    private PhoneSound? _sound;
    private bool _pcLocked;

    internal PhoneSound PhoneSound => _sound!;

    internal bool SoundPanelOpen => SoundPopup.IsOpen;

    private void InitSound()
    {
        var fake = Environment.GetEnvironmentVariable(FakePhoneSound.Variable);
        _sound = new PhoneSound(_host, string.IsNullOrEmpty(fake) ? CoreAudioSound.Find : _ => FakePhoneSound.Find(fake), SoundNow);
        _sound.Changed += ShowSound;
        SoundPanel.Attach(this, _host, _sound);
        _host.Session.MirrorReady += BeginSound;
        _host.Session.MirrorEnded += _sound.End;
        _host.ConfigPreviewed += _sound.Refresh;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        QuickSound.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = RunActionAsync("sound-mute");
        };
        QuickSound.PreviewMouseWheel += (_, e) =>
        {
            if (_host.Config.Sound.WheelOnButton && _sound.Available)
            {
                e.Handled = true;
                _ = RunActionAsync(e.Delta > 0 ? "sound-up" : "sound-down");
            }
        };
        SoundPopup.Closed += (_, _) =>
        {
            if (ActiveView.HasChild)
            {
                ActiveView.FocusChild();
            }
        };
        ShowSound();
    }

    private void BeginSound(ScrcpyProcess scrcpy)
    {
        if (!_host.Config.Mirror.Audio)
        {
            return;
        }

        _sound!.Begin(scrcpy.ProcessId, scrcpy.Serial);
        if (scrcpy.SoundProblem is { } already)
        {
            _sound.Refused(already);
            return;
        }

        scrcpy.SoundRefused += why => Dispatcher.BeginInvoke(() =>
        {
            _host.Log.Info("No phone sound: " + why);
            _sound.Refused(why);
        });
    }

    /// <summary>What the sound rules need to know about the window, now.</summary>
    private SoundWindow SoundNow()
    {
        var hidden = !IsVisible || WindowState == WindowState.Minimized;
        // The sound panel and the fullscreen controls are windows of this app too: only another
        // program in front counts as behind.
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var foregroundProcess);
        var behind = !hidden && foregroundProcess != Environment.ProcessId;
        var typing = FocusInsideControl() ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(Environment.TickCount64 - _hooks.LastKeyToPhone);
        return new SoundWindow(hidden, behind, _pcLocked, typing);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock => true,
            SessionSwitchReason.SessionUnlock => false,
            _ => _pcLocked,
        };
        Dispatcher.BeginInvoke(() =>
        {
            _pcLocked = locked;
            _sound?.Refresh();
        });
    }

    private void ShowSound()
    {
        var sound = _sound!;
        var (icon, words) = !sound.Available ? ("IconPcSoundOff", sound.Problem!)
            : sound.Muted ? ("IconPcMute", "muted")
            : sound.Volume < 0.34 ? ("IconPcSound", SoundPanelWords(sound))
            : ("IconPcSoundUp", SoundPanelWords(sound));
        QuickSound.Content = FindResource(icon);
        QuickSound.ToolTip = "Phone sound on this PC: " + words + " · " + Shortcuts.Gesture("sound-up") + " / " + Shortcuts.Gesture("sound-down") + " · right-click mutes";
        System.Windows.Automation.AutomationProperties.SetHelpText(QuickSound, words);
        SoundPanel.Show();
    }

    private static string SoundPanelWords(PhoneSound sound) => Views.SoundPanel.Words(sound.Volume, sound.Muted);

    private void OnQuickSound(object sender, RoutedEventArgs e)
    {
        SoundPopup.IsOpen = !SoundPopup.IsOpen;
        if (SoundPopup.IsOpen)
        {
            SoundPanel.Show();
            Dispatcher.BeginInvoke(SoundPanel.FocusFirst, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>Closes the sound panel and gives the keyboard back to the phone.</summary>
    internal void CloseSoundPanel() => SoundPopup.IsOpen = false;

    /// <summary>Shows a group of the Settings tab, opened and in view.</summary>
    internal void OpenSettingsGroup(string groupName)
    {
        SoundPopup.IsOpen = false;
        if (!_sidebarWanted)
        {
            SetSidebarVisible(true);
        }

        SelectTab("settings");
        Dispatcher.BeginInvoke(() => SettingsPanel.Reveal(groupName), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void StopSound()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _sound?.Dispose();
    }

    /// <summary>The sound actions: louder, quieter, and mute on this PC. They say the new level.</summary>
    private AndroidResult RunSound(string id)
    {
        var sound = _sound!;
        if (!sound.Available)
        {
            return AndroidResult.Failure(sound.Problem!);
        }

        switch (id)
        {
            case "sound-up":
                sound.Step(1);
                break;
            case "sound-down":
                sound.Step(-1);
                break;
            default:
                sound.SetMuted(!sound.Muted);
                break;
        }

        return AndroidResult.Success("Phone sound on this PC: " + SoundPanelWords(sound));
    }
}
