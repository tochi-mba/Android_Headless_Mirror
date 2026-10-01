using System.Globalization;

namespace Rex.Core;

public enum TransferState
{
    Waiting,
    Sending,
    Installing,
    Done,
    Installed,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>
/// How far a transfer has got, how fast it goes and how long is left, from samples of bytes sent
/// over time; and the words each state is shown with.
/// </summary>
public static class TransferProgress
{
    /// <summary>The speed is averaged over this much of the most recent time.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    public static bool IsFinished(TransferState state) => state is TransferState.Done or TransferState.Installed or TransferState.Skipped or TransferState.Failed or TransferState.Cancelled;

    public static int Percent(long sent, long total) =>
        total <= 0 ? (sent > 0 ? 100 : 0) : (int)Math.Clamp(sent * 100 / total, 0, 100);

    /// <summary>Bytes a second over the last <see cref="Window"/>, from (elapsed, bytes) samples in order; 0 when unknown.</summary>
    public static double Speed(IReadOnlyList<(TimeSpan Elapsed, long Bytes)> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var last = samples[^1];
        var first = samples.LastOrDefault(s => last.Elapsed - s.Elapsed >= Window, samples[0]);
        var seconds = (last.Elapsed - first.Elapsed).TotalSeconds;
        return seconds <= 0 ? 0 : Math.Max(0, (last.Bytes - first.Bytes) / seconds);
    }

    /// <summary>How long is left at this speed, or null when it cannot be said.</summary>
    public static TimeSpan? Left(long sent, long total, double speed) =>
        speed <= 0 || total <= 0 ? null : TimeSpan.FromSeconds(Math.Max(0, total - sent) / speed);

    /// <summary>"34% · 12 MB/s · about 20 s left", as far as each part is known.</summary>
    public static string Sending(long sent, long total, double speed)
    {
        var parts = new List<string> { Percent(sent, total) + "%" };
        if (speed > 0)
        {
            parts.Add(Size((long)speed) + "/s");
        }

        if (Left(sent, total, speed) is { } left)
        {
            parts.Add("about " + Duration(left) + " left");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The words a finished or waiting transfer shows.</summary>
    public static string Words(TransferState state, string folder, string? why) => state switch
    {
        TransferState.Waiting => "Waiting",
        TransferState.Sending => "Sending",
        TransferState.Installing => "Installing",
        TransferState.Done => "In " + TransferPlan.FolderName(folder),
        TransferState.Installed => "Installed",
        TransferState.Skipped => "Skipped: " + (why ?? "already there"),
        TransferState.Failed => "Failed: " + (why ?? "it did not arrive"),
        _ => "Cancelled",
    };

    /// <summary>A size as people read it: "640 KB", "12 MB", "1.4 GB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
    };

    public static string Duration(TimeSpan span) => span.TotalSeconds switch
    {
        < 60 => Math.Ceiling(span.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s",
        < 3600 => Math.Ceiling(span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min",
        _ => span.TotalHours.ToString("0.0", CultureInfo.InvariantCulture) + " h",
    };

    /// <summary>adb's own failure words, in the app's.</summary>
    public static string Why(string adbText) =>
        adbText.Contains("No space left", StringComparison.OrdinalIgnoreCase) ? "there is no space left on the phone"
        : adbText.Contains("no devices", StringComparison.OrdinalIgnoreCase) || adbText.Contains("not found", StringComparison.OrdinalIgnoreCase) && adbText.Contains("device", StringComparison.OrdinalIgnoreCase) ? "the phone disconnected"
        : adbText.Contains("INSTALL_FAILED_VERSION_DOWNGRADE", StringComparison.Ordinal) ? "a newer version is installed; allow older versions in Settings to install it"
        : adbText.Contains("INSTALL_FAILED_TEST_ONLY", StringComparison.Ordinal) ? "it is a test build; allow test builds in Settings to install it"
        : adbText.Contains("INSTALL_FAILED_ALREADY_EXISTS", StringComparison.Ordinal) ? "it is already installed; allow updates in Settings to replace it"
        : adbText.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ? "the phone did not allow writing there"
        : adbText.Trim().Split('\n').Last().Trim();
}
