using System.Windows.Threading;
using Rex.Core;

namespace Rex.Mirror.Services.Sound;

/// <summary>What the window tells the sound about itself, read at each look.</summary>
/// <param name="Hidden">Hidden in the tray or minimised.</param>
/// <param name="Behind">Shown, but another window is in front.</param>
/// <param name="Locked">This PC is locked.</param>
/// <param name="SinceLastKey">How long ago a key was last typed into the phone.</param>
public sealed record SoundWindow(bool Hidden, bool Behind, bool Locked, TimeSpan SinceLastKey);

/// <summary>
/// The phone's sound on this PC for the mirror that is running: finds scrcpy's audio session, keeps
/// it at the level the person chose and the rules allow (<see cref="SoundPolicy"/>), fades between
/// levels (<see cref="SoundFade"/>), adopts or undoes changes made in the Windows mixer, and keeps
/// the person's choice where <see cref="SoundMemory"/> says. It looks for the session every two
/// seconds only until it has one, checks the rules four times a second, and runs its fading timer
/// only while a fade is under way.
/// </summary>
public sealed class PhoneSound : IDisposable
{
    private static readonly TimeSpan SearchEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SaveAfter = TimeSpan.FromMilliseconds(400);

    private readonly AppHost _host;
    private readonly Func<int, IPhoneSound?> _find;
    private readonly Func<SoundWindow> _window;
    private readonly DispatcherTimer _look;
    private readonly DispatcherTimer _fade;
    private readonly DispatcherTimer _save;
    private IPhoneSound? _session;
    private int _processId;
    private string? _serial;
    private DateTime _lastSearch = DateTime.MinValue;
    private DateTime _lastFade;
    private double _applied = -1;
    private bool? _appliedMuted;
    private double? _appliedBalance;
    private string? _problem = SoundProblems.NoMirror;

    /// <summary>The volume and mute config.json held when last looked at, so only a change to them is adopted.</summary>
    private (double Volume, bool Muted) _configSeen;

    public PhoneSound(AppHost host, Func<int, IPhoneSound?> find, Func<SoundWindow> window)
    {
        _host = host;
        _find = find;
        _window = window;
        _look = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _look.Tick += (_, _) => Look();
        _fade = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _fade.Tick += (_, _) => Fade();
        _save = new DispatcherTimer { Interval = SaveAfter };
        _save.Tick += (_, _) => Save();
    }

    /// <summary>The volume the person chose for this phone, 0 to 1.</summary>
    public double Volume { get; private set; } = 1;

    /// <summary>The person muted it.</summary>
    public bool Muted { get; private set; }

    /// <summary>Why there is no sound to control, or null when there is.</summary>
    public string? Problem => _host.Config.Mirror.Audio ? _problem : SoundProblems.AudioOff;

    public bool Available => Problem is null;

    /// <summary>What the rules make of it right now.</summary>
    public SoundTarget Target { get; private set; } = new(1, false, null);

    /// <summary>The level actually playing, after any fade.</summary>
    public double Level => Math.Max(0, _applied);

    /// <summary>The balance in Settings, -1 (left) to 1 (right).</summary>
    public double Balance => _host.Config.Sound.Balance;

    /// <summary>A change made in the Windows mixer is adopted rather than put back.</summary>
    public bool FollowMixer => _host.Config.Sound.FollowMixer;

    /// <summary>The volume and mute are kept for this phone alone.</summary>
    public bool PerPhone => SoundMemory.PerPhone(_host.Config.Sound, _serial);

    /// <summary>How many channels the output has (0 when unknown).</summary>
    public int Channels => _session?.Channels ?? 0;

    /// <summary>How loud it is playing, 0 to 1, for the panel's meter.</summary>
    public float Peak => _session?.Peak ?? 0;

    /// <summary>Raised after anything the sound panel or the top bar shows has changed.</summary>
    public event Action? Changed;

    /// <summary>A mirror with sound started: its process, and the phone whose choice applies.</summary>
    public void Begin(int processId, string serial)
    {
        End();
        _processId = processId;
        _serial = serial;
        (Volume, Muted) = SoundMemory.StartWith(_host.Config.Sound, _host.State.GetDevice(serial));
        _configSeen = (_host.Config.Sound.Volume, _host.Config.Sound.Muted);
        _problem = SoundProblems.Waiting;
        _lastSearch = DateTime.MinValue;
        _look.Start();
        Look();
    }

    /// <summary>scrcpy said the phone cannot or will not send its sound.</summary>
    public void Refused(string why)
    {
        DropSession();
        _problem = why;
        _look.Stop();
        Changed?.Invoke();
    }

    /// <summary>The mirror ended: there is nothing to control until the next one.</summary>
    public void End()
    {
        Save();
        DropSession();
        _look.Stop();
        _fade.Stop();
        _processId = 0;
        _problem = SoundProblems.NoMirror;
        Changed?.Invoke();
    }

