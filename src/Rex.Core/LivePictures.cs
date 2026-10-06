namespace Rex.Core;

/// <summary>
/// The live pictures the window draws itself, the soft background and the navigator's picture,
/// on a PC that draws without a graphics card (Remote Desktop, many virtual machines). There every
/// frame is drawn on the processor, and a live picture at its usual rate costs half a core, so by
/// default it runs slower; it can be kept as set, or turned off there.
/// </summary>
public static class LivePictures
{
    public const string Slower = "slower";
    public const string AsSet = "same";
    public const string Off = "off";

    public static readonly string[] Choices = [Slower, AsSet, Off];

    /// <summary>The most frames a second a live picture runs at, slowed down, without a graphics card.</summary>
    public const double SlowerRate = 3;

    /// <summary>The frames a second a live picture set to <paramref name="wanted"/> runs at; 0 means it is not shown.</summary>
    public static double Rate(double wanted, bool withoutGraphicsCard, string choice) =>
        wanted <= 0 ? 0
        : !withoutGraphicsCard ? wanted
        : choice switch
        {
            Off => 0,
            AsSet => wanted,
            _ => Math.Min(wanted, SlowerRate),
        };
}
