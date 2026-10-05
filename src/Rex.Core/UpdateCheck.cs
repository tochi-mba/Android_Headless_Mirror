using System.Text.Json;

namespace Rex.Core;

/// <summary>
/// The optional check for a newer version: whether one is due (at most once a day), what GitHub's
/// latest release says, and whether it is worth offering (newer than this one, and not skipped).
/// It is off unless the person turns it on, and nothing is sent anywhere while it is off.
/// </summary>
public static class UpdateCheck
{
    /// <summary>GitHub's latest published release of this app (drafts and pre-releases are never "latest").</summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/tochi-mba/Android_Headless_Mirror/releases/latest";

    /// <summary>The longest the app waits for GitHub; a check that takes longer is given up without a word.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan Every = TimeSpan.FromDays(1);

    /// <summary>Whether a check is due: turned on, and never asked or asked a day ago or more.</summary>
    public static bool Due(bool enabled, DateTimeOffset? last, DateTimeOffset now) =>
        enabled && (last is not { } then || now - then >= Every || then > now);

    /// <summary>The version GitHub's latest release names (its tag, "v2.14.0"), or null for anything else.</summary>
    public static Version? Latest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String &&
                   Version.TryParse(tag.GetString()!.TrimStart('v', 'V'), out var version)
                ? version
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether to offer <paramref name="latest"/>: newer than <paramref name="current"/> and not the version skipped.</summary>
    public static bool Offer(Version? latest, string current, string? skipped) =>
        latest is not null &&
        Version.TryParse(current, out var running) && latest > running &&
        !(Version.TryParse(skipped, out var passed) && passed == latest);

    /// <summary>The release's own page, where it is downloaded.</summary>
    public static string ReleasePage(Version version) => $"{SiteLinks.Repository}/releases/tag/v{version}";
}
