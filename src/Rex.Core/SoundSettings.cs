namespace Rex.Core;

/// <summary>
/// The phone's sound as it plays on this PC: its level, mute, balance and the rules that lower or
/// silence it. Only the scrcpy process's own audio session on this PC is changed; the phone's own
/// volume is never touched (that is the phone's volume actions).
/// </summary>
public sealed record SoundSettings
{
    public const double SmallestStep = 0.01;
    public const double LargestStep = 0.25;
    public const int ShortestLowering = 200;
    public const int LongestLowering = 5000;
    public const int LongestFade = 1000;

    /// <summary>The volume on this PC, 0 to 1; with <see cref="RememberPerPhone"/> on, where a new phone starts.</summary>
    public double Volume { get; set; } = 1;

    /// <summary>Muted on this PC.</summary>
    public bool Muted { get; set; }

    /// <summary>What one key press or wheel notch changes the volume by (0.01 to 0.25).</summary>
    public double Step { get; set; } = 0.05;

    /// <summary>Each phone keeps its own volume and mute, in state.json.</summary>
    public bool RememberPerPhone { get; set; } = true;

    /// <summary>Every mirror starts muted.</summary>
    public bool StartMuted { get; set; }

    public bool MuteWhenHidden { get; set; }

    public bool MuteWhenBehind { get; set; }

    public bool MuteWhenLocked { get; set; }

    /// <summary>Lower the sound for a moment after each key typed into the phone.</summary>
    public bool LowerWhileTyping { get; set; }

    /// <summary>The fraction of the volume it is lowered to, 0 to 1.</summary>
    public double LowerTo { get; set; } = 0.3;

    /// <summary>How long after the last key the sound comes back, in milliseconds (200 to 5000).</summary>
    public int LowerForMs { get; set; } = 800;

    /// <summary>How long a change of level takes, in milliseconds (0 is at once, up to 1000).</summary>
    public int FadeMs { get; set; } = 150;

    /// <summary>-1 is the left only, 0 both, 1 the right only.</summary>
    public double Balance { get; set; }

    /// <summary>A change made in the Windows volume mixer is adopted rather than put back.</summary>
    public bool FollowMixer { get; set; } = true;

    /// <summary>The sound panel shows a level meter.</summary>
    public bool ShowLevel { get; set; } = true;

    /// <summary>The wheel over the top bar's sound button changes the volume.</summary>
    public bool WheelOnButton { get; set; } = true;

    public SoundSettings Copy() => this with { };

    public void Normalize()
    {
        Volume = Fraction(Volume, 1);
        Step = double.IsFinite(Step) ? Math.Clamp(Step, SmallestStep, LargestStep) : 0.05;
        LowerTo = Fraction(LowerTo, 0.3);
        LowerForMs = Math.Clamp(LowerForMs, ShortestLowering, LongestLowering);
        FadeMs = Math.Clamp(FadeMs, 0, LongestFade);
        Balance = double.IsFinite(Balance) ? Math.Clamp(Balance, -1, 1) : 0;
    }

    /// <summary>A value from 0 to 1, or the fallback when it is not a number.</summary>
    public static double Fraction(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}
