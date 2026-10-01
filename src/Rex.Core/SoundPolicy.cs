namespace Rex.Core;

/// <summary>What the sound depends on right now.</summary>
/// <param name="Volume">The volume the person chose for this phone.</param>
/// <param name="Muted">The person muted it.</param>
/// <param name="Hidden">The window is hidden or minimised.</param>
/// <param name="Behind">Another window is in front.</param>
/// <param name="Locked">This PC is locked.</param>
/// <param name="SinceLastKey">How long ago the last key was typed into the phone.</param>
public sealed record SoundInputs(double Volume, bool Muted, bool Hidden, bool Behind, bool Locked, TimeSpan SinceLastKey);

/// <summary>The level and mute to play at, and the words for a rule that is acting, if any.</summary>
public sealed record SoundTarget(double Level, bool Muted, string? Why);

/// <summary>
/// Decides the sound from the person's choice and the rules. The person's own mute comes first and
/// needs no words; then this PC locked, the window hidden, the window behind others, and last the
/// lowering while typing. Only the first reason that applies is said.
/// </summary>
public static class SoundPolicy
{
    public const string WhyLocked = "Muted while this PC is locked";
    public const string WhyHidden = "Muted while the window is hidden";
    public const string WhyBehind = "Muted while another window is in front";
    public const string WhyTyping = "Lowered while you type";

    /// <summary>
    /// How long ago the last key went to the phone, from two <c>Environment.TickCount64</c> readings;
    /// forever when no key has gone yet.
    /// </summary>
    public static TimeSpan SinceLastKey(long nowMs, long? lastKeyMs) =>
        lastKeyMs is { } last && nowMs - last < (long)TimeSpan.MaxValue.TotalMilliseconds
            ? TimeSpan.FromMilliseconds(Math.Max(0, nowMs - last))
            : TimeSpan.MaxValue;

    public static SoundTarget Decide(SoundInputs now, SoundSettings settings)
    {
        if (now.Muted)
        {
            return new SoundTarget(now.Volume, true, null);
        }

        var silenced = (now.Locked && settings.MuteWhenLocked) ? WhyLocked
            : (now.Hidden && settings.MuteWhenHidden) ? WhyHidden
            : (now.Behind && settings.MuteWhenBehind) ? WhyBehind
            : null;
        if (silenced is not null)
        {
            return new SoundTarget(now.Volume, true, silenced);
        }

        var typing = settings.LowerWhileTyping && settings.LowerTo < 1 &&
            now.SinceLastKey < TimeSpan.FromMilliseconds(settings.LowerForMs);
        return typing
            ? new SoundTarget(now.Volume * settings.LowerTo, false, WhyTyping)
            : new SoundTarget(now.Volume, false, null);
    }
}
