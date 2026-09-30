namespace Rex.Core;

public enum UsbNotice
{
    None,

    /// <summary>The auto-repair task is working on it.</summary>
    Fixing,

    /// <summary>Nothing will happen by itself: the prompted repair, and setting up auto-repair, are offered.</summary>
    Offer,

    /// <summary>Repairs were tried and Windows still cannot read the phone: what a person can do.</summary>
    Advice,
}

/// <summary>What to show, and whether to start the auto-repair task now.</summary>
public sealed record UsbRecoveryDecision(UsbNotice Notice, bool Trigger, int Attempts)
{
    public static UsbRecoveryDecision Quiet { get; } = new(UsbNotice.None, false, 0);
}

/// <summary>
/// When the app repairs USB by itself, and what it says while it does. An episode is one spell of
/// Windows failing to read the phone: it starts when a failed phone-like node is seen with no
/// phone ready, and ends when a phone is ready or no failed node has been seen for
/// <see cref="EpisodeGap"/>. A repair makes the node vanish for a moment, so a short absence is
/// the same episode. Attempts are at most one per <see cref="MinimumInterval"/>, whatever the
/// episode, and <see cref="AttemptsPerEpisode"/> per episode. Pure and clocked from outside.
/// </summary>
public sealed class UsbRecoveryPolicy
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(90);
    public const int AttemptsPerEpisode = 3;
    public static readonly TimeSpan EpisodeGap = TimeSpan.FromSeconds(15);

    /// <summary>How long the last repair gets to bring the phone back before advice replaces it.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a problem must last before anything is said. The hub has already given up on a
    /// failed enumeration; a phone's own device can show a problem for a while as Windows
    /// installs its drivers the first time it is plugged in.
    /// </summary>
    public static readonly TimeSpan FailedEnumerationDelay = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan PhoneDeviceDelay = TimeSpan.FromSeconds(20);

    private DateTime? _episodeStart;
    private DateTime? _absentSince;
    private DateTime? _lastAttempt;
    private int _attempts;
    private bool _dismissed;
    private UsbNotice _shown;

    public bool InEpisode => _episodeStart is not null;

    public int Attempts => _attempts;

    /// <param name="automatic">The auto-repair task is set up, allowed, and reaches one of these problems.</param>
    public UsbRecoveryDecision Observe(DateTime now, bool phoneReady, IReadOnlyList<UsbProblem> problems, bool automatic)
    {
        if (phoneReady)
        {
            EndEpisode();
            return UsbRecoveryDecision.Quiet;
        }

        if (problems.Count == 0)
        {
            if (_episodeStart is null)
            {
                return UsbRecoveryDecision.Quiet;
            }

            _absentSince ??= now;
            if (now - _absentSince >= EpisodeGap)
            {
                EndEpisode();
                return UsbRecoveryDecision.Quiet;
            }

            return Decide(_shown, trigger: false);
        }

        _absentSince = null;
        if (_episodeStart is null)
        {
            EndEpisode();
            _episodeStart = now;
        }

        var delay = problems.Any(p => p.Kind == UsbProblemKind.FailedEnumeration) ? FailedEnumerationDelay : PhoneDeviceDelay;
        if (now - _episodeStart < delay)
        {
            return Decide(UsbNotice.None, trigger: false);
        }

        var trigger = false;
        var settled = _attempts > 0 && now - _lastAttempt >= SettleTime;
        UsbNotice notice;
        if (automatic && _attempts < AttemptsPerEpisode)
        {
            trigger = _lastAttempt is null || now - _lastAttempt >= MinimumInterval;
            if (trigger)
            {
                _attempts++;
                _lastAttempt = now;
            }

            notice = UsbNotice.Fixing;
        }
        else if (automatic)
        {
            notice = settled ? UsbNotice.Advice : UsbNotice.Fixing;
        }
        else
        {
            notice = settled ? UsbNotice.Advice : UsbNotice.Offer;
        }

        _shown = notice;
        return Decide(notice, trigger);
    }

    /// <summary>
    /// A repair the person started counts against the same limits: always for the spacing, and
    /// towards the episode's attempts when there is one (the driver repair can be asked for
    /// without any failed node).
    /// </summary>
    public void RecordAttempt(DateTime now)
    {
        _lastAttempt = now;
        if (InEpisode)
        {
            _attempts++;
        }
    }

    /// <summary>"Hide": say nothing more until this episode ends. Repairs carry on.</summary>
    public void Dismiss() => _dismissed = true;

    private UsbRecoveryDecision Decide(UsbNotice notice, bool trigger) =>
        new(_dismissed ? UsbNotice.None : notice, trigger, _attempts);

    private void EndEpisode()
    {
        _episodeStart = null;
        _absentSince = null;
        _attempts = 0;
        _dismissed = false;
        _shown = UsbNotice.None;
    }
}

/// <summary>What the notice bar says for each <see cref="UsbNotice"/>.</summary>
public static class UsbRecoveryText
{
    public const string FixingTitle = "Windows couldn't read the phone over USB. Fixing it…";
    public const string OfferTitle = "Windows couldn't read the phone over USB";
    public const string AdviceTitle = "Windows still can't read the phone over USB";

    public const string Advice =
        "Plug the phone into another USB port, one on the PC itself rather than a hub, or try another cable. " +
        "Turning off USB power saving (USB selective suspend, in the power plan's advanced settings) " +
        "and choosing File transfer in the phone's USB options also help.";

    public static (string Title, string Text) For(UsbNotice notice, IReadOnlyList<UsbProblem> problems, int attempts)
    {
        var reported = problems.Count == 0 ? "a USB device it could not read" : $"“{problems[0].Describe()}”";
        return notice switch
        {
            UsbNotice.Fixing => (FixingTitle,
                $"Windows reports {reported}. The app is asking Windows to reset it" +
                (attempts > 0 ? $" (attempt {attempts} of {UsbRecoveryPolicy.AttemptsPerEpisode})." : ".")),
            UsbNotice.Offer => (OfferTitle,
                $"Windows reports {reported}. Resetting the USB connection usually brings the phone back; Windows asks for administrator approval first."),
            UsbNotice.Advice => (AdviceTitle, Advice + DriverHint(problems)),
            _ => (string.Empty, string.Empty),
        };
    }

    /// <summary>A phone part with no driver at all is not fixed by any reset.</summary>
    private static string DriverHint(IReadOnlyList<UsbProblem> problems) =>
        problems.FirstOrDefault(p => p.Kind == UsbProblemKind.PhoneDevice && p.Node.ProblemCode == 28) is { } missing
            ? $" Windows has no driver for part of the phone: install {missing.Vendor}'s USB driver."
            : string.Empty;
}
