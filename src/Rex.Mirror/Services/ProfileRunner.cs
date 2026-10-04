using System.IO;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>
/// Profiles in the running app: applied by hand, by key, from the tray or the pipe, and by
/// themselves in fullscreen, on battery, or for a phone. The work itself is the
/// <see cref="ProfileBook"/>'s, shared with the command line; this adds the restart a profile may
/// need, the moments that switch automatic profiles, and the words said about them.
/// </summary>
public sealed class ProfileRunner
{
    /// <summary>With this set, the file it names says "battery" or "mains" in place of this PC's power.</summary>
    public const string FakePowerVariable = "REX_FAKE_POWER";

    private readonly AppHost _host;
    private DateTime _powerCheckedUtc;
    private bool _onBattery;
    private bool _fullscreen;
    private string? _phoneProfile;

    public ProfileRunner(AppHost host)
    {
        _host = host;
        Book = new ProfileBook(ProfileStore.For(host.Paths), host.State, () => host.Config, host.UpdateConfig);
        _onBattery = ReadOnBattery();
    }

    public ProfileBook Book { get; }

    public ProfileStore Store => Book.Store;

    /// <summary>The profile applied by hand, or empty for the person's own settings.</summary>
    public string Manual => _host.State.Ui.Profile;

    /// <summary>The automatic profile in effect now, or null.</summary>
    public string? Automatic => Book.ReadPutBack()?.Profile;

    /// <summary>Whether this PC runs on battery now, as last looked at.</summary>
    public bool OnBattery => _onBattery;

    /// <summary>Raised when a profile is applied, saved, renamed or removed, or an automatic one starts or ends.</summary>
    public event Action? Changed;

    /// <summary>Raised with words worth saying, such as an automatic profile switching.</summary>
    public event Action<string>? Said;

    /// <summary>Whether this PC has a battery at all; the battery rule means nothing without one.</summary>
    public bool HasBattery =>
        Environment.GetEnvironmentVariable(FakePowerVariable) is { Length: > 0 } fake
            ? File.Exists(fake)
            : System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;

    /// <summary>Applies a profile (a saved one, else a preset) and says what happened.</summary>
    public string Apply(string name, bool byHand = true)
    {
        if (Book.Apply(name, byHand) is not { } applied)
        {
            return $"There is no profile called “{name.Trim()}”.";
        }

        var words = applied.Words + RestartWords();
        Changed?.Invoke();
        return words;
    }

    /// <summary>
    /// A profile command from the pipe, run as the command line would run it on the files; then
    /// what the app adds: the restart an applied profile may need, and the window, the tray and an
    /// automatic profile following.
    /// </summary>
    public ProfileResult Run(ProfileRequest request)
    {
        var result = ProfileVerbs.Run(Book, request, DateTimeOffset.UtcNow);
        if (request.Verb is "list" or "show" or "export")
        {
            return result;
        }

        if (request.Verb == "apply")
        {
            result = result with { Words = result.Words + RestartWords() };
        }

        if (request.Verb is "rename" or "delete")
        {
            Evaluate();
        }
        else
        {
            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>Applies the profile a number key stands for, or says there is none.</summary>
    public string ApplyNumber(int number) =>
        Book.NameAt(number) is { } name ? Apply(name) : $"There is no profile {number}. Save one in Settings, Profiles.";

    /// <summary>A profile that changes how the mirror starts restarts it, or offers to, as set.</summary>
    private string RestartWords()
    {
        if (!_host.Session.NeedsRestart)
        {
            return string.Empty;
        }

        if (_host.Config.Profiles.AfterApplying == "restart")
        {
            _host.Session.RestartMirror();
            return " · the mirror restarts to use it";
        }

        return " · Restart now to use all of it";
    }

    public Profile Save(string name, IReadOnlyCollection<string>? groups, bool onlyChanged) =>
        Done(Book.Save(name, groups, onlyChanged, DateTimeOffset.UtcNow));

    public Profile Update(string name) => Done(Book.Update(name, DateTimeOffset.UtcNow));

    public void Rename(string from, string to)
    {
        Book.Rename(from, to);
        Evaluate();
    }

    public void Delete(string name)
    {
        Book.Delete(name);
        Evaluate();
    }

    public void UseForPhone(string serial, string name)
    {
        Book.UseForPhone(serial, name);
        if (_host.Session.ActiveDevice?.Serial == serial)
        {
            PhoneConnected(serial);
            return;
        }

        Changed?.Invoke();
    }

    private T Done<T>(T result)
    {
        Changed?.Invoke();
        return result;
    }

    // ----- Automatic profiles -----

    public void FullscreenChanged(bool fullscreen)
    {
        _fullscreen = fullscreen;
        Evaluate();
    }

    /// <summary>A phone connected: its own profile, if it has one, now counts.</summary>
    public void PhoneConnected(string serial)
    {
        _phoneProfile = _host.State.GetDevice(serial)?.Profile;
        Evaluate();
    }

    /// <summary>
    /// The window's tick calls this; only while a battery rule exists does it look at the power,
    /// every two seconds, so a PC with no such rule spends nothing on it.
    /// </summary>
    public void CheckPower()
    {
        var now = DateTime.UtcNow;
        if (_host.Config.Profiles.WhenOnBattery.Length == 0 || now - _powerCheckedUtc < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _powerCheckedUtc = now;
        var onBattery = ReadOnBattery();
        if (onBattery != _onBattery)
        {
            _onBattery = onBattery;
            Evaluate();
        }
    }

    private bool ReadOnBattery()
    {
        if (Environment.GetEnvironmentVariable(FakePowerVariable) is { Length: > 0 } fake)
        {
            try
            {
                return File.Exists(fake) && File.ReadAllText(fake).Trim().Equals("battery", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return _onBattery;
            }
        }

        return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
    }

    /// <summary>Starts, switches or ends the automatic profile to match the rules and the moment.</summary>
    public void Evaluate()
    {
        var settings = _host.Config.Profiles;
        var wanted = ProfilePolicy.Automatic(new ProfileInputs(Manual, _phoneProfile, _onBattery, _fullscreen), settings);
        var profile = wanted is null ? null : Book.Find(wanted);
        var record = Book.ReadPutBack();
        if (string.Equals(profile?.Name, record?.Profile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (record is not null)
        {
            Book.End(record, settings.PutBack);
            if (profile is null)
            {
                Say($"{record.Profile} is off" + RestartWords());
            }
        }

        if (profile is not null)
        {
            Book.Start(profile);
            var why = _fullscreen && settings.WhenFullscreen.Length > 0 ? "in fullscreen"
                : _onBattery && settings.WhenOnBattery.Length > 0 ? "while this PC runs on battery"
                : "for this phone";
            Say($"{profile.Name} is on {why}" + RestartWords());
        }

        Changed?.Invoke();
    }

    private void Say(string words)
    {
        if (_host.Config.Profiles.Announce)
        {
            Said?.Invoke(words);
        }
    }
}
