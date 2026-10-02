using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Launches the real desktop app against the fake adb and fake scrcpy, drives it over the pipe
/// and saves screenshots under artifacts/screens. This is the closest CI gets to a phone.
/// </summary>
[Collection("desktop")]
public sealed partial class AppEndToEndTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task SoftBackgroundAndNavigator_FollowAMovingPicture()
    {
        // A still picture proves the background appears; a moving one proves it keeps up. The
        // fake phone cycles red, green and blue like a video, and both the soft background around
        // the phone and the navigator's picture of it must change colour with it.
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.AnimateMarker, string.Empty);
        var config = ConfigFile.Load(package.Paths.Config);
        config.Ambient.FrameRate = 30;
        config.Ambient.Opacity = 1;
        config.Zoom.NavigatorAlways = true;
        ConfigFile.Save(package.Paths.Config, config);

        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var status = await app.WaitForStatusAsync(
            s => s["ambientFrame"]!.GetValue<bool>() && s["navigatorPicture"]!.GetValue<bool>(),
            StartupTimeout,
            "the soft background and the navigator picture");
        // With "keep the navigator on screen" set, it shows at 100% as a live preview.
        Assert.Equal(1.0, status["zoom"]!.GetValue<double>());
        await app.FocusAsync();

        var navigator = status["navigator"]!;
        var bounds = app.WindowBounds();
        var inNavigator = new System.Drawing.Point(
            (int)(navigator["left"]!.GetValue<double>() + navigator["width"]!.GetValue<double>() / 2) - bounds.Left,
            (int)(navigator["top"]!.GetValue<double>() + navigator["height"]!.GetValue<double>() / 2) - bounds.Top);

        var marginColours = new HashSet<char>();
        var navigatorColours = new HashSet<char>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
        while ((marginColours.Count < 3 || navigatorColours.Count < 3) && DateTime.UtcNow < deadline)
        {
            using var shot = await app.CaptureWindowAsync();
            if (Dominant(MeanColour(shot, new System.Drawing.Rectangle(shot.Width / 20, shot.Height / 5, shot.Width / 7, shot.Height * 3 / 5))) is { } margin)
            {
                marginColours.Add(margin);
            }

            if (Dominant(MeanColour(shot, new System.Drawing.Rectangle(inNavigator.X - 6, inNavigator.Y - 6, 12, 12))) is { } preview)
            {
                navigatorColours.Add(preview);
            }

            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        await app.SaveScreenshotAsync("ambient-moving.png");
        Assert.True(marginColours.Count == 3, $"The soft background must follow the video; it showed only {string.Join(",", marginColours)}.");
        Assert.True(navigatorColours.Count == 3, $"The navigator picture must follow the video; it showed only {string.Join(",", navigatorColours)}.");
        await app.QuitAsync();
    }

    private static System.Drawing.Color MeanColour(System.Drawing.Bitmap bitmap, System.Drawing.Rectangle area)
    {
        long r = 0, g = 0, b = 0, count = 0;
        for (var y = Math.Max(0, area.Top); y < Math.Min(bitmap.Height, area.Bottom); y += 2)
        {
            for (var x = Math.Max(0, area.Left); x < Math.Min(bitmap.Width, area.Right); x += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                count++;
            }
        }

        return count == 0 ? System.Drawing.Color.Black : System.Drawing.Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }

    /// <summary>R, G or B when one channel clearly leads, which is all the fake video ever shows; null in between.</summary>
    private static char? Dominant(System.Drawing.Color colour)
    {
        var channels = new[] { ('R', colour.R), ('G', colour.G), ('B', colour.B) }.OrderByDescending(c => c.Item2).ToArray();
        return channels[0].Item2 >= 60 && channels[0].Item2 - channels[1].Item2 >= 40 ? channels[0].Item1 : null;
    }

    [Fact]
    public async Task AmbientBackgroundAndNavigator_FollowTheLiveMirror()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        // The soft background is a live copy of the on-screen mirror, captured small and blurred
        // before it ever reaches the window; nothing is fetched from the phone for it.
        await app.WaitForStatusAsync(
            s => s["ambientVisible"]!.GetValue<bool>() && s["ambientFrame"]!.GetValue<bool>(),
            StartupTimeout,
            "live soft background");
        await app.FocusAsync();
        await app.SaveScreenshotAsync("ambient-live.png");
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.WaitForStatusAsync(s => s["navigatorVisible"]!.GetValue<bool>(), StartupTimeout, "navigator");
        // The navigator carries a live picture of the whole phone screen, from the same capture as
        // the soft background: one copy of the screen serves both.
        var status = await app.WaitForStatusAsync(s => s["navigatorPicture"]!.GetValue<bool>(), StartupTimeout, "navigator picture");
        Assert.Contains(status["capture"]!.GetValue<string>(), new[] { "gpu", "gdi" });
        await app.SaveScreenshotAsync("navigator.png");

        // Neither picture ever comes from the phone: zooming must not send a screenshot request.
        Assert.DoesNotContain(package.AdbCalls(), line => line.Contains("screencap", StringComparison.Ordinal));

        // Switching the picture off leaves the frame; the mode stays adjustable while zoomed.
        package.EditConfig(c => c.Zoom.NavigatorPicture = false);
        await app.WaitForStatusAsync(s => s["navigatorVisible"]!.GetValue<bool>() && !s["navigatorPicture"]!.GetValue<bool>(), StartupTimeout, "navigator without its picture");
        await app.QuitAsync();
    }

    [Fact]
    public async Task SoftBackground_PaintsTheSpaceAroundThePhone()
    {
        // The soft background is meant to be seen. Checking that a picture is attached proves
        // nothing: a copy taken from the screen carries no transparency, so a frame can be present
        // and still draw as nothing at all. This compares what the window actually looks like with
        // the background on and off, and the phone itself is identical in both.
        using var package = new TestPackage(withFakeTools: true);
        TestPackage.WritePreviewImage(Path.Combine(package.ToolsFolder, "preview.png"));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(
            s => s["ambientVisible"]!.GetValue<bool>() && s["ambientFrame"]!.GetValue<bool>(),
            StartupTimeout,
            "soft background");
        await app.FocusAsync();

        // A frame being attached is not the same as one being on screen: the first copy can be
        // taken before the fake phone has painted its picture (all black, which blurs to nothing),
        // and the layered window that draws the background composes a beat after the app does,
        // on the way in and on the way out. The picture is a blue-to-orange gradient, so the strip
        // of mirror area left of the phone has colour in it exactly while the background is drawn;
        // the app's own chrome there is grey and dark.
        using var lit = await CaptureWhenAsync(app, bitmap => MarginColour(bitmap) > 0.2, "the soft background to be on screen");

        // Turning it off in the config file also exercises the live reload the settings panel uses.
        package.EditConfig(c => c.Ambient.Enabled = false);
        await app.WaitForStatusAsync(s => !s["ambientVisible"]!.GetValue<bool>(), StartupTimeout, "soft background off");
        using var dark = await CaptureWhenAsync(app, bitmap => MarginColour(bitmap) < 0.1, "the soft background to leave the screen");

        var painted = PaintedFraction(lit, dark);
        Assert.True(painted > 0.15,
            $"The soft background must fill the space around the phone; only {painted:P0} of the window changed when it was switched off.");
        await app.QuitAsync();
    }

    /// <summary>Captures the window once it looks the way a test is waiting for, or fails saying what it waited for.</summary>
    private static async Task<System.Drawing.Bitmap> CaptureWhenAsync(AppProcess app, Func<System.Drawing.Bitmap, bool> ready, string description)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
        while (true)
        {
            var bitmap = await app.CaptureWindowAsync();
            if (ready(bitmap))
            {
                return bitmap;
            }

            var margin = MarginColour(bitmap);
            bitmap.Dispose();
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}; {margin:P0} of the margin left of the phone has colour in it.");
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// How much of the strip left of the phone carries colour rather than the app's grey and black.
    /// The phone sits in the middle of the mirror area with the side panel on the right, so the
    /// left fifth of the window, away from the title and status bars, is soft background or nothing.
    /// </summary>
    private static double MarginColour(System.Drawing.Bitmap bitmap)
    {
        var colourful = 0;
        var count = 0;
        for (var y = bitmap.Height / 5; y < bitmap.Height * 4 / 5; y += 4)
        {
            for (var x = bitmap.Width / 20; x < bitmap.Width / 5; x += 4)
            {
                var pixel = bitmap.GetPixel(x, y);
                var spread = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                if (spread > 20)
                {
                    colourful++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : (double)colourful / count;
    }

    /// <summary>
    /// How much of the window two captures disagree about, sampled coarsely because a blurred
    /// background covers broad areas and has no fine detail to miss.
    /// </summary>
    private static double PaintedFraction(System.Drawing.Bitmap lit, System.Drawing.Bitmap dark)
    {
        Assert.Equal(lit.Size, dark.Size);
        var changed = 0;
        var count = 0;
        for (var y = 0; y < lit.Height; y += 4)
        {
            for (var x = 0; x < lit.Width; x += 4)
            {
                var a = lit.GetPixel(x, y);
                var b = dark.GetPixel(x, y);
                var difference = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                if (difference > 12)
                {
                    changed++;
                }

                count++;
            }
        }

        return count == 0 ? 0 : (double)changed / count;
    }

    [Fact]
    public async Task Landscape_RefitsThePictureAndTheSoftBackgroundAroundIt()
    {
        // Turning the phone changes the picture's shape. Keeping the old view would leave a wide
        // picture inside the tall rectangle the portrait one left behind: letterboxed on every
        // side, with the soft background still masked around a phone that is no longer there.
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => s["ambientVisible"]!.GetValue<bool>(), StartupTimeout, "soft background");

        var portrait = await Surface(app);
        Assert.True(portrait.Width < portrait.Height, "The phone starts upright.");
        Assert.InRange(portrait.Height, portrait.ViewportHeight - 2, portrait.ViewportHeight + 2);

        await app.ActionAsync("rotation-landscape");
        await app.WaitForStatusAsync(
            s => s["surface"]!["width"]!.GetValue<double>() > s["surface"]!["height"]!.GetValue<double>(),
            StartupTimeout,
            "the picture to turn");

        var landscape = await Surface(app);
        Assert.InRange(landscape.Width, landscape.ViewportWidth - 2, landscape.ViewportWidth + 2);
        Assert.True(landscape.Height <= landscape.ViewportHeight + 2, "The picture must still fit inside the mirror area.");
        await app.SaveScreenshotAsync("landscape-refit.png");
        await app.QuitAsync();
    }

    [Fact]
    public async Task Landscape_FillsTheWidthEvenWhenScrcpyNeverResizesItsWindow()
    {
        // The case seen on a real phone: the video turned, but scrcpy's window kept its portrait
        // size (its one resize was lost), so the picture sat letterboxed at portrait width. scrcpy
        // still reports the new video size, and that alone must turn the picture.
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.KeepWindowMarker, string.Empty);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var portrait = await Surface(app);
        Assert.True(portrait.Width < portrait.Height, "The phone starts upright.");

        await app.ActionAsync("rotation-landscape");
        await app.WaitUntilAsync(() => package.ScrcpyLog().Contains("texture 2400x1080"), StartupTimeout, "scrcpy to report the turn");
        await app.WaitForStatusAsync(s => s["video"]?["width"]?.GetValue<int>() == 2400, TimeSpan.FromSeconds(5), "the report to reach the app");
        await app.WaitForStatusAsync(
            s => s["surface"]!["width"]!.GetValue<double>() > s["surface"]!["height"]!.GetValue<double>(),
            TimeSpan.FromSeconds(5),
            "the picture to turn from the report alone");
        var landscape = await Surface(app);
        Assert.InRange(landscape.Width, landscape.ViewportWidth - 2, landscape.ViewportWidth + 2);

        await app.ActionAsync("rotation-portrait");
        await app.WaitForStatusAsync(
            s => s["surface"]!["width"]!.GetValue<double>() < s["surface"]!["height"]!.GetValue<double>(),
            TimeSpan.FromSeconds(10),
            "the picture to turn back");
        await app.QuitAsync();
    }

    /// <summary>
    /// Whether a picture is exactly the video's shape, to the pixel it is rounded to. A picture the
    /// shape of whatever scrcpy's window opened at is letterboxed inside by real scrcpy, and was
    /// only caught by eye before this.
    /// </summary>
    private static bool HasTheVideosShape(JsonObject status, double width, double height)
    {
        if (status["video"] is not { } video || width <= 0 || height <= 0)
        {
            return false;
        }

        var aspect = video["width"]!.GetValue<double>() / video["height"]!.GetValue<double>();
        return aspect < 1
            ? Math.Abs(width - height * aspect) <= 1.5
            : Math.Abs(height - width / aspect) <= 1.5;
    }

    private static bool PictureHasTheVideosShape(JsonObject status) =>
        HasTheVideosShape(status, status["surface"]!["width"]!.GetValue<double>(), status["surface"]!["height"]!.GetValue<double>());

    private static async Task<(double Width, double Height, double ViewportWidth, double ViewportHeight)> Surface(AppProcess app)
    {
        var status = await app.SendAsync(new IpcRequest("status"));
        var surface = status.Data!["surface"]!;
        return (
            surface["width"]!.GetValue<double>(),
            surface["height"]!.GetValue<double>(),
            surface["viewportWidth"]!.GetValue<double>(),
            surface["viewportHeight"]!.GetValue<double>());
    }

    [Fact]
    public async Task FullscreenControls_CanBeDraggedAnywhereAndPinnedBack()
    {
        using var package = new TestPackage(withFakeTools: true);

        // This is about dragging, not about being told things: the first-time hint makes the bar a
        // different shape, and it has its own test.
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { TipsSeen = [Tips.FirstFullscreen] });

        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.PressKeyAsync(0x7A); // F11
        await app.WaitForStatusAsync(
            data => data["fullscreen"]!.GetValue<bool>() && data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(15),
            "fullscreen controls");
        var monitor = app.MonitorBounds();

        // Dragging the bar itself puts it wherever it is dropped, and that is remembered. The press
        // lands just inside its leading edge, which is bar rather than button.
        app.MovePointerToTop();
        var bar = await SettledBar(app);
        var grabX = (int)(bar.Left + (bar.Width / 2));
        var grabY = (int)(bar.Top + (bar.Height * 0.85));
        var dropX = monitor.Left + (int)(monitor.Width * 0.45);
        var dropY = monitor.Top + (int)(monitor.Height * 0.7);
        await app.DragAsync(grabX, grabY, dropX, dropY);

        await app.WaitUntilAsync(
            () => ConfigFile.Load(package.Paths.Config).Hud.IsPlaced,
            TimeSpan.FromSeconds(10),
            "the dragged position to be saved");

        // The bar ends up where it was let go. How exactly the grab offset is kept is arithmetic,
        // and HudLayout covers that precisely; what matters here is that a drag across the screen
        // lands the bar under the hand that moved it.
        var placed = ConfigFile.Load(package.Paths.Config).Hud;
        Assert.InRange(placed.X!.Value, (dropX / (double)monitor.Width) - 0.06, (dropX / (double)monitor.Width) + 0.06);
        Assert.InRange(placed.Y!.Value, (dropY / (double)monitor.Height) - 0.08, (dropY / (double)monitor.Height) + 0.08);

        var moved = await SettledBar(app);
        Assert.True(moved.Top > monitor.Top + (monitor.Height / 2),
            "The controls must sit where they were dropped.");
        await app.SaveScreenshotAsync("fullscreen-hud-dragged.png");

        // Pressing a control is a click, never a drag: the bar must not run away with the pointer.
        var buttonX = (int)(moved.Left + (moved.Width * 0.17));
        var buttonY = (int)(moved.Top + (moved.Height * 0.25));
        await app.DragAsync(buttonX, buttonY, buttonX, buttonY);
        Assert.Contains(package.AdbCalls(), line => line.Contains("keyevent", StringComparison.Ordinal));
        var after = ConfigFile.Load(package.Paths.Config).Hud;
        Assert.Equal(placed.X, after.X);
        Assert.Equal(placed.Y, after.Y);

        // Choosing a position again pins them back, wherever they were dragged to.
        package.EditConfig(c =>
        {
            c.Hud.Position = "bottom";
            c.Hud.X = null;
            c.Hud.Y = null;
        });
        await app.WaitForStatusAsync(
            data => data["hudBar"]!["top"]!.GetValue<double>() > monitor.Top + (monitor.Height * 0.8),
            TimeSpan.FromSeconds(15),
            "the controls to go back to the bottom");

        await app.PressKeyAsync(0x1B); // Esc leaves fullscreen.
        await app.WaitForStatusAsync(data => !data["fullscreen"]!.GetValue<bool>(), TimeSpan.FromSeconds(10), "windowed again");
        await app.QuitAsync();
    }

    /// <summary>
    /// The bar once it has stopped moving. It is centred on whatever it is pinned to and its status
    /// line changes width as it reports things, so a position read a moment too early is no longer
    /// where the bar is when a press arrives.
    /// </summary>
    private static async Task<(double Left, double Top, double Width, double Height)> SettledBar(AppProcess app)
    {
        var previous = (Left: double.NaN, Top: double.NaN, Width: double.NaN, Height: double.NaN);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var status = await app.SendAsync(new IpcRequest("status"));
            var bar = status.Data!["hudBar"]!;
            var current = (
                bar["left"]!.GetValue<double>(),
                bar["top"]!.GetValue<double>(),
                bar["width"]!.GetValue<double>(),
                bar["height"]!.GetValue<double>());
            if (current == previous && current.Item3 > 0)
            {
                return current;
            }

            previous = current;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The fullscreen controls never settled in one place.");
    }

    [Fact]
    public async Task TwoPhones_AreOfferedAndTheChoiceIsRemembered()
    {
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new
        {
            Devices = new[]
            {
                new { Serial = "FAKE123", State = "device", Model = "Fake Phone" },
                new { Serial = "FAKE456", State = "device", Model = "Second Phone" },
            },
        });

        // Both phones already have an answer, so the notice bar is free to carry the hint: one
        // question at a time is the whole point of it.
        var state = new StateStore(package.Paths.State);
        state.SetLockScreenMode("FAKE123", LockScreenModes.None);
        state.SetLockScreenMode("FAKE456", LockScreenModes.None);

        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var status = await app.WaitForStatusAsync(
            data => data["visibleDevices"]!.GetValue<int>() == 2,
            StartupTimeout,
            "both phones");

        // With a second phone on the desk the app says so once, and the chip becomes the way to choose.
        Assert.Equal("second-phone", status["tip"]!.GetValue<string>());
        await app.SaveScreenshotAsync("two-phones.png");

        // The chip opens a menu in the app's own style listing both, with what each one is doing.
        app.Ui.Invoke("DeviceChip");
        var items = app.Ui.OpenMenuItems();
        Assert.True(items.Count == 2, "The picker should list the two phones, not: " + string.Join(" | ", items.Select(i => $"{i.Current.Name} [{i.Current.ClassName}]")));
        Assert.Equal("Fake Phone, USB · mirroring now", items[0].Current.Name);
        Assert.Equal("Second Phone, USB · ready", items[1].Current.Name);
        await app.SaveScreenshotAsync("phone-picker.png");

        // Picking the second phone switches the mirror to it and is remembered.
        ((System.Windows.Automation.InvokePattern)items[1].GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
        await app.WaitForStatusAsync(
            data => data["device"]?["serial"]?.GetValue<string>() == "FAKE456" && data["phase"]!.GetValue<string>() == "mirroring",
            StartupTimeout,
            "the picked phone mirroring");
        Assert.Equal("FAKE456", ConfigFile.Load(package.Paths.Config).Session.PreferredSerial);
        await app.QuitAsync();

        using var again = new AppProcess(package);
        var second = await again.WaitForStatusAsync(
            data => data["device"]?["serial"]?.GetValue<string>() == "FAKE456",
            StartupTimeout,
            "the second phone mirroring");
        Assert.Equal("FAKE456", second["device"]!["serial"]!.GetValue<string>());
        await again.QuitAsync();
    }

    [Fact]
    public async Task Navigator_MovesTheViewAndLetsGoWhereverTheButtonIsReleased()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.FocusAsync();

        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        var zoomed = await app.WaitForStatusAsync(
            data => data["navigatorVisible"]!.GetValue<bool>() && data["navigator"]!["width"]!.GetValue<double>() > 0,
            StartupTimeout,
            "the navigator");

        var navigator = zoomed["navigator"]!;
        var startX = (int)(navigator["left"]!.GetValue<double>() + (navigator["width"]!.GetValue<double>() / 2));
        var startY = (int)(navigator["top"]!.GetValue<double>() + (navigator["height"]!.GetValue<double>() / 2));
        var before = Offset(zoomed);

        // Dragging inside it moves what the mirror is looking at.
        await app.DragAsync(startX, startY, startX - 40, startY - 30);
        var moved = await app.WaitForStatusAsync(data => Offset(data) != before, StartupTimeout, "the view to move");
        await app.SaveScreenshotAsync("navigator-dragged.png");

        // Letting go anywhere at all ends the drag. The button coming up over another window is
        // exactly what used to leave the view following the pointer around the screen.
        var monitor = app.MonitorBounds();
        await app.DragAsync(startX, startY, startX - 220, startY - 160);
        await app.WaitForStatusAsync(
            data => !data["navigator"]!["dragging"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(10),
            "the navigator to let go");

        // And having let go, the view stays where it was put however far the pointer wanders,
        // including back across the navigator itself.
        var settled = Offset((await app.SendAsync(new IpcRequest("status"))).Data!);
        app.MovePointerTo(startX, startY);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        app.MovePointerTo(startX - 60, startY - 60);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        app.MovePointerTo(monitor.Left + (monitor.Width / 2), monitor.Top + (monitor.Height / 2));
        await Task.Delay(400, TestContext.Current.CancellationToken);

        var after = await app.SendAsync(new IpcRequest("status"));
        Assert.Equal(settled, Offset(after.Data!));
        Assert.False(after.Data!["navigator"]!["dragging"]!.GetValue<bool>());
        _ = moved;

        // Reset puts the whole phone back in the window, which is the way out of any mess.
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "reset" }));
        await app.WaitForStatusAsync(data => data["zoom"]!.GetValue<double>() == 1, TimeSpan.FromSeconds(10), "the zoom to reset");
        await app.QuitAsync();

        static (double X, double Y) Offset(System.Text.Json.Nodes.JsonNode status) =>
            (status["surface"]!["x"]!.GetValue<double>(), status["surface"]!["y"]!.GetValue<double>());
    }

    [Fact]
    public async Task SidebarWheelScrollsSettingsWithoutReachingPhone()
    {
        using var package = new TestPackage(withFakeTools: true);
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { SidebarTab = "settings", SidebarVisible = true });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var before = package.ScrcpyLog().Count(line => line.Contains("mousewheel", StringComparison.Ordinal));
        // A wheel turn sent the moment the window comes forward can land before it is ready for
        // it; a person would turn it again, and so does this, up to three times.
        for (var turn = 0; turn < 3; turn++)
        {
            await app.ScrollSidebarAsync();
            var scrolled = (await app.SendAsync(new IpcRequest("status"))).Data!["sidebarScrollOffset"]!.GetValue<double>() > 0;
            if (scrolled || turn == 2)
            {
                break;
            }

            await Task.Delay(700, TestContext.Current.CancellationToken);
        }

        await app.SaveScreenshotAsync("sidebar-scroll.png");
        await app.WaitForStatusAsync(s => s["sidebarScrollOffset"]!.GetValue<double>() > 0, TimeSpan.FromSeconds(5), "sidebar scroll");
        Assert.Equal(before, package.ScrcpyLog().Count(line => line.Contains("mousewheel", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MirrorsAFakePhone_ZoomsAndAcceptsActions()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // The picture is the video's shape, whatever size scrcpy's window opened at.
        await app.WaitForStatusAsync(PictureHasTheVideosShape, TimeSpan.FromSeconds(10), "the picture to take the video's shape");
        // A moment later it has not drifted: the launch shape used to be taken up on a later tick.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var settled = await app.SendAsync(new IpcRequest("status"));
        Assert.True(PictureHasTheVideosShape(settled.Data!.AsObject()), $"The picture drifted from the video's shape: {settled.Data!["surface"]!.ToJsonString()}");

        Assert.Equal("FAKE123", status["device"]!["serial"]!.GetValue<string>());
        Assert.Equal("Galaxy S21 Ultra", status["device"]!["name"]!.GetValue<string>());
        Assert.True(app.IsPerMonitorV2(), "The main window must run with PerMonitorV2 DPI awareness.");
        Assert.Contains(package.ScrcpyLog(), line => line.Contains("--window-borderless", StringComparison.Ordinal) && line.Contains("--shortcut-mod=rctrl", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.EndsWith("shell wm dismiss-keyguard", StringComparison.Ordinal));

        var zoomed = await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        Assert.True(zoomed.Ok, zoomed.Error);
        Assert.True(zoomed.Data!["zoom"]!.GetValue<double>() > 1.0);
        await app.SaveScreenshotAsync("mirroring-zoomed.png");

        var reset = await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "reset" }));
        Assert.Equal(1.0, reset.Data!["zoom"]!.GetValue<double>());

        var sleep = await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "sleep" }));
        Assert.True(sleep.Ok, sleep.Error);
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(l => l.StartsWith("key ", StringComparison.Ordinal) && l.Contains("vk=79", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "scrcpy shortcut key log");
        var keys = package.ScrcpyLog().Where(l => l.StartsWith("key ", StringComparison.Ordinal)).ToArray();
        Assert.Contains(keys, k => k.Contains("vk=163", StringComparison.Ordinal)); // Right Ctrl
        Assert.DoesNotContain(keys, k => k.Contains("vk=165", StringComparison.Ordinal)); // Right Alt is not part of the scrcpy modifier.
        Assert.Contains(keys, k => k.Contains("vk=79", StringComparison.Ordinal));  // O

        var home = await app.SendAsync(new IpcRequest("action", new Dictionary<string, string> { ["name"] = "home" }));
        Assert.True(home.Ok, home.Error);
        Assert.Contains(package.AdbCalls(), line => line.EndsWith("input keyevent KEYCODE_HOME", StringComparison.Ordinal));

        var shot = await app.SendAsync(new IpcRequest("screenshot"));
        Assert.True(shot.Ok, shot.Error);
        Assert.True(File.Exists(shot.Data!["path"]!.GetValue<string>()));

        await app.SaveScreenshotAsync("mirroring.png");

        var stop = await app.SendAsync(new IpcRequest("session-stop"));
        Assert.True(stop.Ok, stop.Error);
        var stopped = await app.WaitForPhaseAsync("stopped", TimeSpan.FromSeconds(15));
        Assert.False(stopped["mirroring"]!.GetValue<bool>());

        await app.QuitAsync();
    }

    [Fact]
    public async Task WithoutTools_ShowsFirstRunSetup()
    {
        using var package = new TestPackage(withFakeTools: false);
        using var app = new AppProcess(package);
        var status = await app.WaitForPhaseAsync("needssetup", StartupTimeout);

        Assert.False(status["mirroring"]!.GetValue<bool>());
        Assert.True(status["onboarding"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("first-run-no-scrcpy.png");
        await app.QuitAsync();
    }

    [Fact]
    public async Task FirstRun_GuidesUntilTheFirstPhoneIsMirrored()
    {
        using var package = new TestPackage(withFakeTools: true);
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        using var app = new AppProcess(package);
        var waiting = await app.WaitForPhaseAsync("waiting", StartupTimeout);
        Assert.True(waiting["onboarding"]!.GetValue<bool>());
        await app.SaveScreenshotAsync("first-run.png");

        package.WriteScenario(new { Devices = new[] { new { Serial = "FAKE123", State = "unauthorized", Model = "Fake Phone" } } });
        await app.WaitForStatusAsync(data => data["onboarding"]!.GetValue<bool>() && data["phase"]!.GetValue<string>() == "waiting"
            && data["message"]!.GetValue<string>().Contains("Allow", StringComparison.Ordinal), StartupTimeout, "the tap-Allow hint");
        await app.SaveScreenshotAsync("first-run-allow.png");

        File.Delete(package.FakeAdbScenario);
        var mirroring = await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.False(mirroring["onboarding"]!.GetValue<bool>());
        await app.QuitAsync();

        // Once a phone has been mirrored the guide stays away, even with nothing connected.
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        using var second = new AppProcess(package);
        Assert.False((await second.WaitForPhaseAsync("waiting", StartupTimeout))["onboarding"]!.GetValue<bool>());
        await second.QuitAsync();
    }

    [Fact]
    public async Task PatternGuide_DiscoversAndroidGeometryAndRenders()
    {
        using var package = new TestPackage(withFakeTools: true);
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        package.WriteScenario(new
        {
            Devices = new[] { new { Serial = "FAKE123", State = "device", Model = "Fake Phone" } },
            KeyguardLocked = true,
            UiHierarchyDelayMs = 1500,
            UiHierarchy = """
                <hierarchy rotation="0"><node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
                  <node class="com.android.internal.widget.LockPatternView" bounds="[140,820][940,1620]" />
                </node></hierarchy>
                """,
        });

        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // While Android is asked where the pattern is, the spinner shows and runs; once the dots
        // are drawn it stops, rather than pulsing forever behind them.
        await app.WaitForStatusAsync(
            data => data["patternGuide"]?["resolving"]?.GetValue<bool>() == true && data["patternGuide"]?["spinning"]?.GetValue<bool>() == true,
            TimeSpan.FromSeconds(8),
            "the spinner while the pattern is found");
        var ready = await app.WaitForStatusAsync(
            data => data["patternGuide"]?["visible"]?.GetValue<bool>() == true &&
                data["patternGuide"]?["resolving"]?.GetValue<bool>() == false &&
                data["patternGuide"]?["source"]?.GetValue<string>() == PatternGeometry.SourceUiView,
            TimeSpan.FromSeconds(8),
            "discovered pattern geometry");
        Assert.False(ready["patternGuide"]!["spinning"]!.GetValue<bool>());
        Assert.Equal(PatternGeometry.SourceUiView, ready["patternGuide"]!["source"]!.GetValue<string>());
        await app.SaveScreenshotAsync("pattern-ready.png");
        await app.DragPatternAndCaptureAsync("pattern-trace.png");
        await app.QuitAsync();
    }

    [Fact]
    public async Task PatternGuide_FollowsAndroidEachTimeTheLockScreenComesBack()
    {
        using var package = new TestPackage(withFakeTools: true);
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        static object Scenario(bool locked, int top) => new
        {
            Devices = new[] { new { Serial = "FAKE123", State = "device", Model = "Fake Phone" } },
            KeyguardLocked = locked,
            UiHierarchy = $"""
                <hierarchy rotation="0"><node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
                  <node class="com.android.internal.widget.LockPatternView" bounds="[140,{top}][940,{top + 800}]" />
                </node></hierarchy>
                """,
        };

        package.WriteScenario(Scenario(locked: true, top: 820));
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var first = await app.WaitForStatusAsync(
            data => data["patternGuide"]?["source"]?.GetValue<string>() == PatternGeometry.SourceUiView,
            TimeSpan.FromSeconds(10),
            "the guide to find the pattern");
        var firstTop = first["patternGuide"]!["bounds"]!["top"]!.GetValue<double>();

        // The phone is unlocked and used, then locks again with the pattern lower down the screen
        // (a notification, a different layout): the guide must move with it, not show where the
        // pattern was last time.
        package.WriteScenario(Scenario(locked: false, top: 820));
        await app.WaitForStatusAsync(data => data["patternGuide"]?["visible"]?.GetValue<bool>() == false, TimeSpan.FromSeconds(10), "the guide to hide on unlock");
        package.WriteScenario(Scenario(locked: true, top: 1220));
        var second = await app.WaitForStatusAsync(
            data => data["patternGuide"]?["bounds"]?["top"]?.GetValue<double>() is { } top && top > firstTop + 0.1,
            TimeSpan.FromSeconds(10),
            "the guide to follow the pattern to its new place");
        Assert.Equal(PatternGeometry.SourceUiView, second["patternGuide"]!["source"]!.GetValue<string>());

        // Opening calibration and saving it untouched keeps the guide automatic rather than
        // freezing today's placement; moving it first is what saves a calibration.
        await app.PressCtrlAltKeyAsync((byte)'C');
        await app.PressKeyAsync(0x0D);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Null(new StateStore(package.Paths.State).GetDevice("FAKE123")!.Calibration);
        var automatic = await app.WaitForStatusAsync(
            data => data["patternGuide"]?["source"]?.GetValue<string>() == PatternGeometry.SourceUiView,
            TimeSpan.FromSeconds(5),
            "the guide to stay automatic");
        Assert.True(automatic["patternGuide"]!["visible"]!.GetValue<bool>());

        await app.PressCtrlAltKeyAsync((byte)'C');
        await app.PressKeyAsync(0x28);
        await app.PressKeyAsync(0x28);
        await app.PressKeyAsync(0x0D);
        await app.WaitForStatusAsync(
            data => data["patternGuide"]?["source"]?.GetValue<string>() == PatternGeometry.SourceCalibration,
            TimeSpan.FromSeconds(5),
            "a moved calibration to take over");
        var calibration = new StateStore(package.Paths.State).GetDevice("FAKE123")!.Calibration;
        Assert.NotNull(calibration);
        Assert.False(calibration.Landscape);
        await app.QuitAsync();
    }

    [Fact]
    public async Task PhoneThatReenumerates_IsPickedUpAgainWithoutPressingStart()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // The phone drops off USB (a Samsung changes USB mode on unlock) and scrcpy dies with it.
        package.WriteScenario(new { Devices = Array.Empty<object>() });
        await app.WaitForStatusAsync(data => data["visibleDevices"]!.GetValue<int>() == 0, TimeSpan.FromSeconds(10), "ADB to lose the phone");
        await app.KillMirrorAsync();
        var waiting = await app.WaitForPhaseAsync("waiting", StartupTimeout);
        Assert.Contains("disconnected", waiting["message"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);

        File.Delete(package.FakeAdbScenario);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.Equal(2, package.ScrcpyLog().Count(line => line.StartsWith("args ", StringComparison.Ordinal)));
        await app.QuitAsync();
    }

    [Fact]
    public async Task SecondInstance_HandsOverToTheFirst()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        using var second = app.StartAnother();
        Assert.True(await Task.Run(() => second.WaitForExit(15000), TestContext.Current.CancellationToken));
        Assert.Equal(0, second.ExitCode);
        Assert.True((await app.SendAsync(new IpcRequest("ping"))).Ok);

        await app.QuitAsync();
    }

    [Fact]
    public async Task Pipe_AcceptsConcurrentClientsAndSurvivesMalformedRequest()
    {
        using var package = new TestPackage(withFakeTools: false);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("needssetup", StartupTimeout);

        var replies = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => app.SendAsync(new IpcRequest("ping"))));
        Assert.All(replies, reply => Assert.True(reply.Ok, reply.Error));
        var malformed = await app.SendRawAsync("{\"command\":42}");
        Assert.False(malformed.Ok);

        var unsupported = await app.SendRawAsync("""{"v":1,"command":"ping","args":{}}""");
        Assert.False(unsupported.Ok);
        Assert.Contains("Unsupported protocol version", unsupported.Error);

        var oversized = await app.SendRawAsync(new string('x', Rex.Mirror.Services.PipeServer.MaxRequestBytes + 1));
        Assert.False(oversized.Ok);
        Assert.Contains("byte limit", oversized.Error);

        var stalled = await app.SendPartialAsync("{\"v\":2,\"command\":\"ping\"");
        Assert.False(stalled.Ok);
        Assert.Contains("timed out", stalled.Error, StringComparison.OrdinalIgnoreCase);

        Assert.True((await app.SendAsync(new IpcRequest("ping"))).Ok);
        await app.QuitAsync();
    }

    [Fact]
    public async Task ForceKillingRex_CleansOwnedScrcpyProcessTree()
    {
        using var package = new TestPackage(withFakeTools: true, configure: config =>
            config.Mirror.ExtraArgs = "--rex-spawn-child");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitUntilAsync(
            () => package.ScrcpyLog().Any(line => line.StartsWith("child-pid ", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "fake scrcpy child process");

        var processIds = package.ScrcpyLog()
            .Where(line => line.StartsWith("pid ", StringComparison.Ordinal) || line.StartsWith("child-pid ", StringComparison.Ordinal))
            .Select(line => int.Parse(line[(line.LastIndexOf(' ') + 1)..]))
            .Distinct()
            .ToArray();
        Assert.True(processIds.Length >= 2, "Expected fake scrcpy and its child process.");

        await app.KillAppOnlyAsync();

        foreach (var processId in processIds)
        {
            await AppProcess.WaitForProcessExitAsync(processId, TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 5)]
    public async Task UnexpectedExit_RespectsRestartPreferenceAndLimit(bool restart, int launches)
    {
        using var package = new TestPackage(withFakeTools: true, configure: config =>
        {
            config.Session.RestartOnUnexpectedExit = restart;
            config.Session.RetrySeconds = 1;
        });
        using var app = new AppProcess(package);
        for (var i = 0; i < launches; i++)
        {
            await app.WaitForPhaseAsync("mirroring", StartupTimeout);
            await app.KillMirrorAsync();
            await app.WaitForPhaseAsync(i == launches - 1 ? "stopped" : "waiting", StartupTimeout);
        }

        await Task.Delay(2500, TestContext.Current.CancellationToken);
        Assert.Equal(launches, package.ScrcpyLog().Count(line => line.StartsWith("args ", StringComparison.Ordinal)));
        // An explicit restart must still work when automatic restart is disabled or exhausted.
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForPhaseAsync("waiting", StartupTimeout);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.QuitAsync();
    }

    [Fact]
    public async Task Fullscreen_FillsMonitorHidesHudRotatesAndRestoresWindow()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        var windowed = app.WindowBounds();
        await app.SendAsync(new IpcRequest("zoom", new Dictionary<string, string> { ["direction"] = "in" }));
        await app.PressKeyAsync(0x7A, repeat: true); // F11: holding it must only toggle once.
        var status = await app.WaitForStatusAsync(
            data => data["fullscreen"]!.GetValue<bool>() && data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5),
            "fullscreen HUD");
        Assert.True(status["fullscreen"]!.GetValue<bool>());
        Assert.Equal(1, status["zoom"]!.GetValue<double>());
        Assert.True(status["hudVisible"]!.GetValue<bool>());
        Assert.Equal(app.MonitorBounds(), app.WindowBounds());
        await app.SaveScreenshotAsync("fullscreen-hud.png");

        app.MovePointerToCenter();
        await app.WaitForStatusAsync(
            data => !data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5),
            "HUD auto-hide");
        await app.SaveScreenshotAsync("fullscreen-clean.png");
        app.MovePointerToTop();
        await app.WaitForStatusAsync(
            data => data["hudVisible"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(3),
            "HUD reveal");

        await app.ActionAsync("rotation-landscape");
        await app.WaitUntilAsync(
            () => package.AdbCalls().Any(line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "landscape rotation command");
        await app.SaveScreenshotAsync("fullscreen-rotation.png");
        await app.ActionAsync("rotation-portrait");
        await app.ActionAsync("rotation-auto");
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 1", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system user_rotation 0", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), line => line.Contains("settings put system accelerometer_rotation 1", StringComparison.Ordinal));
        await app.PressKeyAsync(0x1B); // Escape returns to the previous window bounds.
        await app.WaitForStatusAsync(
            data => !data["fullscreen"]!.GetValue<bool>(),
            TimeSpan.FromSeconds(5),
            "windowed mode after Escape");
        await app.WaitUntilAsync(
            () => app.WindowBounds() == windowed,
            TimeSpan.FromSeconds(5),
            "window bounds to restore after leaving fullscreen");
        Assert.Equal(windowed, app.WindowBounds());

        await app.ActionAsync("fullscreen");
        await app.SendAsync(new IpcRequest("session-stop"));
        await app.WaitForPhaseAsync("stopped", StartupTimeout);
        Assert.False((await app.SendAsync(new IpcRequest("status"))).Data!["fullscreen"]!.GetValue<bool>());
        await app.QuitAsync();
    }
}

[CollectionDefinition("desktop", DisableParallelization = true)]
public sealed class DesktopCollection;
