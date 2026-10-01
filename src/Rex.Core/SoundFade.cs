namespace Rex.Core;

/// <summary>Moves a level towards its target at a steady pace, so a change never clicks.</summary>
public static class SoundFade
{
    /// <summary>
    /// The level after <paramref name="elapsed"/>: the whole range takes <paramref name="fadeMs"/>,
    /// the target is reached exactly and never passed, and a fade of 0 arrives at once.
    /// </summary>
    public static double Next(double current, double target, TimeSpan elapsed, int fadeMs)
    {
        if (fadeMs <= 0)
        {
            return target;
        }

        var step = Math.Max(0, elapsed.TotalMilliseconds) / fadeMs;
        return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
    }
}
