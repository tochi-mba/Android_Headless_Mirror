using System.Text.Json.Serialization;

namespace Rex.Core;

/// <summary>
/// A second, different phone beside the first: whether one is shown, what happens when one
/// connects, where it goes, whose sound plays, and how its own session differs from the main one.
/// </summary>
public sealed record SecondPhoneSettings
{
    public static readonly IReadOnlyList<string> WhenConnectedChoices = ["ask", "always", "never"];
    public static readonly IReadOnlyList<string> SideChoices = ["right", "left"];
    public static readonly IReadOnlyList<string> SoundChoices = ["main", "active", "both"];
    public static readonly IReadOnlyList<string> ScreenOffChoices = ["same", "off", "on"];

    /// <summary>A second phone may be shown beside the first at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When another phone connects: ask in the notice bar, always show it, or never.</summary>
    public string WhenConnected { get; set; } = "ask";

    /// <summary>The phone shown beside last time comes back by itself when it connects again.</summary>
    public bool Remember { get; set; } = true;

    /// <summary>Which side of the main phone it goes on (the top or bottom when they are stacked).</summary>
    public string Side { get; set; } = "right";

    /// <summary>
    /// Whose sound plays on this PC: the main phone's, the phone you are using (the other is muted
    /// here), or both. The other phone's session carries sound only when it can be heard.
    /// </summary>
    public string Sound { get; set; } = "main";

    /// <summary>Its own screen while shown: like the main phone's setting, off, or on.</summary>
    public string ScreenOff { get; set; } = "same";

    /// <summary>Its video's longest side, or 0 for the main phone's.</summary>
    public int MaxSize { get; set; }

    /// <summary>Its video bit rate, or empty for the main phone's.</summary>
    public string BitRate { get; set; } = string.Empty;

    /// <summary>It stops while the window is hidden and starts again when it shows.</summary>
    public bool PauseWhenHidden { get; set; }

    /// <summary>Its own profile applies while you use it, and is put back when you go back to the main phone.</summary>
    public bool FollowsProfile { get; set; }

    public SecondPhoneSettings Copy() => this with { };

    public void Normalize()
    {
        WhenConnected = MirrorSettings.OneOf(WhenConnectedChoices, WhenConnected, "ask");
        Side = MirrorSettings.OneOf(SideChoices, Side, "right");
        Sound = MirrorSettings.OneOf(SoundChoices, Sound, "main");
        ScreenOff = MirrorSettings.OneOf(ScreenOffChoices, ScreenOff, "same");
        MaxSize = SecondScreenSettings.MaxSizeChoices.Contains(MaxSize) ? MaxSize : 0;
        BitRate = ScrcpyArguments.IsValidBitRate(BitRate) ? BitRate.Trim().ToUpperInvariant() : string.Empty;
    }

    /// <summary>Whether the other phone's session should carry sound for this PC.</summary>
    [JsonIgnore]
    public bool OtherHasSound => Sound != "main";

    /// <summary>Whether the other phone's own screen goes off while shown, given the main phone's setting.</summary>
    public bool TurnsScreenOff(bool mainTurnsOff) => ScreenOff switch
    {
        "off" => true,
        "on" => false,
        _ => mainTurnsOff,
    };
}
