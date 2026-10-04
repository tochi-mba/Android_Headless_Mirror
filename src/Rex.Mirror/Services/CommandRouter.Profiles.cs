using System.IO;
using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>Profiles over the pipe: rex profile while the app runs, and what the status says about them.</summary>
public static partial class CommandRouter
{
    private static IpcResponse Profile(AppHost host, IpcRequest request)
    {
        try
        {
            var result = host.Profiles.Run(ProfileRequest.FromFields(request.Args));
            return IpcResponse.Success(new JsonObject { ["words"] = result.Words, ["data"] = result.Data });
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException or FormatException or IOException or UnauthorizedAccessException)
        {
            return IpcResponse.Fail(ex.Message);
        }
    }

    private static JsonObject ProfilesStatus(AppHost host)
    {
        var profiles = host.Profiles;
        var automatic = profiles.Automatic;
        return new JsonObject
        {
            ["current"] = automatic ?? (profiles.Manual.Length > 0 ? profiles.Manual : null),
            ["manual"] = profiles.Manual,
            ["automatic"] = automatic,
            ["onBattery"] = profiles.OnBattery,
            ["list"] = new JsonArray([.. profiles.Store.List().Select(e => JsonValue.Create(e.Name))]),
            ["tray"] = host.Tray is { } tray ? new JsonArray([.. tray.ProfileItems.Select(item => JsonValue.Create(item))]) : null,
        };
    }
}
