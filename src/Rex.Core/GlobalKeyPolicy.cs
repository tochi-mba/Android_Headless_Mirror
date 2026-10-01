namespace Rex.Core;

/// <summary>How the window is when the show-or-hide key is pressed.</summary>
/// <param name="Visible">Shown, rather than hidden in the tray.</param>
/// <param name="Minimized">Minimised to the taskbar.</param>
/// <param name="InFront">The foreground window.</param>
/// <param name="Fullscreen">Filling the display.</param>
public sealed record WindowNow(bool Visible, bool Minimized, bool InFront, bool Fullscreen);

/// <summary>What the show-or-hide key does to the window.</summary>
public enum GlobalStep
{
    Show,
    ShowFullscreen,
    BringToFront,
    HideToTray,
    Minimise,
    Nothing,
}

/// <summary>What the show-or-hide key does, given the window and the person's choices.</summary>
public static class GlobalKeyPolicy
{
    public static GlobalStep ForShowHide(WindowNow now, GlobalKeysSettings settings)
    {
        if (!now.Visible || now.Minimized)
        {
            return settings.ShowFullscreen ? GlobalStep.ShowFullscreen : GlobalStep.Show;
        }

        if (!now.InFront)
        {
            return settings.WhenBehind == "hide" ? Hide(settings) : GlobalStep.BringToFront;
        }

        return settings.WhenInFront == "nothing" ? GlobalStep.Nothing : Hide(settings);
    }

    /// <summary>
    /// Whether fullscreen is left before the step: a window hidden in fullscreen would otherwise come
    /// back filling the display whatever "Open straight to fullscreen" says.
    /// </summary>
    public static bool LeavesFullscreenFirst(WindowNow now, GlobalStep step) =>
        now.Fullscreen && step is GlobalStep.HideToTray or GlobalStep.Minimise;

    private static GlobalStep Hide(GlobalKeysSettings settings) =>
        settings.HideTo == "taskbar" ? GlobalStep.Minimise : GlobalStep.HideToTray;
}
