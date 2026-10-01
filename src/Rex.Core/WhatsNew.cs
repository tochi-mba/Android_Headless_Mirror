namespace Rex.Core;

/// <summary>
/// When the window says what is new: once per version, after an update. Never after a first
/// install (there is no "before"), a downgrade, or a version it cannot read, and never once the
/// person has switched it off.
/// </summary>
public static class WhatsNew
{
    public static bool ShouldOffer(string? lastRunVersion, string currentVersion, bool enabled) =>
        enabled &&
        Version.TryParse(lastRunVersion, out var last) &&
        Version.TryParse(currentVersion, out var current) &&
        current > last;
}
