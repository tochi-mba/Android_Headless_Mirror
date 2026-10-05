namespace Rex.Core;

/// <summary>What to do about another phone that is ready: nothing, ask in the notice bar, or show it.</summary>
public enum OtherPhoneStep
{
    Nothing,
    Ask,
    Show,
}

/// <summary>What the other phone's session does after it ends.</summary>
public enum OtherPhoneAfterExit
{
    /// <summary>It was stopped on purpose (hidden, paused, the window closing): nothing more.</summary>
    Stopped,

    /// <summary>The phone itself left; it comes back by itself when ADB sees it again.</summary>
    WaitForPhone,

    /// <summary>It closed by itself: start it again after the main session's retry delay.</summary>
    Restart,

    /// <summary>It keeps closing: stop trying, and say so.</summary>
    GiveUp,
}

/// <summary>Per phone, what was said about showing it beside: nothing yet, or never.</summary>
public static class ShowBesideAnswers
{
    public const string Never = "never";

    public static bool IsValid(string? answer) => answer is "" or Never;
}

/// <summary>
/// The second phone's decisions, pure: whether a phone that connects is shown, asked about or
/// left alone; what follows when its session ends; and when it pauses out of sight.
/// </summary>
public static class SecondPhonePlan
{
    /// <summary>The restarts in a row it is given before it stops trying, as for the main session.</summary>
    public const int MostRestarts = 4;

    /// <summary>A session that ran this long was working; its count of restarts starts again.</summary>
    public static readonly TimeSpan Recovered = TimeSpan.FromMinutes(1);

    /// <summary>How long the window is out of sight before a paused-when-hidden phone stops.</summary>
    public static readonly TimeSpan PauseAfter = TimeSpan.FromSeconds(5);

    /// <summary>
    /// For a ready phone that is not the main one: shown when it is the one remembered from last
    /// time or the setting says always; asked about when the setting says ask; otherwise nothing.
    /// "Never for this phone" and the feature being off always win.
    /// </summary>
    public static OtherPhoneStep OnConnect(SecondPhoneSettings settings, string serial, string? showBeside, string? remembered) =>
        !settings.Enabled || showBeside == ShowBesideAnswers.Never ? OtherPhoneStep.Nothing
        : settings.Remember && !string.IsNullOrEmpty(remembered) && string.Equals(remembered, serial, StringComparison.Ordinal) ? OtherPhoneStep.Show
        : settings.WhenConnected switch
        {
            "always" => OtherPhoneStep.Show,
            "ask" => OtherPhoneStep.Ask,
            _ => OtherPhoneStep.Nothing,
        };

    /// <summary>
    /// After the other phone's session ended. <paramref name="restarts"/> is how many restarts in
    /// a row it has had; a session that ran for <see cref="Recovered"/> counts as having none.
    /// </summary>
    public static (OtherPhoneAfterExit Next, int Restarts) AfterExit(
        bool stoppedOnPurpose, bool phoneStillReady, bool restartOnExit, int restarts, TimeSpan ranFor)
    {
        if (ranFor >= Recovered)
        {
            restarts = 0;
        }

        if (stoppedOnPurpose)
        {
            return (OtherPhoneAfterExit.Stopped, restarts);
        }

        if (!phoneStillReady)
        {
            return (OtherPhoneAfterExit.WaitForPhone, restarts);
        }

        return restartOnExit && restarts < MostRestarts
            ? (OtherPhoneAfterExit.Restart, restarts + 1)
            : (OtherPhoneAfterExit.GiveUp, restarts);
    }

    /// <summary>Whether a phone shown beside should be stopped now because the window is out of sight.</summary>
    public static bool PausesNow(bool pauseWhenHidden, bool windowHidden, TimeSpan hiddenFor) =>
        pauseWhenHidden && windowHidden && hiddenFor >= PauseAfter;
}
