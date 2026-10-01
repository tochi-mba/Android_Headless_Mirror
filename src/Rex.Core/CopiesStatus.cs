namespace Rex.Core;

/// <summary>
/// Where the copies of the phone stand, and the sentence the Controls tab shows about them. It is
/// kept apart from the window so every case reads right without a phone: waiting for the mirror,
/// opening one, all of them showing, some out of sight for want of room, and all of them waiting
/// while the phone is on its side.
/// </summary>
/// <param name="Mirroring">Whether the main picture is up.</param>
/// <param name="Wanted">Copies asked for, not counting the phone's own view.</param>
/// <param name="Running">Copies running and embedded.</param>
/// <param name="Starting">Whether a copy is opening right now.</param>
/// <param name="ShownViews">Views the last layout gave room to, the main one included.</param>
/// <param name="Upright">Whether the picture is taller than it is wide.</param>
/// <param name="WhyNoMore">Why another copy cannot be added now, or null when one can.</param>
/// <param name="Room">How many copies there is room to show; the rest wait, stopped.</param>
public sealed record CopiesStatus(bool Mirroring, int Wanted, int Running, bool Starting, int ShownViews, bool Upright, string? WhyNoMore, int Room = int.MaxValue)
{
    /// <summary>Copies out of sight: running without a cell for a moment, or waiting, stopped, for room.</summary>
    public int Hidden => Mirroring ? Math.Max(0, Running - Math.Max(0, ShownViews - 1)) + Waiting : 0;

    /// <summary>Copies wanted but stopped for want of room.</summary>
    public int Waiting => Mirroring && Room < Wanted ? Wanted - Math.Max(Room, 0) : 0;

    public bool CanAdd => Mirroring && !Starting && WhyNoMore is null;

    public bool CanRemove => Wanted > 0;

    public string Summary
    {
        get
        {
            if (!Mirroring)
            {
                return Wanted == 0
                    ? "Extra live views of the phone, side by side, once it is mirrored."
                    : $"{Count(Wanted)} will open again once the phone is mirrored.";
            }

            if (Starting)
            {
                return $"Opening copy {Math.Clamp(Running + 1, 1, Math.Max(1, Wanted))} of {Math.Max(1, Wanted)}…";
            }

            if (Wanted > 0 && !Upright)
            {
                return $"{Count(Wanted)} out of sight while the phone is on its side, paused so they cost nothing. {(Wanted == 1 ? "It comes" : "They come")} back when the phone is upright.";
            }

            if (Hidden > 0)
            {
                var showing = Math.Max(0, Wanted - Hidden);
                return $"{showing} of {Count(Wanted)} showing, {Hidden} paused for want of room. Make the window wider to see {(Hidden == 1 ? "it" : "them")}.";
            }

            var reason = WhyNoMore is { } why ? " " + why : string.Empty;
            if (Wanted == 0)
            {
                return "Extra live views of the phone, side by side. Each one takes touch and typing, and zoom applies to all of them." + reason;
            }

            if (Running < Wanted)
            {
                return $"{Running} of {Wanted} open. Trying the next one again shortly.";
            }

            return $"{Count(Running)} open. Touch or type on any of them; zoom applies to all." + reason;
        }
    }

    private static string Count(int copies) => copies == 1 ? "1 copy" : $"{copies} copies";
}
