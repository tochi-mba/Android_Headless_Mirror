namespace Rex.Core;

/// <summary>
/// A second screen for one app: a display of its own on the phone, shown in the window beside the
/// phone or instead of it, the size it is made, and what Android puts on it.
/// </summary>
public sealed record SecondScreenSettings
{
    public static readonly IReadOnlyList<string> PlacementChoices = ["beside", "instead"];
    public static readonly IReadOnlyList<string> SideChoices = ["right", "left"];
    public static readonly IReadOnlyList<string> SizeChoices = ["follow", "phone", "720p", "1080p", "1440p", "custom"];
    public static readonly IReadOnlyList<string> KeyboardChoices = ["here", "phone", "never"];
    public static readonly IReadOnlyList<int> MaxSizeChoices = [0, 480, 720, 1024, 1280, 1600, 1920, 2560, 3840];
    public const int SmallestSide = 320;
    public const int LargestSide = 3840;
    public const int SmallestDpi = 100;
    public const int LargestDpi = 640;
    public const int NarrowestView = 240;
    public const int WidestMinimum = 1200;

    /// <summary>beside the phone, or instead of it (the phone's view hides; its session keeps running).</summary>
    public string Placement { get; set; } = "beside";

    /// <summary>Which side of the phone it goes on.</summary>
    public string Side { get; set; } = "right";

    /// <summary>follow (exactly the view's pixels), phone, 720p, 1080p, 1440p or custom.</summary>
    public string Size { get; set; } = "follow";

    public int CustomWidth { get; set; } = 1920;

    public int CustomHeight { get; set; } = 1080;

    /// <summary>Fixed sizes upright instead of on their side.</summary>
    public bool Portrait { get; set; }

    /// <summary>Text and buttons: a density from 100 to 640, or 0 for the phone's own.</summary>
    public int Dpi { get; set; }

    /// <summary>The video's longest side, or 0 for no limit.</summary>
    public int MaxSize { get; set; }

    /// <summary>Android's navigation bar and status bar on it.</summary>
    public bool Decorations { get; set; } = true;

    /// <summary>Closing it moves its app to the phone's own screen instead of closing the app.</summary>
    public bool KeepAppsOnClose { get; set; } = true;

    /// <summary>Where Android's on-screen keyboard appears: here (on it), phone, or never.</summary>
    public string Keyboard { get; set; } = "here";

    /// <summary>When the mirror starts, open it again with the app it last had.</summary>
    public bool ReopenOnStart { get; set; }

    /// <summary>While the window is being resized, resize the display too (off: once the drag ends).</summary>
    public bool ResizeWhileDragging { get; set; }

    /// <summary>It is never made narrower than this, in DIPs, while beside the phone.</summary>
    public int MinWidth { get; set; } = 360;

    public SecondScreenSettings Copy() => this with { };

    public void Normalize()
    {
        Placement = MirrorSettings.OneOf(PlacementChoices, Placement, "beside");
        Side = MirrorSettings.OneOf(SideChoices, Side, "right");
        Size = MirrorSettings.OneOf(SizeChoices, Size, "follow");
        Keyboard = MirrorSettings.OneOf(KeyboardChoices, Keyboard, "here");
        CustomWidth = Math.Clamp(CustomWidth, SmallestSide, LargestSide);
        CustomHeight = Math.Clamp(CustomHeight, SmallestSide, LargestSide);
        Dpi = Dpi <= 0 ? 0 : Math.Clamp(Dpi, SmallestDpi, LargestDpi);
        MaxSize = MaxSizeChoices.Contains(MaxSize) ? MaxSize : 0;
        MinWidth = Math.Clamp(MinWidth, NarrowestView, WidestMinimum);
    }
}

/// <summary>How several views share the mirror area, and what marks them.</summary>
public sealed record ViewsSettings
{
    public static readonly IReadOnlyList<string> ArrangementChoices = ["auto", "side", "stack"];
    public static readonly IReadOnlyList<string> MarkChoices = ["auto", "always", "never"];

    /// <summary>auto (whatever fits best), side by side, or one above the other (stack).</summary>
    public string Arrangement { get; set; } = "auto";

    /// <summary>An outline on the view that has the keyboard: auto (when there is more than one), always, never.</summary>
    public string Outline { get; set; } = "auto";

    /// <summary>A name on each view: auto (when they show different things), always, never.</summary>
    public string Captions { get; set; } = "auto";

    /// <summary>A splitter between the phone and the second screen.</summary>
    public bool Splitter { get; set; } = true;

    public ViewsSettings Copy() => this with { };

    public void Normalize()
    {
        Arrangement = MirrorSettings.OneOf(ArrangementChoices, Arrangement, "auto");
        Outline = MirrorSettings.OneOf(MarkChoices, Outline, "auto");
        Captions = MirrorSettings.OneOf(MarkChoices, Captions, "auto");
    }

    /// <summary>Whether a mark set to auto, always or never shows, given whether there is reason for it.</summary>
    public static bool Shows(string mark, bool wanted) => mark == "always" || mark == "auto" && wanted;
}
