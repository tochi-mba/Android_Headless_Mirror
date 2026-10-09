using System.Windows;
using System.Windows.Threading;
using Rex.Core;
using Rex.Mirror.Mirror;
using Rex.Mirror.Native;
using Rex.Mirror.Services.Sound;

namespace Rex.Mirror;

/// <summary>The other phone's upkeep: its settings, pausing it out of sight, its battery, and its sound.</summary>
public partial class MainWindow
{
    /// <summary>What decides the other session's command line, so a change to it starts the session again.</summary>
    private string OtherArgumentsKey(AdbDevice device) =>
        string.Join('|', ScrcpyArguments.BuildOtherPhone(_host.Config, device.Serial, device.IsTcp, string.Empty, null,
            ScrcpyArguments.KeyboardModeFor(_host.Config, _host.State.GetDevice(device.Serial))));

    private void ApplySecondPhoneConfig()
    {
        Group.PhoneSettings = _host.Config.SecondPhone;
        Group.InvalidateMeasure();
        if (!_host.Config.SecondPhone.Enabled && _other is not null)
        {
            StopOther(byPerson: false);
            return;
        }

        if (_other is { } device && _otherState == OtherPhoneState.Showing && OtherArgumentsKey(device) != _otherArguments)
        {
            SetStatus($"Starting {NameOf(device.Serial)} again with the new settings…");
            EndOtherSound();
            var process = _otherProcess;
            _otherProcess = null;
            _otherView?.Detach();
            process?.Kill();
            process?.Dispose();
            _otherState = OtherPhoneState.Starting;
            _ = LaunchOtherAsync();
        }

        EvaluatePhones();
    }

    /// <summary>
    /// The window's tick: stops the phone beside once the window has been out of sight long enough,
    /// when asked to, and starts it again when the window shows; reads its battery now and then.
    /// </summary>
    private void TickPhones()
    {
        if (_other is null)
        {
            _hiddenSince = null;
            return;
        }

        var hidden = !IsVisible || WindowState == WindowState.Minimized;
        _hiddenSince = hidden ? _hiddenSince ?? DateTime.UtcNow : null;
        var hiddenFor = _hiddenSince is { } since ? DateTime.UtcNow - since : TimeSpan.Zero;
        if (_otherState == OtherPhoneState.Showing && SecondPhonePlan.PausesNow(_host.Config.SecondPhone.PauseWhenHidden, hidden, hiddenFor))
        {
            EndOtherSound();
            var process = _otherProcess;
            _otherProcess = null;
            _otherView?.Detach();
            process?.Kill();
            process?.Dispose();
            _otherState = OtherPhoneState.Paused;
            _host.Log.Info($"{_other.Serial} paused beside while the window is out of sight.");
        }
        else if (_otherState == OtherPhoneState.Paused && !hidden)
        {
            _otherState = OtherPhoneState.Starting;
            _ = LaunchOtherAsync();
        }

        if (DateTime.UtcNow - _otherBatteryRead >= OtherBatteryEvery)
        {
            _ = ReadOtherBatteryAsync(force: false);
        }
    }

    /// <summary>How often the phone beside's battery is read for its name over the view.</summary>
    private static readonly TimeSpan OtherBatteryEvery = TimeSpan.FromSeconds(60);

    private async Task ReadOtherBatteryAsync(bool force)
    {
        if (_other is not { } device || _host.Session.Adb is not { } adb || !force && DateTime.UtcNow - _otherBatteryRead < OtherBatteryEvery)
        {
            return;
        }

        _otherBatteryRead = DateTime.UtcNow;
        var battery = await adb.GetBatteryAsync(device.Serial);
        if (_other?.Serial == device.Serial)
        {
            _otherBattery = battery;
            TrackOverlay();
        }
    }

    /// <summary>The other phone's own sound on this PC, when its session carries one.</summary>
    private void BeginOtherSound(ScrcpyProcess process)
    {
        EndOtherSound();
        if (!_host.Config.Mirror.Audio || !_host.Config.SecondPhone.OtherHasSound)
        {
            return;
        }

        var fake = Environment.GetEnvironmentVariable(FakePhoneSound.Variable);
        _otherSound = new PhoneSound(_host, string.IsNullOrEmpty(fake) ? CoreAudioSound.Find : _ => FakePhoneSound.Find(fake + ".beside"), OtherSoundNow);
        _otherSound.Changed += ShowSound;
        _otherSound.Begin(process.ProcessId, process.Serial);
        process.SoundRefused += why => Dispatcher.BeginInvoke(() => _otherSound?.Refused(why));
    }

    private void EndOtherSound()
    {
        if (_otherSound is not { } sound)
        {
            return;
        }

        _otherSound = null;
        sound.Changed -= ShowSound;
        sound.End();
        sound.Dispose();
    }

    /// <summary>With the sound from the phone in use, the phone beside is quiet while the main one is in use.</summary>
    private SoundWindow OtherSoundNow() =>
        SoundNow() with { OtherPhoneInUse = _host.Config.SecondPhone.Sound == "active" && !OtherActive };

    /// <summary>The sound the sound button and its panel control: the phone in use, when it has its own.</summary>
    internal PhoneSound TargetSound => OtherActive && _otherSound is { } other ? other : _sound!;

    /// <summary>The names and battery over each phone's view, and whether one is outlined, with two phones shown.</summary>
    private bool MarkPhones(List<(Rect, string)> captions, ref Rect? outline)
    {
        if (Group.Phones is not { Other: { } otherCell } phones || _other is not { } other)
        {
            return false;
        }

        static Rect Box(RectD r) => new(r.X, r.Y, r.Width, r.Height);
        static string Battery(BatteryStatus? battery) => battery is { } b ? $" · {b.Level}%" : string.Empty;
        var views = _host.Config.Views;
        if (ViewsSettings.Shows(views.Captions, wanted: true))
        {
            if (_host.Session.ActiveDevice is { } main)
            {
                captions.Add((Box(phones.Main), NameOf(main.Serial) + Battery(_host.Session.Battery)));
            }

            captions.Add((Box(otherCell), NameOf(other.Serial) + Battery(_otherBattery) + (_otherState == OtherPhoneState.Starting ? " · starting…" : string.Empty)));
        }

        if (ViewsSettings.Shows(views.Outline, wanted: true))
        {
            outline = OtherActive ? Box(otherCell) : ReferenceEquals(_activeView, Host) ? Box(phones.Main) : outline;
        }

        return true;
    }
}
