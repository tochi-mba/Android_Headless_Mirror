using System.Text.Json.Nodes;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>The second screen over the pipe: open it with an app, switch its app, close it, or say what it is doing.</summary>
public static partial class CommandRouter
{
    /// <summary>
    /// <c>verb</c> open (with <c>app</c>, a name or package; <c>placement</c> and <c>size</c> change
    /// the settings first), app (switch the open one's app), close, or nothing for its state.
    /// </summary>
    private static async Task<IpcResponse> ScreenAsync(AppHost host, IpcRequest request)
    {
        if (host.Window is not { } window)
        {
            return IpcResponse.Fail("The window is not available.");
        }

        switch (request.Arg("verb"))
        {
            case "":
                return IpcResponse.Success(ScreenStatus(host, window));

            case "close":
                window.CloseSecondScreen();
                return IpcResponse.Success(ScreenStatus(host, window));

            case "open" or "app":
            {
                if (request.Arg("verb") == "app" && window.ScreenState != ScreenState.Showing)
                {
                    return IpcResponse.Fail("There is no second screen open. Open one first.");
                }

                var placement = request.Arg("placement");
                var size = request.Arg("size");
                if (placement.Length > 0 && !SecondScreenSettings.PlacementChoices.Contains(placement))
                {
                    return IpcResponse.Fail("Placement is beside or instead.");
                }

                if (size.Length > 0 && ScreenSize.Parse(size) is null)
                {
                    return IpcResponse.Fail(ScreenSize.Usage);
                }

                if (size.Length > 0 || placement.Length > 0)
                {
                    host.UpdateConfig(c =>
                    {
                        if (placement.Length > 0)
                        {
                            c.SecondScreen.Placement = placement;
                        }

                        if (ScreenSize.Parse(size) is { } wanted)
                        {
                            c.SecondScreen.Size = wanted.Size;
                            if (wanted.Size == "custom")
                            {
                                c.SecondScreen.CustomWidth = wanted.Width;
                                c.SecondScreen.CustomHeight = wanted.Height;
                            }
                        }
                    });
                }

                var known = window.AppsSerial is { } serial ? host.State.GetDevice(serial)?.Apps ?? [] : [];
                var wantedApp = request.Arg("app");
                var match = AppList.Resolve(known, wantedApp);
                var package = match.App?.Package ?? (match.Candidates.Count == 0 && PackageName.IsValid(wantedApp) ? wantedApp : null);
                if (package is null)
                {
                    return IpcResponse.Fail(match.Error!);
                }

                var result = await window.OpenOnSecondScreenAsync(package, request.Arg("fresh") == "true").ConfigureAwait(true);
                return result.Ok ? IpcResponse.Success(ScreenStatus(host, window)) : IpcResponse.Fail(result.Text);
            }

            default:
                return IpcResponse.Fail("The second screen understands open, app and close.");
        }
    }

    private static JsonObject ScreenStatus(AppHost host, MainWindow window)
    {
        var spec = window.ScreenSpecNow;
        return new JsonObject
        {
            ["state"] = window.ScreenState.ToString().ToLowerInvariant(),
            ["app"] = window.ScreenApp.Length == 0 ? null : window.ScreenApp,
            ["words"] = window.ScreenWords,
            ["displayId"] = window.ScreenDisplayId,
            ["width"] = spec?.Width,
            ["height"] = spec?.Height,
            ["dpi"] = spec?.Dpi,
            ["follows"] = spec?.Follows,
            ["placement"] = host.Config.SecondScreen.Placement,
            ["side"] = host.Config.SecondScreen.Side,
            ["squeezed"] = window.Group.Arrangement?.Squeezed ?? false,
            ["active"] = window.ScreenActive,
            ["processId"] = window.ScreenProcessId,
            ["reason"] = window.WhyNoSecondScreen,
            ["rect"] = window.ScreenView is { } view && view.ViewportScreenRect is { Width: > 0 } rect
                ? new JsonObject { ["left"] = rect.Left, ["top"] = rect.Top, ["width"] = rect.Width, ["height"] = rect.Height }
                : null,
            ["captions"] = new JsonArray([.. window.ViewCaptions.Select(c => (JsonNode)c)]),
            ["captionsShowing"] = window.ViewCaptionsShowing,
            ["outlined"] = window.ViewOutlined,
        };
    }
}
