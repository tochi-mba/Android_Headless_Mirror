namespace Rex.Core;

/// <summary>How long ago something happened, and how long something lasts, in the words a person would use.</summary>
public static class TimeWords
{
    /// <summary>A whole number of minutes as "1 minute", "45 minutes", "1 hour", "2 hours 30 minutes".</summary>
    public static string Minutes(int minutes)
    {
        static string Count(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? Count(rest, "minute")
            : rest == 0 ? Count(hours, "hour")
            : Count(hours, "hour") + " " + Count(rest, "minute");
    }

    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var gone = now - then;
        return gone switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 2 } => "a minute ago",
            { TotalHours: < 1 } => $"{(int)gone.TotalMinutes} minutes ago",
            { TotalHours: < 2 } => "an hour ago",
            { TotalDays: < 1 } => $"{(int)gone.TotalHours} hours ago",
            { TotalDays: < 2 } => "yesterday",
            _ => $"{(int)gone.TotalDays} days ago",
        };
    }
}
