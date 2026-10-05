namespace Rex.Core;

/// <summary>
/// A mirror started with a time limit (scrcpy --time-limit) ends by itself when the time is up.
/// That is a stop the person asked for, never a crash to start again from.
/// </summary>
public static class MirrorTimeLimit
{
    /// <summary>scrcpy counts from its own start, a moment after the app's; this much early still counts.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(2);

    /// <summary>Whether a mirror started with a limit of <paramref name="minutes"/> ended because its time was up.</summary>
    public static bool Reached(int minutes, TimeSpan ranFor) =>
        minutes > 0 && ranFor >= TimeSpan.FromMinutes(minutes) - Slack;

    /// <summary>What the status line says once it has stopped.</summary>
    public static string Stopped(int minutes) =>
        $"Stopped after {TimeWords.Minutes(minutes)}, as set in Settings. Press Start to mirror again.";
}
