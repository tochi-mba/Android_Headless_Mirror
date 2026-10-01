using System.Text.RegularExpressions;

namespace Rex.Core;

public enum ClashAnswer
{
    Send,
    KeepBoth,
    Replace,
    Skip,
    Ask,
}

/// <summary>What to do when the phone already has a file of the same name, and the name a kept copy gets.</summary>
public static partial class NameClash
{
    [GeneratedRegex(@"^(?<stem>.*?) \((?<n>\d+)\)$")]
    private static partial Regex Numbered();

    /// <summary>What the setting says to do, or Send when there is no clash.</summary>
    public static ClashAnswer Decide(string policy, bool exists) => !exists ? ClashAnswer.Send : policy switch
    {
        "ask" => ClashAnswer.Ask,
        "replace" => ClashAnswer.Replace,
        "skip" => ClashAnswer.Skip,
        _ => ClashAnswer.KeepBoth,
    };

    /// <summary>
    /// A name that is not taken: "photo.jpg" becomes "photo (2).jpg", then "photo (3).jpg". A name
    /// that already ends in a number counts on from it; a name with no extension, or a dot-file,
    /// gets the number at the end.
    /// </summary>
    public static string KeepBoth(string name, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var (stem, extension) = dot > 0 ? (name[..dot], name[dot..]) : (name, string.Empty);
        var n = 2;
        if (Numbered().Match(stem) is { Success: true } numbered && int.TryParse(numbered.Groups["n"].Value, out var already))
        {
            stem = numbered.Groups["stem"].Value;
            n = already + 1;
        }

        while (taken.Contains($"{stem} ({n}){extension}"))
        {
            n++;
        }

        return $"{stem} ({n}){extension}";
    }
}
