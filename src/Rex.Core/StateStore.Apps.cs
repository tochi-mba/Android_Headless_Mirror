namespace Rex.Core;

/// <summary>Each phone's apps, favourites and recently opened apps, kept with the rest of its state.</summary>
public sealed partial class StateStore
{
    /// <summary>Remembers the apps just read from a phone.</summary>
    public void SetApps(string serial, IReadOnlyList<PhoneApp> apps, DateTimeOffset readUtc) => Update(state =>
    {
        var profile = GetOrAdd(state, serial);
        profile.Apps = apps.ToArray();
        profile.AppsReadUtc = readUtc;
    });

    /// <summary>Stars an app (it goes last among the favourites) or takes its star away.</summary>
    public void SetFavourite(string serial, string package, bool on) => Update(state =>
    {
        var profile = GetOrAdd(state, serial);
        profile.FavouriteApps = AppOrder.Star(profile.FavouriteApps ?? [], package, on);
    });

    /// <summary>Moves a favourite up (negative) or down (positive) the list.</summary>
    public void MoveFavourite(string serial, string package, int delta) => Update(state =>
    {
        var profile = GetOrAdd(state, serial);
        profile.FavouriteApps = AppOrder.Move(profile.FavouriteApps ?? [], package, delta);
    });

    /// <summary>Remembers the app the second screen has, for opening it again later.</summary>
    public void SetSecondScreenApp(string serial, string package) => Update(state => GetOrAdd(state, serial).SecondScreenApp = package);

    /// <summary>Notes that an app was opened, keeping at most <paramref name="keep"/> recent apps.</summary>
    public void NoteAppOpened(string serial, string package, DateTimeOffset now, int keep) => Update(state =>
    {
        var profile = GetOrAdd(state, serial);
        profile.RecentApps = AppOrder.Opened(profile.RecentApps ?? [], package, now, keep);
    });
}
