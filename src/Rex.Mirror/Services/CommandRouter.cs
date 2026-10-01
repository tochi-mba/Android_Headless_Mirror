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
                var result = await window.ApplyAppActionAsync(direction switch
                {
                    "in" => "zoom-in",
                    "out" => "zoom-out",
                    "reset" => "zoom-reset",
                    _ => string.Empty,
                }).ConfigureAwait(true);
                return result.Ok
                    ? IpcResponse.Success(new JsonObject { ["zoom"] = Math.Round(window.Host.Zoom, 3) })
                    : IpcResponse.Fail(result.Text);
            }

            case "action":
            {
                var id = request.Arg("name");
                var result = await session.RunActionAsync(id, appId => window is null
                    ? Task.FromResult(AndroidResult.Failure("The window is not available."))
                    : window.ApplyAppActionAsync(appId)).ConfigureAwait(true);
                window?.NoteAction(id, result.Ok);
                return result.Ok ? IpcResponse.Success(new JsonObject { ["action"] = id, ["text"] = result.Text }) : IpcResponse.Fail(result.Text);
            }

            default:
                return IpcResponse.Fail($"Unknown command '{request.Command}'.");
        }
    }

    public static string AppVersion =>
        typeof(CommandRouter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static JsonObject CopiesStatus(MainWindow window)
    {
        var state = window.CopiesState;
        return new JsonObject
        {
            ["wanted"] = state.Wanted,
            ["running"] = state.Running,
            ["starting"] = state.Starting,
            ["shown"] = state.ShownViews,
            ["hidden"] = state.Hidden,
            ["waiting"] = state.Waiting,
            ["room"] = state.Room == int.MaxValue ? null : state.Room,
            ["canAdd"] = state.CanAdd,
            ["canRemove"] = state.CanRemove,
            ["reason"] = state.WhyNoMore,
            ["summary"] = state.Summary,
            ["processes"] = new JsonArray(window.Copies.Processes.Select(p => (JsonNode)p.ProcessId).ToArray()),
            ["views"] = new JsonArray(window.AllViews.Select(view =>
            {
                var rect = view.ViewportScreenRect;
                var surface = view.SurfaceRect;
                return (JsonNode)new JsonObject
                {
                    ["x"] = rect.Left,
                    ["y"] = rect.Top,
                    ["width"] = rect.Width,
                    ["height"] = rect.Height,
                    ["shown"] = view.IsShown,
                    ["zoom"] = Math.Round(view.Zoom, 3),
                    ["surfaceX"] = Math.Round(surface.X),
                    ["surfaceY"] = Math.Round(surface.Y),
                    ["surfaceWidth"] = Math.Round(surface.Width),
                    ["surfaceHeight"] = Math.Round(surface.Height),
                };
            }).ToArray()),
        };
    }

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
            ["paused"] = host.Window?.MirrorPaused ?? false,
            ["sidebarScrollOffset"] = host.Window?.SidebarScrollOffset ?? 0,
            // Where the panel's scrolling area is on screen, so the audit can stitch its pages.
            ["sidebarViewport"] = host.Window is { SidebarViewport.IsEmpty: false } panel ? new JsonObject
            {
                ["left"] = Math.Round(panel.SidebarViewport.X),
                ["top"] = Math.Round(panel.SidebarViewport.Y),
                ["width"] = Math.Round(panel.SidebarViewport.Width),
                ["height"] = Math.Round(panel.SidebarViewport.Height),
            } : null,
            ["ambientFrame"] = host.Window?.AmbientFrameAvailable ?? false,
            ["sidebarTab"] = host.Window?.SidebarTab,
            ["windowVisible"] = host.Window?.IsVisible ?? false,
            // Keys from anywhere: what is set, whether the hook is listening, and what the last one did.
            ["globalKeys"] = host.Window is { } keys ? new JsonObject
            {
                ["enabled"] = host.Config.GlobalKeys.Enabled,
                ["showHide"] = host.Config.GlobalKeys.ShowHide,
                ["actions"] = new JsonArray([.. host.Config.GlobalKeys.Actions.Select(a => (JsonNode)new JsonObject { ["key"] = a.Key, ["action"] = a.Action })]),
                ["recording"] = Views.Controls.ChordBox.AnyRecording,
                ["hookInstalled"] = keys.KeyboardHookInstalled,
                ["foregroundStep"] = keys.LastForegroundStep,
            } : null,
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
            // What the app is teaching right now, so a test can drive the tour and the hints.
            ["tour"] = host.Window is { } teaching ? new JsonObject
            {
                ["visible"] = teaching.TourVisible,
                ["step"] = teaching.TourStep,
                ["steps"] = teaching.TourStepCount,
            } : null,
            ["tip"] = host.Window?.TipShowing ?? string.Empty,
            ["sidebarVisible"] = host.Window?.SidebarVisible ?? false,
            ["sidebarWidth"] = host.Window?.SidebarWidthDip ?? 0,
            ["ambientVisible"] = host.Window?.AmbientVisible ?? false,
            ["navigatorVisible"] = host.Window?.NavigatorVisible ?? false,
            ["navigatorPicture"] = host.Window?.NavigatorPictureVisible ?? false,
            ["capture"] = host.Window?.CapturePath,
            // Where the navigator is and whether it is being dragged, so a test can grab it.
            ["navigator"] = host.Window is { } nav ? new JsonObject
            {
                ["left"] = Math.Round(nav.NavigatorRect.X),
                ["top"] = Math.Round(nav.NavigatorRect.Y),
                ["width"] = Math.Round(nav.NavigatorRect.Width),
                ["height"] = Math.Round(nav.NavigatorRect.Height),
                ["dragging"] = nav.NavigatorDragging,
            } : null,
            ["lockQuestion"] = session.PendingLockQuestionSerial is not null,
            ["usb"] = host.Usb.Status(),
            ["zoom"] = host.Window is { } w ? Math.Round(w.Host.Zoom, 3) : 1.0,
            // Where the picture sits inside the mirror area, so a caller can tell whether it fills
            // the space it is given: after the phone turns, a landscape picture should.
            ["surface"] = host.Window is { } mirror ? new JsonObject
            {
                ["x"] = Math.Round(mirror.Host.SurfaceRect.X),
                ["y"] = Math.Round(mirror.Host.SurfaceRect.Y),
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
                ["spinning"] = window.PatternSpinnerRunning,
                // Where the dots sit, as fractions of the picture, so a test can see the guide
                // move when Android moves the pattern.
                ["bounds"] = window.PatternGuideBounds is { } bounds ? new JsonObject
                {
                    ["left"] = Math.Round(bounds.Left, 4),
                    ["top"] = Math.Round(bounds.Top, 4),
                    ["right"] = Math.Round(bounds.Right, 4),
                    ["bottom"] = Math.Round(bounds.Bottom, 4),
                } : null,
            } : null,
            // The video size scrcpy last reported: the shape the phone's picture really is.
            ["video"] = session.Scrcpy?.VideoSize is { } video ? new JsonObject
            {
                ["width"] = video.Width,
                ["height"] = video.Height,
            } : null,
            // The copies of the phone: how many are wanted, running and given room, and where each
            // view is on screen with its zoom, so a test can see them side by side and in step.
            ["copies"] = host.Window is { } copies ? CopiesStatus(copies) : null,
            ["window"] = host.Window is { } shown ? new JsonObject
            {
                ["topmost"] = shown.Topmost,
                ["sidebarSide"] = shown.SidebarOnLeft ? "left" : "right",
                ["quickButtons"] = new JsonArray(shown.QuickButtonsShowing.Select(id => (JsonNode)id).ToArray()),
                ["hints"] = shown.HintsShowing,
                ["frameRate"] = shown.FrameRateShowing,
                ["lastNotification"] = shown.LastNotification,
                ["foreground"] = shown.InFront,
            } : null,
            ["view"] = new JsonObject
            {
                ["orientation"] = DisplayOrientation.Name(session.ViewOrientation),
                ["paused"] = session.ViewPaused,
            },
            ["keyboard"] = new JsonObject
            {
                ["mode"] = session.Scrcpy?.KeyboardMode,
                ["browse"] = host.Window?.BrowseMode ?? false,
                ["altHeldForPc"] = host.Window?.AllViews.Any(view => view.HoldingKeyboard) ?? false,
            },
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
