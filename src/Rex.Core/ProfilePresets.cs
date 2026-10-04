using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>Profiles the app comes with, to apply as they are or to start your own from.</summary>
public static class ProfilePresets
{
    public static readonly IReadOnlyList<Profile> All =
    [
        Preset("Lowest latency", ("Mirror.MaxFps", 120), ("Mirror.VideoBitRate", "8M"), ("Mirror.VideoCodec", "h264"), ("Mirror.VideoBufferMs", 0),
            ("Mirror.AudioBufferMs", 20), ("Ambient.Enabled", false), ("Zoom.NavigatorPicture", false)),
        Preset("Best picture", ("Mirror.MaxSize", 0), ("Mirror.VideoBitRate", "24M"), ("Mirror.VideoCodec", "h265"), ("Mirror.VideoBufferMs", 50),
            ("Mirror.AudioBitRate", "256K")),
        Preset("Battery saver", ("Mirror.MaxSize", 1280), ("Mirror.MaxFps", 30), ("Mirror.VideoBitRate", "4M"), ("Session.StayAwake", false),
            ("Session.KeepActive", false), ("Ambient.Enabled", false), ("Copies.Most", 1)),
        Preset("Presentation", ("App.AlwaysOnTop", true), ("App.ShowHints", false), ("Hud.ShowMessages", false), ("Sound.MuteWhenHidden", true)),
        Preset("Gaming", ("Mirror.MaxFps", 120), ("Mirror.VideoBitRate", "16M"), ("Mirror.VideoBufferMs", 0), ("Input.Gamepad", "uhid"),
            ("Input.MouseHover", true), ("Sound.LowerWhileTyping", false)),
        Preset("Quiet", ("Sound.MuteWhenHidden", true), ("Sound.MuteWhenBehind", true), ("App.NotifyConnections", false),
            ("Transfer.NotifyWhenHidden", false)),
    ];

    public static Profile? Find(string name) => All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What a rule (in fullscreen, on battery) can switch to: none, the saved profiles, then the
    /// presets a saved one does not already go by. The value is the name; "" is none.
    /// </summary>
    public static IReadOnlyList<(string Value, string Label)> RuleChoices(IEnumerable<string> saved)
    {
        var names = saved.ToArray();
        return
        [
            (string.Empty, "None"),
            .. names.Select(name => (name, name)),
            .. All.Where(p => !names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).Select(p => (p.Name, p.Name + " (preset)")),
        ];
    }

    private static Profile Preset(string name, params (string Path, object Value)[] settings) =>
        new(name, settings.ToDictionary(s => s.Path, s => s.Value switch
        {
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            _ => (JsonNode)JsonValue.Create((string)s.Value),
        }, StringComparer.Ordinal), DateTimeOffset.UnixEpoch);
}
