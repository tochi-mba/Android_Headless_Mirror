namespace Rex.Core;

/// <summary>A one-time hint, shown the first time someone reaches the thing it explains.</summary>
public sealed record Tip(string Id, string Title, string Text);

/// <summary>
/// The hints the app offers once each, the first time they would help.
///
/// They appear in the notice bar the window already uses for one question at a time, rather than in
/// a second mechanism of their own, and each is remembered so nobody is told twice.
/// </summary>
public static class Tips
{
    public const string FirstZoom = "first-zoom";
    public const string FirstFullscreen = "first-fullscreen";
    public const string FirstRiskyWrite = "first-risky-write";
    public const string SecondPhone = "second-phone";

    public static readonly IReadOnlyList<Tip> All =
    [
        new(FirstZoom, "You are zoomed in",
            "The navigator in the corner shows which part of the phone you are looking at; drag it to move. Alt + drag pans, and the badge in the top bar goes back to a whole screen."),
        new(FirstFullscreen, "The controls are still there",
            "Move the pointer to the top edge and they come back. Drag them by the bar to keep them somewhere else, and press Esc to leave fullscreen."),
        new(FirstRiskyWrite, "About those risk labels",
            "Settings marked risky can change how the phone behaves until you set them back. Every one of them can be put back to the phone's own default with Default."),
        new(SecondPhone, "Two phones are connected",
            "The chip at the top now lists them. Pick the one you want to mirror and the app remembers it for next time."),
    ];

    public static Tip? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
