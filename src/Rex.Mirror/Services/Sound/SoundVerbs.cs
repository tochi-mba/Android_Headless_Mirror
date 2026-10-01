using Rex.Core;

namespace Rex.Mirror.Services.Sound;

/// <summary>The pipe's <c>sound</c> command played on the running sound.</summary>
internal static class SoundVerbs
{
    /// <summary>Applies a verb or a level; returns why it was refused, or null.</summary>
    public static string? Apply(PhoneSound sound, string verb)
    {
        if (SoundCommand.Level(verb) is { } level)
        {
            sound.SetVolume(level);
            return null;
        }

        switch (verb.ToLowerInvariant())
        {
            case "up":
                sound.Step(1);
                return null;
            case "down":
                sound.Step(-1);
                return null;
            case "mute":
                sound.SetMuted(true);
                return null;
            case "unmute":
                sound.SetMuted(false);
                return null;
            case "toggle":
                sound.SetMuted(!sound.Muted);
                return null;
            default:
                return $"'{verb}' is not a level or a verb. Use: {SoundCommand.Usage}";
        }
    }
}
