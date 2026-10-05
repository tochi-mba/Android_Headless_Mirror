namespace Rex.Core;

/// <summary>Window and background behaviour of the desktop app.</summary>
public sealed record AppSettings
{
    /// <summary>Keep running in the tray when the window is closed so the next phone auto-opens.</summary>
    public bool RunInBackground { get; set; } = true;

    /// <summary>Bring the window up automatically when an authorised phone connects.</summary>
    public bool OpenOnConnect { get; set; } = true;

    /// <summary>Ask before writing sensitive or advanced Android settings.</summary>
    public bool ConfirmSensitiveWrites { get; set; } = true;

    public string ScreenshotDirectory { get; set; } = "captures/screenshots";

    /// <summary>
    /// When Windows cannot read the phone over USB ("USB device not recognised"), start the
    /// auto-repair task without asking. The task itself is set up once, with administrator
    /// approval; with this off the app still says what is wrong and offers the repair.
    /// </summary>
    public bool AutoRepairUsb { get; set; } = true;

    public static readonly string[] SidebarSides = ["right", "left"];

    /// <summary>The phone buttons the top bar can show, in the order it shows them.</summary>
    public static readonly string[] QuickButtons = ["home", "back", "recents", "sleep", "screenshot", "sound"];

    public static readonly string[] ScreenshotFormats = ["png", "jpg"];

    /// <summary>Keep the window above every other window.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>Which side of the mirror the side panel sits on: "right" or "left".</summary>
    public string SidebarSide { get; set; } = "right";

    /// <summary>The phone buttons in the top bar (see <see cref="QuickButtons"/>); any may be left out.</summary>
    public List<string> TopBarButtons { get; set; } = [.. QuickButtons];

    /// <summary>The shortcut hints at the right of the status bar.</summary>
    public bool ShowHints { get; set; } = true;

    /// <summary>A notification when a phone connects or goes away while the window is out of sight.</summary>
    public bool NotifyConnections { get; set; }

    /// <summary>How many frames a second the mirror is showing, live in the status bar (scrcpy --print-fps).</summary>
    public bool ShowFrameRate { get; set; }

    /// <summary>Screenshots as PNG (exactly what the phone showed) or JPG (much smaller).</summary>
    public string ScreenshotFormat { get; set; } = "png";

    /// <summary>Put each new screenshot on the clipboard as well, ready to paste.</summary>
    public bool CopyScreenshots { get; set; }

    /// <summary>After an update, say once in the bar above the mirror which version this is.</summary>
    public bool ShowWhatsNew { get; set; } = true;

    /// <summary>The bar above the mirror with the phone's name and its buttons.</summary>
    public bool ShowTopBar { get; set; } = true;

    /// <summary>The bar under the mirror with what the app is doing and the shortcut hints.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>How large the side panel's text and controls are, from 0.8 to 1.5 times their usual size.</summary>
    public double PanelScale { get; set; } = 1.0;

    /// <summary>Open the window where it was and at the size it had; off opens it centred at its usual size.</summary>
    public bool RememberPlacement { get; set; } = true;

    /// <summary>Size the window so the phone fills its area, once the first picture of each start arrives.</summary>
    public bool FitWindowOnStart { get; set; }

    /// <summary>Ask before quitting while a phone is mirrored.</summary>
    public bool ConfirmQuit { get; set; }

    /// <summary>A word from the tray when the mirror stops by itself while the window is out of sight.</summary>
    public bool NotifyMirrorStops { get; set; }

    /// <summary>How screenshots and recordings are named, with the tokens of <see cref="CaptureName"/>.</summary>
    public string CaptureNames { get; set; } = CaptureName.Default;

    /// <summary>A short flash over the view as a screenshot is taken.</summary>
    public bool ScreenshotFlash { get; set; }

    /// <summary>Open each screenshot in its viewer once it is saved.</summary>
    public bool OpenScreenshots { get; set; }

    public const double SmallestPanelScale = 0.8;
    public const double LargestPanelScale = 1.5;

    public AppSettings Copy() => this with { TopBarButtons = [.. TopBarButtons] };

    public void Normalize()
    {
        ScreenshotDirectory = !string.IsNullOrWhiteSpace(ScreenshotDirectory) &&
            (Path.IsPathFullyQualified(ScreenshotDirectory) || PathRules.IsSafeRelativePath(ScreenshotDirectory))
            ? ScreenshotDirectory.Trim() : "captures/screenshots";
        SidebarSide = MirrorSettings.OneOf(SidebarSides, SidebarSide, "right");
        ScreenshotFormat = MirrorSettings.OneOf(ScreenshotFormats, ScreenshotFormat, "png");
        PanelScale = double.IsFinite(PanelScale) ? Math.Round(Math.Clamp(PanelScale, SmallestPanelScale, LargestPanelScale), 2) : 1.0;
        CaptureNames = CaptureName.WhyNot(CaptureNames) is null ? CaptureNames.Trim() : CaptureName.Default;

        // Known buttons only, each once, in the top bar's own order.
        var wanted = (TopBarButtons ?? []).Select(id => (id ?? string.Empty).Trim().ToLowerInvariant()).ToHashSet();
        TopBarButtons = QuickButtons.Where(wanted.Contains).ToList();
    }
}
