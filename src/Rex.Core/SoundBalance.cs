namespace Rex.Core;

/// <summary>Balance as the levels of the left and right channels.</summary>
public static class SoundBalance
{
    /// <summary>-1 leaves only the left, 1 only the right; the side the balance moves towards stays full.</summary>
    public static (float Left, float Right) Channels(double balance)
    {
        var b = double.IsFinite(balance) ? Math.Clamp(balance, -1, 1) : 0;
        return ((float)(b > 0 ? 1 - b : 1), (float)(b < 0 ? 1 + b : 1));
    }
}
