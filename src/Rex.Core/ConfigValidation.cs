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
