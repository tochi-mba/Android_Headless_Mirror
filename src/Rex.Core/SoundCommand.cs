using System.Globalization;

namespace Rex.Core;

/// <summary>What <c>rex sound</c> may ask for: a level from 0 to 100, or up, down, mute, unmute or toggle.</summary>
public static class SoundCommand
{
    public const string Usage = "rex sound [0-100|up|down|mute|unmute|toggle]";

    public static readonly string[] Verbs = ["up", "down", "mute", "unmute", "toggle"];

    /// <summary>Whether <paramref name="verb"/> is one of the verbs or a whole level from 0 to 100.</summary>
    public static bool IsValid(string verb) => Verbs.Contains(verb, StringComparer.OrdinalIgnoreCase) || Level(verb) is not null;

    /// <summary>The level asked for, 0 to 1, or null when the words are not a level.</summary>
    public static double? Level(string verb) =>
        int.TryParse(verb, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && percent <= 100 ? percent / 100.0 : null;
}
