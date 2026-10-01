using System.Globalization;

namespace Rex.Core;

/// <summary>An app opened from this PC: when it was opened last and how often.</summary>
public sealed record RecentApp(string Package, DateTimeOffset OpenedUtc, int Times);

/// <summary>One row of the Apps tab.</summary>
/// <param name="Favourite">The app is starred.</param>
/// <param name="Missing">A favourite that is not on the phone any more.</param>
/// <param name="Hidden">The person hid it from the lists.</param>
public sealed record AppEntry(PhoneApp App, bool Favourite, bool Missing = false, bool Hidden = false);

/// <summary>A group of rows under a heading.</summary>
public sealed record AppSection(string Id, string Title, IReadOnlyList<AppEntry> Apps);

/// <summary>
/// How the Apps tab is laid out: favourites first in the order they were put in, then the apps
/// opened last (while sorting by name), then the person's own apps and, when asked, the phone's.
/// A search is one list of matches across all of them. Starring, ordering and remembering what
/// was opened are here too, so the app and the command line keep the same lists.
/// </summary>
public static class AppOrder
{
    /// <summary>How many recent apps the RECENT group shows.</summary>
    public const int RecentShown = 5;

    public const string Favourites = "favourites";
    public const string Recent = "recent";
    public const string Yours = "yours";
    public const string System = "system";
    public const string Matches = "matches";
    public const string HiddenApps = "hidden";

    public static IReadOnlyList<AppSection> Sections(
        IReadOnlyList<PhoneApp> apps,
        IReadOnlyList<string> favourites,
        IReadOnlyList<RecentApp> recent,
        AppsSettings settings,
        string? query = null,
        bool showHidden = false)
    {
        var starred = favourites.ToHashSet(StringComparer.Ordinal);
        var hidden = settings.Hidden.ToHashSet(StringComparer.Ordinal);
        AppEntry Entry(PhoneApp app) => new(app, starred.Contains(app.Package), Hidden: hidden.Contains(app.Package));
        var sections = new List<AppSection>();
        void Add(string id, string title, IEnumerable<AppEntry> entries)
        {
            var list = entries.ToArray();
            if (list.Length > 0)
            {
                sections.Add(new AppSection(id, title, list));
            }
        }

        var visible = apps.Where(a => showHidden || !hidden.Contains(a.Package)).ToArray();
        if (!string.IsNullOrWhiteSpace(query))
        {
            Add(Matches, "MATCHES", AppList.Search(Sort(visible, recent, settings.SortBy), query).Select(Entry));
            return sections;
        }

        var byPackage = apps.GroupBy(a => a.Package, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Add(Favourites, "FAVOURITES", favourites.Select(p => byPackage.TryGetValue(p, out var app)
            ? Entry(app)
            : new AppEntry(new PhoneApp(p, p, false), true, Missing: true)));

        var shown = apps.Where(a => !hidden.Contains(a.Package)).ToArray();
        if (settings.ShowRecent && settings.SortBy == "name" && settings.RecentCount > 0)
        {
            Add(Recent, "RECENT", recent
                .OrderByDescending(r => r.OpenedUtc)
                .Select(r => byPackage.GetValueOrDefault(r.Package))
                .Where(a => a is not null && !hidden.Contains(a.Package) && !starred.Contains(a.Package))
                .Take(RecentShown)
                .Select(a => Entry(a!)));
        }

        Add(Yours, "YOUR APPS", Sort(shown.Where(a => !a.System), recent, settings.SortBy).Select(Entry));
        if (settings.ShowSystem)
        {
            Add(System, "SYSTEM APPS", Sort(shown.Where(a => a.System), recent, settings.SortBy).Select(Entry));
        }

        if (showHidden)
        {
            Add(HiddenApps, "HIDDEN APPS", Sort(apps.Where(a => hidden.Contains(a.Package)), recent, settings.SortBy).Select(Entry));
        }

        return sections;
    }

    /// <summary>By name as this PC sorts words; or opened last first; or opened most first. Ties go by name.</summary>
    public static IReadOnlyList<PhoneApp> Sort(IEnumerable<PhoneApp> apps, IReadOnlyList<RecentApp> recent, string sortBy)
    {
        var names = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        var opened = recent.GroupBy(r => r.Package, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byName = apps.OrderBy(a => a.Name, names).ThenBy(a => a.Package, StringComparer.Ordinal);
        return (sortBy switch
        {
            "recent" => apps.OrderByDescending(a => opened.TryGetValue(a.Package, out var r) ? r.OpenedUtc : DateTimeOffset.MinValue)
                .ThenBy(a => a.Name, names).ThenBy(a => a.Package, StringComparer.Ordinal),
            "most-used" => apps.OrderByDescending(a => opened.TryGetValue(a.Package, out var r) ? r.Times : 0)
                .ThenBy(a => a.Name, names).ThenBy(a => a.Package, StringComparer.Ordinal),
            _ => byName,
        }).ToArray();
    }

    /// <summary>The recent list after opening an app: it moves to the front with one more opening, and the list keeps <paramref name="keep"/>.</summary>
    public static IReadOnlyList<RecentApp> Opened(IReadOnlyList<RecentApp> recent, string package, DateTimeOffset now, int keep)
    {
        var times = recent.FirstOrDefault(r => r.Package == package)?.Times ?? 0;
        return recent
            .Where(r => r.Package != package)
            .Prepend(new RecentApp(package, now, times + 1))
            .OrderByDescending(r => r.OpenedUtc)
            .Take(Math.Max(0, keep))
            .ToArray();
    }

    /// <summary>Favourites with the package added at the end, or taken out.</summary>
    public static IReadOnlyList<string> Star(IReadOnlyList<string> favourites, string package, bool on) =>
        on ? (favourites.Contains(package, StringComparer.Ordinal) ? favourites.ToArray() : [.. favourites, package])
           : favourites.Where(p => p != package).ToArray();

    /// <summary>Favourites with the package moved up (negative) or down (positive), stopping at either end.</summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<string> favourites, string package, int delta)
    {
        var list = favourites.ToList();
        var from = list.IndexOf(package);
        if (from < 0)
        {
            return list;
        }

        var to = Math.Clamp(from + delta, 0, list.Count - 1);
        list.RemoveAt(from);
        list.Insert(to, package);
        return list;
    }
}