    public void SetVolume(double volume)
    {
        Volume = SoundSettings.Fraction(volume, Volume);
        Chosen();
    }

    /// <summary>One step louder (+1) or quieter (-1), by the step in Settings.</summary>
    public void Step(int direction)
    {
        Volume = SoundSettings.Fraction(Math.Round(Volume + direction * _host.Config.Sound.Step, 2), Volume);
        if (direction > 0)
        {
            Muted = false;
        }

        Chosen();
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        Chosen();
    }

    /// <summary>
    /// Settings changed: the rules and the balance apply at once, and while phones are not remembered
    /// one by one, a new volume or mute in Settings is this phone's too.
    /// </summary>
    public void Refresh()
    {
        var settings = _host.Config.Sound;
        if ((settings.Volume, settings.Muted) != _configSeen)
        {
            _configSeen = (settings.Volume, settings.Muted);
            if (!SoundMemory.PerPhone(settings, _serial))
            {
                (Volume, Muted) = _configSeen;
            }
        }

        Look();
    }

    /// <summary>
    /// The person changed the volume or mute: apply it now and keep it. A phone's own choice is
    /// written to state.json a moment after the last change; the shared one goes to config.json the
    /// way every setting that moves while it is dragged does.
    /// </summary>
    private void Chosen()
    {
        if (SoundMemory.PerPhone(_host.Config.Sound, _serial))
        {
            _save.Stop();
            _save.Start();
        }
        else
        {
            var (volume, muted) = (Volume, Muted);
            _configSeen = (volume, muted);
            _host.PreviewConfig(c =>
            {
                c.Sound.Volume = volume;
                c.Sound.Muted = muted;
            });
        }

        Look();
    }

    private void Save()
    {
        if (!_save.IsEnabled)
        {
            return;
        }

        _save.Stop();
        if (_serial is not null)
        {
            _host.State.SetSound(_serial, Volume, Muted);
        }
    }

    private void Look()
    {
        if (_processId == 0 || !_host.Config.Mirror.Audio)
        {
            Changed?.Invoke();
            return;
        }

        if (_session is { Alive: false })
        {
            DropSession();
            _problem = SoundProblems.Waiting;
        }

        if (_session is null)
        {
            if (DateTime.UtcNow - _lastSearch < SearchEvery)
            {
                Changed?.Invoke();
                return;
            }

            _lastSearch = DateTime.UtcNow;
            _session = _find(_processId);
            if (_session is null)
            {
                Changed?.Invoke();
                return;
            }

            _problem = null;
            _host.Log.Info("Found the phone's sound on this PC.");
        }

        var settings = _host.Config.Sound;
        if (_session.TryTakeOutsideChange(out var outsideVolume, out var outsideMuted))
        {
            if (settings.FollowMixer)
            {
                Volume = SoundSettings.Fraction(outsideVolume, Volume);
                Muted = outsideMuted;
                _applied = Volume;
                _appliedMuted = Muted;
                Chosen();
            }
            else
            {
                // Put back: what the session now holds is not what the app set.
                _applied = -1;
                _appliedMuted = null;
            }
        }

        var window = _window();
        Target = SoundPolicy.Decide(new SoundInputs(Volume, Muted, window.Hidden, window.Behind, window.Locked, window.SinceLastKey), settings);
        if (_appliedMuted != Target.Muted)
        {
            _session.Muted = Target.Muted;
            _appliedMuted = Target.Muted;
        }

        if (_appliedBalance != settings.Balance)
        {
            var (left, right) = SoundBalance.Channels(settings.Balance);
            _session.SetChannels(left, right);
            _appliedBalance = settings.Balance;
        }

        if (_applied < 0)
        {
            // A session just found, or one changed from outside: set it at once, without a fade.
            _applied = Target.Level;
            _session.Volume = (float)_applied;
        }
        else if (Math.Abs(_applied - Target.Level) > 0.0005 && !_fade.IsEnabled)
        {
            _lastFade = DateTime.UtcNow;
            _fade.Start();
        }

        Changed?.Invoke();
    }

    private void Fade()
    {
        if (_session is null)
        {
            _fade.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        _applied = SoundFade.Next(_applied, Target.Level, now - _lastFade, _host.Config.Sound.FadeMs);
        _lastFade = now;
        _session.Volume = (float)_applied;
        if (Math.Abs(_applied - Target.Level) <= 0.0005)
        {
            _fade.Stop();
            Changed?.Invoke();
        }
    }

    private void DropSession()
    {
        _session?.Dispose();
        _session = null;
        _applied = -1;
        _appliedMuted = null;
        _appliedBalance = null;
    }

    public void Dispose()
    {
        _look.Stop();
        _fade.Stop();
        Save();
        DropSession();
    }
}
