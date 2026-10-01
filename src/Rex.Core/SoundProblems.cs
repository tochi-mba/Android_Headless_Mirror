namespace Rex.Core;

/// <summary>
/// Why there is no phone sound to control on this PC, in words. scrcpy says so in its own output
/// when the phone cannot or will not send its sound; the rest the app knows by itself.
/// </summary>
public static class SoundProblems
{
    public const string AudioOff = "Phone sound is off in Settings.";
    public const string NoMirror = "No phone is being mirrored.";
    public const string Waiting = "Waiting for the phone's sound…";
    public const string TooOld = "This phone cannot send its sound: that needs Android 11 or later.";
    public const string Locked = "Android 11 only sends the sound when the phone is unlocked as the mirror starts. Unlock it and restart the mirror.";
    public const string Refused = "The phone did not send its sound. Restarting the mirror may help.";

    private static readonly (string Says, string Means)[] Warnings =
    [
        ("not supported before Android 11", TooOld),
        ("audio capture must be started in the foreground", Locked),
        ("Failed to start audio capture", Refused),
        ("Audio capture failed", Refused),
        ("stream explicitly disabled by the device", Refused),
    ];

    /// <summary>What a line of scrcpy's output means for the sound, or null when it says nothing about it.</summary>
    public static string? FromScrcpy(string? line) =>
        string.IsNullOrEmpty(line) ? null : Warnings.FirstOrDefault(w => line.Contains(w.Says, StringComparison.OrdinalIgnoreCase)).Means;
}
