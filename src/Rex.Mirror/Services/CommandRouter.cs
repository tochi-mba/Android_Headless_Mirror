using System.Text.Json.Nodes;
using System.Windows;
using System.Text.Json;
using Rex.Core;
using Rex.Mirror.Session;

namespace Rex.Mirror.Services;

/// <summary>Executes pipe requests from the CLI and agents against the running app. UI thread only.</summary>
public static partial class CommandRouter
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

            case "sound":
            {
                if (window is null)
                {
                    return IpcResponse.Fail("The window is not available.");
                }

                var sound = window.PhoneSound;
                if (!sound.Available)
                {
                    return IpcResponse.Fail(sound.Problem!);
                }

                var verb = request.Arg("verb");
                if (verb.Length > 0 && Sound.SoundVerbs.Apply(sound, verb) is { } refused)
                {
                    return IpcResponse.Fail(refused);
                }

                return IpcResponse.Success(SoundStatus(window));
            }

            case "action":
            {
                var id = request.Arg("name");
                if (window?.RunOnSecondScreen(id) is { } routed)
                {
                    return routed.Ok ? IpcResponse.Success(new JsonObject { ["action"] = id, ["text"] = routed.Text }) : IpcResponse.Fail(routed.Text);
                }

                var result = await session.RunActionAsync(id, appId => window is null
                    ? Task.FromResult(AndroidResult.Failure("The window is not available."))
                    : window.ApplyAppActionAsync(appId)).ConfigureAwait(true);
                window?.NoteAction(id, result.Ok);
                return result.Ok ? IpcResponse.Success(new JsonObject { ["action"] = id, ["text"] = result.Text }) : IpcResponse.Fail(result.Text);
            }

            case "apps":
                return await AppsAsync(host, request).ConfigureAwait(true);

            case "open-app":
                return await OpenAppAsync(host, request).ConfigureAwait(true);

            case "screen":
                return await ScreenAsync(host, request).ConfigureAwait(true);

            case "push":
            {
                if (window is null) return IpcResponse.Fail("The window is not available.");
                var paths = PathsOf(request);
                if (paths.Length == 0) return IpcResponse.Fail("No files were given.");
                return IpcResponse.Success(new JsonObject { ["queued"] = await window.PushFromOutsideAsync(paths).ConfigureAwait(true) });
            }

            // A drag and a drop as Windows would deliver them, for tests: real OLE dragging cannot be
            // driven from a test, so these call what the window's own drag handlers call.
            case "drag" when TestHooks.Enabled && window is not null:
                return window.ArmForDrag(PathsOf(request), fromTest: true) ? IpcResponse.Success(new JsonObject { ["hint"] = window.FilesHint }) : IpcResponse.Fail("Nothing can be dropped.");

            case "drop" when TestHooks.Enabled && window is not null:
                return IpcResponse.Success(new JsonObject { ["queued"] = await window.SendPathsAsync(PathsOf(request), dropped: true).ConfigureAwait(true) });

            default:
                return IpcResponse.Fail($"Unknown command '{request.Command}'.");
        }
    }

    public static string AppVersion =>
        typeof(CommandRouter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The phone's sound on this PC: whether there is any, its level and what is acting on it.</summary>
    private static JsonObject SoundStatus(MainWindow window)
    {
        var sound = window.PhoneSound;
        return new JsonObject
        {
            ["available"] = sound.Available,
            ["why"] = sound.Problem,
            ["volume"] = Math.Round(sound.Volume, 3),
            ["muted"] = sound.Muted,
            ["level"] = Math.Round(sound.Level, 3),
            ["mutedBy"] = sound.Target.Muted && !sound.Muted ? sound.Target.Why : null,
            ["lowered"] = !sound.Target.Muted && sound.Target.Why is not null,
            ["balance"] = Math.Round(sound.Balance, 3),
            ["perPhone"] = sound.PerPhone,
            ["followMixer"] = sound.FollowMixer,
            ["channels"] = sound.Channels,
            ["panelOpen"] = window.SoundPanelOpen,
        };
    }

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

    private static string[] PathsOf(IpcRequest request)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(request.Arg("paths")) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonObject FilesStatus(AppHost host, MainWindow window)
    {
        var jobs = window.Transfers;
        return new JsonObject
        {
            // What the app is using now, which a change to config.json reaches a moment later.
            ["folder"] = host.Config.Transfer.Folder,
            ["scanMedia"] = host.Config.Transfer.ScanMedia,
            ["armed"] = window.FilesArmed,
            ["hint"] = window.FilesHint,
            ["waiting"] = jobs.Count(job => job.State == TransferState.Waiting),
            ["running"] = jobs.Count(job => job.State is TransferState.Sending or TransferState.Installing),
            ["done"] = jobs.Count(job => job.State is TransferState.Done or TransferState.Installed),
            ["failed"] = jobs.Count(job => job.State == TransferState.Failed),
            ["items"] = new JsonArray(jobs.Select(job => (JsonNode)new JsonObject
            {
                ["id"] = job.Id.ToString("N"),
                ["name"] = job.Item.Entry.Name,
                ["kind"] = job.Item.Kind.ToString().ToLowerInvariant(),
                ["state"] = job.State.ToString().ToLowerInvariant(),
                ["sent"] = job.Sent,
                ["size"] = job.Item.Entry.Size,
                ["percent"] = job.Percent,
                ["bytesPerSecond"] = Math.Round(job.BytesPerSecond),
                ["secondsLeft"] = job.TimeLeft is { } left ? Math.Ceiling(left.TotalSeconds) : null,
                ["target"] = job.Item.Target,
                ["error"] = job.Why,
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
            ["mirrorProcessId"] = session.Scrcpy?.ProcessId,
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
            ["configReloads"] = host.ConfigReloads,
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
            ["sound"] = host.Window is { } sounding ? SoundStatus(sounding) : null,
            ["apps"] = host.Window is { } listing ? AppsStatus(host, listing) : null,
            ["files"] = host.Window is { } files ? FilesStatus(host, files) : null,
            ["secondScreen"] = host.Window is { } screen ? ScreenStatus(host, screen) : null,
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
