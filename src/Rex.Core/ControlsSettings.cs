namespace Rex.Core;

/// <summary>
/// The Controls tab as the person wants it: which sections show and in what order, which actions
/// the Phone, View and gesture grids hold and in what order, how many tiles a row has, and whether
/// a tile says its name under its icon (it always says it in its tooltip).
/// </summary>
public sealed record ControlsSettings
{
    public const int MostTiles = 24;
    public const int FewestColumns = 2;
    public const int MostColumns = 4;

    /// <summary>The tab's sections, in the order they ship in.</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> AllSections =
    [
        ("apps", "Apps"),
        ("screen", "Second screen"),
        ("phone", "Phone"),
        ("orientation", "Phone orientation"),
        ("view", "View"),
        ("keyboard", "Keyboard"),
        ("zoom", "Zoom"),
        ("copies", "Copies"),
        ("files", "Files"),
        ("clipboard", "Clipboard"),
        ("pattern", "Pattern guide"),
    ];

    public static readonly string[] DefaultPhoneTiles =
        ["home", "back", "recents", "power", "wake", "sleep", "volume-up", "volume-down", "mute", "notifications", "quick-settings", "collapse"];

    /// <summary>
    /// What the PC does with the picture. Rotating the phone itself is under phone orientation, and
    /// scrcpy's FPS counter is left to the command line: it prints to a console nobody can see here.
    /// </summary>
    public static readonly string[] DefaultViewTiles = ["rotate-left", "rotate-right", "pause", "reset-capture", "screenshot", "fullscreen"];

    /// <summary>Up then down, left then right, the way their keys sit; tap and like end the rows.</summary>
    public static readonly string[] DefaultGestureTiles = ["swipe-down", "swipe-up", "tap", "swipe-right", "swipe-left", "like"];

    /// <summary>The sections that show, in order; one left out is hidden.</summary>
    public List<string> Sections { get; set; } = [.. AllSections.Select(s => s.Id)];

    public List<string> PhoneTiles { get; set; } = [.. DefaultPhoneTiles];

    public List<string> ViewTiles { get; set; } = [.. DefaultViewTiles];

    public List<string> GestureTiles { get; set; } = [.. DefaultGestureTiles];

    /// <summary>Tiles in a row: 2 to 4.</summary>
    public int Columns { get; set; } = 3;

    /// <summary>A tile's name under its icon; off shows the icon alone, with the name in its tooltip.</summary>
    public bool TileLabels { get; set; } = true;

    public ControlsSettings Copy() => this with
    {
        Sections = [.. Sections],
        PhoneTiles = [.. PhoneTiles],
        ViewTiles = [.. ViewTiles],
        GestureTiles = [.. GestureTiles],
    };

    /// <summary>Known sections and actions only, each once, in the order given; the column count in range.</summary>
    public void Normalize()
    {
        Sections = (Sections ?? []).Select(s => (s ?? string.Empty).Trim().ToLowerInvariant())
            .Where(s => AllSections.Any(known => known.Id == s)).Distinct().ToList();
        PhoneTiles = Actions(PhoneTiles);
        ViewTiles = Actions(ViewTiles);
        GestureTiles = Actions(GestureTiles);
        Columns = Math.Clamp(Columns, FewestColumns, MostColumns);
    }

    private static List<string> Actions(List<string>? ids) =>
        (ids ?? []).Select(id => MirrorActions.Find(id ?? string.Empty)?.Id).OfType<string>().Distinct().Take(MostTiles).ToList();

    /// <summary>Every section, the ones that show first in their order, then the hidden ones in the order they ship.</summary>
    public IReadOnlyList<(string Id, string Name, bool Shown)> SectionRows() =>
    [
        .. Sections.Select(id => (id, AllSections.First(s => s.Id == id).Name, true)),
        .. AllSections.Where(s => !Sections.Contains(s.Id)).Select(s => (s.Id, s.Name, false)),
    ];

    /// <summary>A list with one item moved by <paramref name="by"/> places (it stays inside the list).</summary>
    public static List<string> Moved(IReadOnlyList<string> list, string id, int by)
    {
        var moved = list.ToList();
        var at = moved.IndexOf(id);
        if (at < 0)
        {
            return moved;
        }

        var to = Math.Clamp(at + by, 0, moved.Count - 1);
        moved.RemoveAt(at);
        moved.Insert(to, id);
        return moved;
    }
}
