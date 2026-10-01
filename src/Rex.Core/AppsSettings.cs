namespace Rex.Core;

/// <summary>
/// The Apps tab: what it lists and how, where favourites show up, and when the phone's list is
/// read. The list itself, favourites and recent apps are per phone, in state.json.
/// </summary>
public sealed record AppsSettings
{
    public static readonly IReadOnlyList<string> SortChoices = ["name", "recent", "most-used"];
    public static readonly IReadOnlyList<string> LayoutChoices = ["list", "grid"];
    public const int MostRecent = 50;
    public const int MostOnControls = 12;
    public const int MostHidden = 500;

    /// <summary>List the apps that came with the phone in their own group.</summary>
    public bool ShowSystem { get; set; }

    /// <summary>Show each app's package name under its name.</summary>
    public bool ShowPackages { get; set; } = true;

    /// <summary>name, recent (last opened first) or most-used.</summary>
    public string SortBy { get; set; } = "name";

    /// <summary>list, or grid (tiles).</summary>
    public string Layout { get; set; } = "list";

    /// <summary>While sorting by name, the apps opened last are shown first in their own group.</summary>
    public bool ShowRecent { get; set; } = true;

    /// <summary>How many recently opened apps each phone remembers (0 remembers none).</summary>
    public int RecentCount { get; set; } = 20;

    /// <summary>Favourites have tiles on the Controls tab.</summary>
    public bool FavouritesOnControls { get; set; } = true;

    /// <summary>At most this many favourite tiles on the Controls tab (1 to 12).</summary>
    public int FavouritesOnControlsMost { get; set; } = 6;

    /// <summary>Favourites have buttons on the fullscreen controls.</summary>
    public bool FavouritesInHud { get; set; }

    /// <summary>Ctrl+Alt+Shift+1 to 9 open favourites 1 to 9.</summary>
    public bool FavouriteKeys { get; set; } = true;

    /// <summary>Read the phone's list again each time it connects, once the mirror is up.</summary>
    public bool ReadOnConnect { get; set; } = true;

    /// <summary>Close an app before opening it, so it starts afresh.</summary>
    public bool OpenFresh { get; set; }

    /// <summary>When the mirror stops, close the apps that were opened from this app.</summary>
    public bool CloseWhenMirrorStops { get; set; }

    /// <summary>Packages left out of the lists (still favourites if starred).</summary>
    public List<string> Hidden { get; set; } = [];

    public AppsSettings Copy() => this with { Hidden = [.. Hidden] };

    public void Normalize()
    {
        SortBy = MirrorSettings.OneOf(SortChoices, SortBy, "name");
        Layout = MirrorSettings.OneOf(LayoutChoices, Layout, "list");
        RecentCount = Math.Clamp(RecentCount, 0, MostRecent);
        FavouritesOnControlsMost = Math.Clamp(FavouritesOnControlsMost, 1, MostOnControls);
        Hidden = (Hidden ?? [])
            .Select(p => (p ?? string.Empty).Trim())
            .Where(PackageName.IsValid)
            .Distinct(StringComparer.Ordinal)
            .Take(MostHidden)
            .ToList();
    }
}
