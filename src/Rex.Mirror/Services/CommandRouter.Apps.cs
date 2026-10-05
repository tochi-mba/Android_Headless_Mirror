using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>The phone's apps over the pipe: the list as the app knows it, and opening one.</summary>
public static partial class CommandRouter
{
    /// <summary>
    /// The shown phone's apps, every one with its flags, narrowed by <c>search</c> when given.
    /// <c>read=true</c> reads the list from the phone first; <c>favourite=package</c> with <c>on</c>
    /// stars it or takes its star away.
    /// </summary>
    private static async Task<IpcResponse> AppsAsync(AppHost host, IpcRequest request)
    {
        if (host.Window?.AppsSerial is not { } serial)
        {
            return IpcResponse.Fail("Connect a phone first.");
        }

        // Asked about another phone, the app does nothing and says which phone it shows.
        if (request.Arg("serial") is { Length: > 0 } asked && asked != serial)
        {
            return IpcResponse.Success(new JsonObject { ["shown"] = serial });
        }

        if (request.Arg("read") == "true" && await host.Session.ReadAppsAsync(serial).ConfigureAwait(true) is { Ok: false } failed)
        {
            return IpcResponse.Fail("Could not read the phone's apps: " + failed.Error);
        }

        // Starring from the command line goes through here while the app runs, so the window shows it at once.
        if (request.Arg("favourite") is { Length: > 0 } starred)
        {
            if (!PackageName.IsValid(starred))
            {
                return IpcResponse.Fail($"\"{starred}\" is not an app's package name.");
            }

            host.State.SetFavourite(serial, starred, request.Arg("on") != "false");
            host.Window?.AppsEdited();
        }

        var profile = host.State.GetDevice(serial);
        var favourites = (profile?.FavouriteApps ?? []).ToHashSet(StringComparer.Ordinal);
        var hidden = host.Config.Apps.Hidden.ToHashSet(StringComparer.Ordinal);
        var apps = AppList.Search(AppOrder.Sort(profile?.Apps ?? [], profile?.RecentApps ?? [], "name"), request.Arg("search"));
        var status = AppsStatus(host, host.Window);
        status["apps"] = new JsonArray([.. apps.Select(a => (JsonNode)new JsonObject
        {
            ["name"] = a.Name,
            ["package"] = a.Package,
            ["system"] = a.System,
            ["favourite"] = favourites.Contains(a.Package),
            ["hidden"] = hidden.Contains(a.Package),
        })]);
        return IpcResponse.Success(status);
    }

    /// <summary>
    /// A phone's video encoders (<c>serial</c>, or the main phone's), read through the app so the
    /// read takes its turn with the mirror and its copies starting.
    /// </summary>
    private static async Task<IpcResponse> EncodersAsync(AppHost host, IpcRequest request)
    {
        var serial = request.Arg("serial") is { Length: > 0 } asked ? asked : host.Session.ActiveDevice?.Serial;
        if (serial is null)
        {
            return IpcResponse.Fail("Connect a phone first.");
        }

        var read = await host.Session.ReadEncodersAsync(serial).ConfigureAwait(true);
        return read.Ok
            ? IpcResponse.Success(EncoderList.ToJson(serial, host.Config.Mirror, read.Encoders))
            : IpcResponse.Fail("Could not read the phone's encoders: " + read.Error);
    }

    /// <summary>Opens an app by its name or package (<c>app</c>), fresh when <c>fresh=true</c>.</summary>
    private static async Task<IpcResponse> OpenAppAsync(AppHost host, IpcRequest request)
    {
        if (host.Window is not { } window || window.AppsSerial is not { } serial)
        {
            return IpcResponse.Fail("Connect a phone first.");
        }

        var wanted = request.Arg("app");
        var known = host.State.GetDevice(serial)?.Apps ?? [];
        var match = AppList.Resolve(known, wanted);
        var app = match.App ?? (match.Candidates.Count == 0 && PackageName.IsValid(wanted) ? new PhoneApp(wanted, wanted, false) : null);
        if (app is null)
        {
            return IpcResponse.Fail(match.Error!);
        }

        var result = await window.OpenAppAsync(app, request.Arg("fresh") == "true").ConfigureAwait(true);
        return result.Ok
            ? IpcResponse.Success(new JsonObject { ["name"] = app.Name, ["package"] = app.Package })
            : IpcResponse.Fail(result.Text);
    }

    /// <summary>What the app knows of the shown phone's apps.</summary>
    private static JsonObject AppsStatus(AppHost host, MainWindow? window)
    {
        var serial = window?.AppsSerial;
        var profile = serial is null ? null : host.State.GetDevice(serial);
        return new JsonObject
        {
            ["serial"] = serial,
            ["count"] = profile?.Apps?.Count ?? 0,
            ["system"] = profile?.Apps?.Count(a => a.System) ?? 0,
            ["read"] = profile?.AppsReadUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["reading"] = host.Session.ReadingApps,
            ["error"] = host.Session.AppsError.Length == 0 ? null : host.Session.AppsError,
            ["favourites"] = new JsonArray([.. (profile?.FavouriteApps ?? []).Select(p => (JsonNode)p)]),
            ["recent"] = new JsonArray([.. (profile?.RecentApps ?? []).Select(r => (JsonNode)new JsonObject { ["package"] = r.Package, ["times"] = r.Times })]),
        };
    }
}
