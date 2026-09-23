using System.Text.Json.Nodes;
using System.Windows;
using Rex.Core;
using Rex.Mirror.Session;

namespace Rex.Mirror.Services;

/// <summary>Executes pipe requests from the CLI and agents against the running app. UI thread only.</summary>
public static class CommandRouter
{
    public static async Task<IpcResponse> HandleAsync(AppHost host, IpcRequest request)
    {
        var window = host.Window;
        var session = host.Session;

        switch (request.Command)
        {
            case "ping":
                return IpcResponse.Success(new JsonObject { ["app"] = "Android Headless Mirror", ["version"] = AppVersion });

            case "status":
                return IpcResponse.Success(Status(host));

            case "show":
                window?.ShowFromTray();
                return IpcResponse.Success();

            case "hide":
                window?.HideToTray();
                return IpcResponse.Success();

            case "quit":
                window?.QuitApplication();
                return IpcResponse.Success();

            case "refresh":
                window?.RefreshAll();
                return IpcResponse.Success();

            case "session-stop":
                session.StopMirror();
                return IpcResponse.Success();

            case "session-restart":
                session.RestartMirror();
                return IpcResponse.Success();

            case "screenshot":
            {
                var (ok, text) = await session.SaveScreenshotAsync().ConfigureAwait(true);
                return ok ? IpcResponse.Success(new JsonObject { ["path"] = text }) : IpcResponse.Fail(text);
            }

            case "zoom":
            {
                if (window is null)
                {
                    return IpcResponse.Fail("The window is not available.");
                }

                var direction = request.Arg("direction");
                var result = window.ApplyAppAction(direction switch
                {
                    "in" => "zoom-in",
                    "out" => "zoom-out",
                    "reset" => "zoom-reset",
                    _ => string.Empty,
                });
                return result.Ok
                    ? IpcResponse.Success(new JsonObject { ["zoom"] = Math.Round(window.Host.Zoom, 3) })
                    : IpcResponse.Fail(result.Text);
            }

            case "action":
            {
                var id = request.Arg("name");
                var result = await session.RunActionAsync(id, appId => window?.ApplyAppAction(appId) ?? AndroidResult.Failure("The window is not available.")).ConfigureAwait(true);
                return result.Ok ? IpcResponse.Success(new JsonObject { ["action"] = id, ["text"] = result.Text }) : IpcResponse.Fail(result.Text);
            }

            default:
                return IpcResponse.Fail($"Unknown command '{request.Command}'.");
        }
    }

    public static string AppVersion =>
        typeof(CommandRouter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static JsonObject Status(AppHost host)
    {
        var session = host.Session;
        return new JsonObject
        {
            ["phase"] = session.Phase.ToString().ToLowerInvariant(),
            ["message"] = session.Message,
            ["mirroring"] = session.IsMirroring,
            ["visibleDevices"] = session.Devices.Count,
            ["restartRequired"] = session.NeedsRestart,
            ["sidebarScrollOffset"] = host.Window?.SidebarScrollOffset ?? 0,
            ["ambientFrame"] = host.Window?.AmbientFrameAvailable ?? false,
            ["sidebarTab"] = host.Window?.SidebarTab,
            ["windowVisible"] = host.Window?.IsVisible ?? false,
            ["fullscreen"] = host.Window?.IsFullscreen ?? false,
            ["hudVisible"] = host.Window?.HudVisible ?? false,
            // Where the fullscreen controls are on screen, so a caller can reach for them.
            ["hudBar"] = host.Window is { } hud ? new JsonObject
            {
                ["left"] = Math.Round(hud.HudBarRect.X),
                ["top"] = Math.Round(hud.HudBarRect.Y),
                ["width"] = Math.Round(hud.HudBarRect.Width),
                ["height"] = Math.Round(hud.HudBarRect.Height),
            } : null,
            ["onboarding"] = host.Window?.OnboardingVisible ?? false,
            ["sidebarVisible"] = host.Window?.SidebarVisible ?? false,
            ["ambientVisible"] = host.Window?.AmbientVisible ?? false,
            ["navigatorVisible"] = host.Window?.NavigatorVisible ?? false,
            ["lockQuestion"] = session.PendingLockQuestionSerial is not null,
            ["zoom"] = host.Window is { } w ? Math.Round(w.Host.Zoom, 3) : 1.0,
            // Where the picture sits inside the mirror area, so a caller can tell whether it fills
            // the space it is given: after the phone turns, a landscape picture should.
            ["surface"] = host.Window is { } mirror ? new JsonObject
            {
                ["width"] = Math.Round(mirror.Host.SurfaceRect.Width),
                ["height"] = Math.Round(mirror.Host.SurfaceRect.Height),
                ["viewportWidth"] = mirror.Host.ViewportPixels.Width,
                ["viewportHeight"] = mirror.Host.ViewportPixels.Height,
            } : null,
            ["patternGuide"] = host.Window is { } window ? new JsonObject
            {
                ["visible"] = window.PatternGuideVisible,
                ["resolving"] = window.PatternGuideResolving,
                ["source"] = window.PatternGuideSource,
            } : null,
            ["device"] = session.ActiveDevice is null ? null : new JsonObject
            {
                ["serial"] = session.ActiveDevice.Serial,
                ["transport"] = session.ActiveDevice.Transport,
                ["name"] = session.Identity?.DisplayName,
                ["model"] = session.Identity?.Model,
                ["android"] = session.Identity?.AndroidVersion,
                ["battery"] = session.Battery?.Level,
            },
            ["devices"] = new JsonArray(session.Devices.Select(d => (JsonNode)new JsonObject
            {
                ["serial"] = d.Serial,
                ["state"] = d.State,
                ["transport"] = d.Transport,
            }).ToArray()),
        };
    }
}
