using System.Text.Json;

namespace Rex.Core;

/// <summary>
/// Values <c>rex config set</c> refuses outright, with the reason, rather than letting
/// <see cref="RexConfig.Normalize"/> quietly put something else in their place: a person who typed
/// a value should hear why it cannot be used.
/// </summary>
public static class ConfigValidation
{
    /// <summary>Throws <see cref="FormatException"/> with the reason when <paramref name="raw"/> cannot be the value at <paramref name="path"/>.</summary>
    /// <param name="path">The setting's path with the names as config.json spells them.</param>
    /// <param name="current">The settings as they are, for values checked against each other.</param>
    public static void Check(string path, string raw, RexConfig current)
    {
        switch (path)
        {
            case "Mirror.ExtraArgs":
                _ = ScrcpyArguments.SplitExtraArgs(raw);
                break;
            case "GlobalKeys.ShowHide" when raw.Trim().Length > 0:
                Refuse(GlobalKeyRules.WhyNot(raw, GlobalKeyRules.ShowHide, Taken(current, replacingShowHide: true)));
                break;
            case "GlobalKeys.Actions":
                CheckActions(raw, current);
                break;
            case "Transfer.Folder":
                Refuse(TransferSettings.WhyNotFolder(raw));
                break;
            case "Keys.Window":
            case "Keys.Browse":
                CheckKeys(raw, current, browse: path == "Keys.Browse");
                break;
            case "App.CaptureNames":
                Refuse(CaptureName.WhyNot(raw));
                break;
            case "Mirror.Crop":
                Refuse(MirrorSettings.WhyNotCrop(raw));
                break;
            case "Mirror.VideoEncoder":
                Refuse(raw.Trim().Length > 0 && !MirrorSettings.IsValidEncoderName(raw)
                    ? "An encoder's name is letters, digits, dots, dashes and underscores. Run 'rex encoders' to see the phone's."
                    : null);
                break;
            case "Mirror.StartOrientation":
                Refuse(DisplayOrientation.Parse(raw.Trim()) is null ? "Use one of " + string.Join(", ", DisplayOrientation.Names) + "." : null);
                break;
            case "Mirror.CaptureOrientation":
                Refuse(MirrorSettings.CaptureOrientations.Contains(raw.Trim()) ? null : "Use one of @, @0, @90, @180 or @270, or nothing to follow the phone.");
                break;
        }
    }

    private static void CheckActions(string raw, RexConfig current)
    {
        var actions = Read(raw);
        if (actions.Count > GlobalKeysSettings.MostActions)
        {
            throw new FormatException($"At most {GlobalKeysSettings.MostActions} actions can have keys from anywhere.");
        }

        var taken = Taken(current, replacingShowHide: false).ToList();
        foreach (var entry in actions)
        {
            var action = MirrorActions.Find(entry.Action ?? string.Empty)
                ?? throw new FormatException($"'{entry.Action}' is not an action. Run 'rex action list' to see them.");
            Refuse(GlobalKeyRules.WhyNot(entry.Key, action.Id, taken) is { } why ? $"{entry.Key} for {action.Label}: {why}" : null);
            taken.Add(KeyChord.Parse(entry.Key));
        }
    }

    /// <summary>A list of keys of one's own: each one checked as <c>rex keys set</c> would, against the ones before it.</summary>
    private static void CheckKeys(string raw, RexConfig current, bool browse)
    {
        const string Example = "Expected a list such as [{\"Action\":\"home\",\"Key\":\"Ctrl+Alt+J\"}].";
        List<KeyBinding> bindings;
        try
        {
            bindings = JsonSerializer.Deserialize(raw, RexJsonContext.Default.ListKeyBinding) is { } list && list.All(b => b is not null)
                ? list
                : throw new FormatException(Example);
        }
        catch (JsonException ex)
        {
            throw new FormatException(Example + " " + ex.Message, ex);
        }

        if (bindings.Count > KeysSettings.MostBindings)
        {
            throw new FormatException($"At most {KeysSettings.MostBindings} keys of your own.");
        }

        var map = new KeyMap(new KeysSettings());
        foreach (var binding in bindings)
        {
            var why = browse ? map.WhyNotBrowse(binding.Action, binding.Key)
                : map.WhyNotWindow(binding.Action, binding.Key) ?? KeyMap.WhyNotFromAnywhere(current.GlobalKeys, binding.Key);
            Refuse(why is null ? null : $"{binding.Key} for {binding.Action}: {why}");
            if (browse)
            {
                map.BindBrowse(binding.Action, binding.Key);
            }
            else
            {
                map.BindWindow(binding.Action, binding.Key);
            }
        }
    }

    private static List<GlobalKeyAction> Read(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize(raw, RexJsonContext.Default.ListGlobalKeyAction) is { } list && list.All(a => a is not null)
                ? list
                : throw new FormatException("Expected a list such as [{\"Key\":\"Ctrl+Shift+F9\",\"Action\":\"screenshot\"}].");
        }
        catch (JsonException ex)
        {
            throw new FormatException("Expected a list such as [{\"Key\":\"Ctrl+Shift+F9\",\"Action\":\"screenshot\"}]. " + ex.Message, ex);
        }
    }

    /// <summary>The keys from anywhere already in use, leaving out the ones being replaced.</summary>
    private static IEnumerable<KeyChord> Taken(RexConfig current, bool replacingShowHide)
    {
        var keys = current.GlobalKeys.Copy();
        if (replacingShowHide)
        {
            keys.ShowHide = string.Empty;
        }
        else
        {
            keys.Actions = [];
        }

        return keys.Keys().Select(k => k.Chord);
    }

    private static void Refuse(string? reason)
    {
        if (reason is not null)
        {
            throw new FormatException(reason);
        }
    }
}
