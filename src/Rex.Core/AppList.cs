using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rex.Core;

/// <summary>An app the phone can open: its own name, its package, and whether it came with the phone.</summary>
public sealed record PhoneApp(string Name, string Package, bool System);

/// <summary>What <see cref="AppList.Resolve"/> found for a name or package someone typed.</summary>
/// <param name="App">The one app it means, or null.</param>
/// <param name="Candidates">Every app it could mean, when there is more than one.</param>
/// <param name="Error">Why there is no single app, in words.</param>
public sealed record AppMatch(PhoneApp? App, IReadOnlyList<PhoneApp> Candidates, string? Error);

/// <summary>
/// The phone's apps as scrcpy lists them (<c>scrcpy --list-apps</c>), and finding one by what a
/// person types. scrcpy writes one app a line after a header: <c>" * "</c> for an app that came
/// with the phone and <c>" - "</c> for one that was installed, the name padded to 30 characters and
/// then the package. A name of 30 characters or more is written alone, with its package on the
/// next line under the package column.
/// </summary>
public static partial class AppList
{
    public const string Header = "List of apps:";
    private const int NameColumn = 30;

    [GeneratedRegex(@"^ (?<mark>[*-]) (?<rest>.+)$")]
    private static partial Regex AppLine();

    [GeneratedRegex(@"^\s{4,}(?<package>\S+)$")]
    private static partial Regex ContinuationLine();

    [GeneratedRegex(@"^(?<name>.+?)\s+(?<package>\S+)$")]
    private static partial Regex LooseLine();

    /// <summary>
    /// Reads the apps from scrcpy's output. Lines before the header, blank lines and anything else
    /// that is not an app are skipped; the first app with a package wins over a later duplicate.
    /// </summary>
    public static IReadOnlyList<PhoneApp> Parse(IEnumerable<string> lines)
    {
        var apps = new List<PhoneApp>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var listing = false;
        (string Name, bool System)? waiting = null;

        void Add(string name, string package, bool system)
        {
            if (PackageName.IsValid(package) && seen.Add(package))
            {
                apps.Add(new PhoneApp(name.Trim().Length == 0 ? package : name.Trim(), package, system));
            }
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', '\n', ' ', '\t');
            if (!listing)
            {
                listing = line.EndsWith(Header, StringComparison.Ordinal);
                continue;
            }

            if (waiting is { } pending && ContinuationLine().Match(line) is { Success: true } continued)
            {
                waiting = null;
                Add(pending.Name, continued.Groups["package"].Value, pending.System);
                continue;
            }

            waiting = null;
            if (AppLine().Match(line) is not { Success: true } match)
            {
                continue;
            }

            var system = match.Groups["mark"].Value == "*";
            var rest = match.Groups["rest"].Value;
            // The name fills exactly the name column, then one space and the package.
            if (rest.Length > NameColumn + 1 && rest[NameColumn] == ' ' && rest[(NameColumn + 1)..] is var tail &&
                !tail.Contains(' ', StringComparison.Ordinal) && PackageName.IsValid(tail))
            {
                Add(rest[..NameColumn], tail, system);
            }
            else if (LooseLine().Match(rest) is { Success: true } loose && PackageName.IsValid(loose.Groups["package"].Value))
            {
                Add(loose.Groups["name"].Value, loose.Groups["package"].Value, system);
            }
            else
            {
                waiting = (rest, system);
            }
        }

        return apps;
    }

    /// <summary>
    /// The apps whose name or package contains every word typed, ignoring case and accents. An app
    /// whose name starts with what was typed comes first, then one with a word that does, then a
    /// name that contains it, then a package that does; otherwise the order given is kept.
    /// </summary>
    public static IReadOnlyList<PhoneApp> Search(IEnumerable<PhoneApp> apps, string? query)
    {
        var whole = Fold(query ?? string.Empty);
        var words = whole.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return apps.ToArray();
        }

        return apps
            .Select((app, order) => (App: app, Order: order, Name: Fold(app.Name), Package: app.Package.ToLowerInvariant()))
            .Where(a => words.All(w => a.Name.Contains(w, StringComparison.Ordinal) || a.Package.Contains(w, StringComparison.Ordinal)))
            .Select(a => (a.App, a.Order, Rank:
                a.Name.StartsWith(whole, StringComparison.Ordinal) ? 0 :
                a.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(n => n.StartsWith(words[0], StringComparison.Ordinal)) ? 1 :
                words.All(w => a.Name.Contains(w, StringComparison.Ordinal)) ? 2 : 3))
            .OrderBy(a => a.Rank)
            .ThenBy(a => a.Order)
            .Select(a => a.App)
            .ToArray();
    }

    /// <summary>
    /// The one app a name or package means: an exact package first, then an exact name, then the
    /// only name that starts with it. Two apps with the same name are both offered, by package.
    /// </summary>
    public static AppMatch Resolve(IReadOnlyList<PhoneApp> apps, string text)
    {
        var wanted = text.Trim();
        if (apps.FirstOrDefault(a => string.Equals(a.Package, wanted, StringComparison.OrdinalIgnoreCase)) is { } byPackage)
        {
            return new AppMatch(byPackage, [byPackage], null);
        }

        var folded = Fold(wanted);
        var exact = apps.Where(a => Fold(a.Name) == folded).ToArray();
        var candidates = exact.Length > 0 ? exact : apps.Where(a => folded.Length > 0 && Fold(a.Name).StartsWith(folded, StringComparison.Ordinal)).ToArray();
        return candidates.Length switch
        {
            1 => new AppMatch(candidates[0], candidates, null),
            0 => new AppMatch(null, [], $"No app on the phone is called \"{wanted}\"."),
            _ => new AppMatch(null, candidates, $"More than one app is called \"{wanted}\": " +
                string.Join(", ", candidates.Select(a => $"{a.Name} ({a.Package})")) + ". Use the package name."),
        };
    }

    /// <summary>Lower case without accents, with runs of spaces as one: "Café  Maps" is "cafe maps".</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
