using System.Text.Json.Serialization;

namespace Rex.Core;

/// <summary>One action played by a key from anywhere.</summary>
public sealed record GlobalKeyAction
{
    /// <summary>The chord, written as <see cref="KeyChord"/> writes it.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>A <see cref="MirrorActions"/> id.</summary>
    public string Action { get; set; } = string.Empty;
}

/// <summary>
/// Keys that work while the window is hidden or behind other windows: one that shows and hides the
/// window, and any number of actions. They are matched in the app's keyboard hook and taken from
/// every other app while this one runs, so <see cref="GlobalKeyRules"/> decides which are allowed.
/// </summary>
public sealed record GlobalKeysSettings
{
    public const string DefaultShowHide = "Ctrl+Alt+M";
    public const int MostActions = 20;
    public static readonly string[] BehindChoices = ["front", "hide"];
    public static readonly string[] InFrontChoices = ["hide", "nothing"];
    public static readonly string[] HideToChoices = ["tray", "taskbar"];

    /// <summary>Every key below works from anywhere; off, they all pass to the app in front.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The key that shows and hides the window; empty turns it off.</summary>
    public string ShowHide { get; set; } = DefaultShowHide;

    /// <summary>The show-or-hide key with the window open but behind others: "front" or "hide".</summary>
    public string WhenBehind { get; set; } = "front";

    /// <summary>The show-or-hide key with the window in front: "hide" or "nothing".</summary>
    public string WhenInFront { get; set; } = "hide";

    /// <summary>Where the key hides the window: "tray" or "taskbar" (minimised).</summary>
    public string HideTo { get; set; } = "tray";

    /// <summary>Show the window filling the display.</summary>
    public bool ShowFullscreen { get; set; }

    /// <summary>Once shown, the phone has the keyboard, so typing goes straight to it.</summary>
    public bool TypeIntoPhone { get; set; } = true;

    /// <summary>Say from the tray what a key did while the window was hidden.</summary>
    public bool Announce { get; set; }

    /// <summary>Actions with keys of their own, up to <see cref="MostActions"/>.</summary>
    public List<GlobalKeyAction> Actions { get; set; } = [];

    /// <summary>The show-or-hide chord, or null when it is off.</summary>
    [JsonIgnore]
    public KeyChord? ShowHideChord => ShowHide.Length == 0 ? null : KeyChord.Parse(ShowHide);

    /// <summary>Every key from anywhere after <see cref="Normalize"/>, with the id it plays.</summary>
    public IEnumerable<(KeyChord Chord, string Id)> Keys()
    {
        if (ShowHideChord is { } showHide)
        {
            yield return (showHide, GlobalKeyRules.ShowHide);
        }

        foreach (var action in Actions)
        {
            yield return (KeyChord.Parse(action.Key), action.Action);
        }
    }

    public GlobalKeysSettings Copy() => this with { Actions = [.. Actions.Select(a => a with { })] };

    /// <summary>
    /// Keeps every key one the rules allow, written the one way: an unreadable or refused
    /// show-or-hide key goes back to the default, and actions that are unknown, refused or a second
    /// use of a key (the first one wins) are dropped.
    /// </summary>
    public void Normalize()
    {
        ShowHide = ShowHide?.Trim() ?? DefaultShowHide;
        if (ShowHide.Length > 0)
        {
            ShowHide = GlobalKeyRules.WhyNot(ShowHide, GlobalKeyRules.ShowHide, []) is null
                ? KeyChord.Parse(ShowHide).ToString()
                : DefaultShowHide;
        }

        WhenBehind = MirrorSettings.OneOf(BehindChoices, WhenBehind, "front");
        WhenInFront = MirrorSettings.OneOf(InFrontChoices, WhenInFront, "hide");
        HideTo = MirrorSettings.OneOf(HideToChoices, HideTo, "tray");

        var taken = ShowHideChord is { } chord ? new List<KeyChord> { chord } : [];
        var kept = new List<GlobalKeyAction>();
        foreach (var entry in (Actions ?? []).OfType<GlobalKeyAction>())
        {
            if (kept.Count == MostActions ||
                MirrorActions.Find(entry.Action ?? string.Empty) is not { } action ||
                GlobalKeyRules.WhyNot(entry.Key, action.Id, taken) is not null)
            {
                continue;
            }

            var key = KeyChord.Parse(entry.Key);
            taken.Add(key);
            kept.Add(new GlobalKeyAction { Key = key.ToString(), Action = action.Id });
        }

        Actions = kept;
    }
}
