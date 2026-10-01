using System.Globalization;
using System.Text;
using Rex.Core;

namespace Rex.Tests.Site;

/// <summary>Writes the middle of docs/shortcuts.html from <see cref="Shortcuts.All"/>.</summary>
internal static class ShortcutsPageWriter
{
    public const string Key = "shortcuts";

    private static readonly (string Id, string Title, string Intro, Func<Shortcut, bool> Belongs)[] Sections =
    [
        ("keys-window", "In the window",
            "They work while the app's window is in front, whatever has the keyboard. Most are Ctrl+Alt with a key; AltGr is never taken, so it still types on the phone.",
            s => s.IsKey && !s.Browse && !s.Global),
        ("keys-anywhere", "From anywhere",
            "They work while the window is hidden in the tray or behind other windows, as long as the app runs. The key is yours to choose in Settings, Shortcuts from anywhere, where any action can have a key of its own too.",
            s => s.Global),
        ("keys-browse", "Browse mode",
            "Press Ctrl+Alt+K first. While browse mode is on, these plain keys act on the phone instead of typing into it; letters still type, so a search box works. Esc goes back to typing.",
            s => s.Browse),
        ("keys-gestures", "Mouse and touchpad",
            "Two fingers on a precision touchpad are two real fingers on the phone. Hold Alt and the same gestures move the view on this PC instead, and the phone receives nothing.",
            s => !s.IsKey),
    ];

    public static string Write()
    {
        var html = new StringBuilder();
        html.Append("<div class=\"filter\">\n");
        html.Append("  <label for=\"shortcuts-filter\">Find a shortcut</label>\n");
        html.Append("  <input id=\"shortcuts-filter\" class=\"filter-input\" type=\"search\" autocomplete=\"off\" spellcheck=\"false\" placeholder=\"A key, or what you want to do\" data-filter=\".key-row\" data-filter-groups=\".ref-group\" data-filter-count=\"shortcuts-filter-count\" data-filter-noun=\"shortcut\">\n");
        html.Append("  <p class=\"filter-count\" id=\"shortcuts-filter-count\" role=\"status\" aria-live=\"polite\"></p>\n");
        html.Append("</div>\n");

        foreach (var (id, title, intro, belongs) in Sections)
        {
            html.Append(CultureInfo.InvariantCulture, $"<section class=\"ref-group\" id=\"{id}\" aria-labelledby=\"{id}-title\">\n");
            html.Append(CultureInfo.InvariantCulture, $"  <h2 id=\"{id}-title\">{title}</h2>\n");
            html.Append(CultureInfo.InvariantCulture, $"  <p class=\"ref-intro\">{SiteHtml.Escape(intro)}</p>\n");
            html.Append("  <table class=\"key-table\">\n");
            html.Append("    <thead><tr><th scope=\"col\">Keys</th><th scope=\"col\">What happens</th></tr></thead>\n");
            html.Append("    <tbody>\n");
            foreach (var shortcut in Shortcuts.All.Where(belongs))
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"      <tr class=\"key-row\" id=\"key-{shortcut.Id}\"><td><kbd>{SiteHtml.Escape(shortcut.Gesture)}</kbd></td><td>{SiteHtml.Escape(shortcut.Description)}</td></tr>\n");
            }

            html.Append("    </tbody>\n  </table>\n</section>\n");
        }

        return html.ToString();
    }
}
