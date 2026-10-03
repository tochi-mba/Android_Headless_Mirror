using Rex.Core;

namespace Rex.Cli;

/// <summary>
/// One line of the command reference: a usage, what it does and an example. <see cref="Name"/> is
/// the command word both modes dispatch on.
/// </summary>
public sealed record CliCommand(
    string Name,
    string Usage,
    string Summary,
    string Example,
    bool Human,
    bool Machine,
    bool NeedsApp);

/// <summary>
/// Every command rex.exe accepts, once. <c>rex help</c>, machine mode's <c>capabilities</c> and the
/// site's command line page are all read from this list, so none of them can disagree with the others.
/// </summary>
public static class CliReference
{
    public static readonly IReadOnlyList<CliCommand> All =
    [
        new("open", "rex open", "Open the app, or bring it to the front.", "rex open",
            Human: true, Machine: true, NeedsApp: false),
        new("status", "rex status", "App, mirror and phone state.", "rex status",
            Human: true, Machine: true, NeedsApp: false),
        new("devices", "rex devices", "Phones visible to ADB.", "rex devices",
            Human: true, Machine: true, NeedsApp: false),
        new("stop", "rex stop", "Stop the mirror. The app stays in the tray.", "rex stop",
            Human: true, Machine: true, NeedsApp: true),
        new("quit", "rex quit", "Exit the app completely.", "rex quit",
            Human: true, Machine: true, NeedsApp: true),
        new("action", "rex action <name> [--serial S]", "Send an action: home, back, sleep, volume, rotation and the rest. rex action list shows the names.", "rex action sleep",
            Human: true, Machine: true, NeedsApp: false),
        new("zoom", "rex zoom <in|out|reset>", "Zoom the PC view of the open mirror.", "rex zoom in",
            Human: true, Machine: true, NeedsApp: true),
        new("sound", SoundCommand.Usage, "The phone's sound on this PC: a level, louder, quieter or muted. Without a value, what it is now.", "rex sound 40",
            Human: true, Machine: true, NeedsApp: true),
        new("app", AppCommands.ListUsage, "The phone's apps by name. A search finds the phone's own apps too; --system lists them all.", "rex app list spot",
            Human: true, Machine: true, NeedsApp: false),
        new("app", AppCommands.OpenUsage, "Open an app by its name or package. --fresh closes it first.", "rex app open Spotify",
            Human: true, Machine: true, NeedsApp: false),
        new("app", AppCommands.ChoreUsage, "Close an app completely, or open its info page on the phone.", "rex app close com.spotify.music",
            Human: true, Machine: true, NeedsApp: false),
        new("app", AppCommands.FavouriteUsage, "Star an app, or take its star away. Favourites have tiles and keys in the app.", "rex app favourite com.spotify.music on",
            Human: true, Machine: true, NeedsApp: false),
        new("push", FileCommands.PushUsage, "Copy files or folders into the phone's shared storage.", "rex push photo.jpg --to /sdcard/Pictures/",
            Human: true, Machine: true, NeedsApp: false),
        new("install", FileCommands.InstallUsage, "Install one or more APK files, with optional update flags.", "rex install app.apk --grant",
            Human: true, Machine: true, NeedsApp: false),
        new("screen", ScreenCommands.Usage, "A second screen for one app, beside the phone or instead of it: open it, switch its app, close it, or see what it is doing.", "rex screen open YouTube --size 1080p",
            Human: true, Machine: true, NeedsApp: true),
        new("screenshot", "rex screenshot [--serial S]", "Save a picture of the phone screen.", "rex screenshot",
            Human: true, Machine: true, NeedsApp: false),
        new("phone", "rex phone list|get [filter]", "Every phone setting this phone offers, with its value.", "rex phone list bright",
            Human: true, Machine: true, NeedsApp: false),
        new("phone", "rex phone set <setting> <value>", "Change one phone setting (rex phone list shows the ids).", "rex phone set brightness 180",
            Human: true, Machine: true, NeedsApp: false),
        new("phone", "rex phone reset <setting>", "Let Android use its own default for a phone setting again.", "rex phone reset font_scale",
            Human: true, Machine: true, NeedsApp: false),
        new("android", "rex android list|get|set|delete <system|secure|global> [key] [value] [--filter text]", "Raw Android settings. Keys that could cut off ADB are refused.", "rex android list global --filter animation",
            Human: true, Machine: true, NeedsApp: false),
        new("config", "rex config list|get|set|restore [path] [value]", "The app's own settings in config.json. set keeps a backup; restore swaps it back.", "rex config set Mirror.MaxFps 90",
            Human: true, Machine: true, NeedsApp: false),
        new("autostart", "rex autostart <on|off>", "Start with Windows, or not.", "rex autostart on",
            Human: true, Machine: true, NeedsApp: false),
        new("lock-mode", "rex lock-mode <serial> <pattern|other|none>", "Say what kind of lock screen a phone has.", "rex lock-mode R3CR... pattern",
            Human: true, Machine: true, NeedsApp: false),
        new("reset-lock", "rex reset-lock [serial|ALL]", "Forget the lock-screen answers for one phone, or all.", "rex reset-lock ALL",
            Human: true, Machine: true, NeedsApp: false),
        new("setup", "rex setup", "Download and check the official scrcpy without opening the app.", "rex setup",
            Human: true, Machine: false, NeedsApp: false),
        new("usb", "rex usb [list|status]", "ADB interfaces, and USB devices Windows could not read.", "rex usb",
            Human: true, Machine: true, NeedsApp: false),
        new("usb", "rex usb repair [--dry-run]", "Repair both. Asks for administrator approval; --dry-run only says what it would do.", "rex usb repair --dry-run",
            Human: true, Machine: true, NeedsApp: false),
        new("usb", "rex usb enable-auto-repair|disable-auto-repair [--dry-run]", "Fix \"USB device not recognised\" without asking each time, or stop doing so. Asks once.", "rex usb enable-auto-repair",
            Human: true, Machine: true, NeedsApp: false),
        new("usb", "rex usb run-auto-repair", "Start the installed auto-repair now. Needs no approval.", "rex usb run-auto-repair",
            Human: true, Machine: true, NeedsApp: false),
        new("diagnostics", "rex diagnostics", "A full report: versions, files, startup, tools, phones and the recent log.", "rex diagnostics",
            Human: true, Machine: true, NeedsApp: false),
        new("capabilities", "rex agent capabilities", "The machine-readable contract: commands, actions, settings and paths.", "rex agent capabilities",
            Human: false, Machine: true, NeedsApp: false),
        new("help", "rex help", "This list.", "rex help",
            Human: true, Machine: false, NeedsApp: false),
    ];

    /// <summary>The command words machine mode accepts, in reference order.</summary>
    public static IReadOnlyList<string> MachineCommands { get; } =
        All.Where(c => c.Machine).Select(c => c.Name).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The command words the human command line accepts, in reference order.</summary>
    public static IReadOnlyList<string> HumanCommands { get; } =
        All.Where(c => c.Human).Select(c => c.Name).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>What <c>rex help</c> prints.</summary>
    public static string HelpText()
    {
        var rows = All.Where(c => c.Human).ToArray();
        var lines = new List<string> { "REX · Android Headless Mirror command line", string.Empty };
        foreach (var command in rows)
        {
            lines.Add(command.Usage.Length > 45
                ? $"  {command.Usage}{Environment.NewLine}  {new string(' ', 48)}{command.Summary}"
                : $"  {command.Usage.PadRight(48)}{command.Summary}");
        }

        lines.Add(string.Empty);
        lines.Add("Machine mode (one JSON document, never prompts): rex agent <command>, rex --json <command>");
        lines.Add("Every command, with examples: " + Rex.Core.SiteLinks.CommandLine);
        return string.Join(Environment.NewLine, lines);
    }
}
