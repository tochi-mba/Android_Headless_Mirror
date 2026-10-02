using System.Globalization;

namespace Rex.Core;

public enum ScreenState
{
    Off,
    Opening,
    Showing,
    Failed,
}

public enum ScreenStep
{
    Nothing,
    Launch,
    Retry,
    GiveUp,
    Reopen,
    Close,
}

/// <summary>
/// What the second screen does next, as events come in: opening, showing, failing (one more try,
/// then giving up with the reason), closing, settings that need the display made again, and the
/// mirror stopping or starting. It decides; the controller does.
/// </summary>
public sealed class SecondScreenPlan
{
    /// <summary>Android 10 is the first that lets an app make a display of its own for another app.</summary>
    public const int FirstApiLevel = 29;

    public ScreenState State { get; private set; } = ScreenState.Off;

    /// <summary>The app on it, or the last one it had; kept after closing so it can be opened again.</summary>
    public string App { get; private set; } = string.Empty;

    /// <summary>Why it failed, while it has.</summary>
    public string? Why { get; private set; }

    /// <summary>The display it was opened with.</summary>
    public ScreenSpec? Spec { get; private set; }

    private bool _retried;

    /// <summary>Why a phone cannot have a second screen, in words, or null when it can.</summary>
    public static string? WhyNot(string apiLevel, string androidVersion) =>
        int.TryParse(apiLevel, NumberStyles.Integer, CultureInfo.InvariantCulture, out var api) && api < FirstApiLevel
            ? $"A second screen needs Android 10 or later; this phone has Android {(androidVersion.Length > 0 ? androidVersion : api.ToString(CultureInfo.InvariantCulture))}."
            : null;

    /// <summary>Opens it with an app (switching app on an open one is not this: that keeps the display).</summary>
    public ScreenStep Open(string app, ScreenSpec spec)
    {
        App = app;
        Spec = spec;
        Why = null;
        _retried = false;
        State = ScreenState.Opening;
        return ScreenStep.Launch;
    }

    /// <summary>The display is up and its picture is in the window.</summary>
    public void Opened()
    {
        State = ScreenState.Showing;
        _retried = false;
    }

    /// <summary>It could not open, or it went away by itself: one more try, then it gives up and says why.</summary>
    public ScreenStep Failed(string why)
    {
        if (State == ScreenState.Off)
        {
            return ScreenStep.Nothing;
        }

        if (!_retried)
        {
            _retried = true;
            State = ScreenState.Opening;
            return ScreenStep.Retry;
        }

        Why = why;
        State = ScreenState.Failed;
        return ScreenStep.GiveUp;
    }

    /// <summary>The person closed it, or the mirror stopped.</summary>
    public ScreenStep Close()
    {
        var was = State;
        State = ScreenState.Off;
        Why = null;
        return was is ScreenState.Opening or ScreenState.Showing ? ScreenStep.Close : ScreenStep.Nothing;
    }

    /// <summary>
    /// The settings changed what the display would be: an open one is made again with the same app.
    /// A display that follows the view changes size by itself, so a new size alone is not a change.
    /// </summary>
    public ScreenStep SettingsChanged(ScreenSpec wanted)
    {
        if (State is not (ScreenState.Showing or ScreenState.Opening) || Spec is not { } current)
        {
            return ScreenStep.Nothing;
        }

        var same = current.Follows && wanted.Follows
            ? current with { Width = wanted.Width, Height = wanted.Height, App = wanted.App, Fresh = wanted.Fresh } == wanted
            : current with { App = wanted.App, Fresh = wanted.Fresh } == wanted;
        if (same)
        {
            return ScreenStep.Nothing;
        }

        Spec = wanted;
        _retried = false;
        State = ScreenState.Opening;
        return ScreenStep.Reopen;
    }

    /// <summary>Whether the mirror starting should bring it back: asked for, and it has had an app.</summary>
    public bool ReopensWith(SecondScreenSettings settings) => settings.ReopenOnStart && App.Length > 0 && State == ScreenState.Off;

    /// <summary>A new app on the open display.</summary>
    public void Switched(string app) => App = app;
}
