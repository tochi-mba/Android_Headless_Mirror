namespace Rex.Core;

/// <summary>How long ago something happened, in the words a person would use.</summary>
public static class TimeWords
{
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
