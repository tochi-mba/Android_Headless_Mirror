using System.Windows.Automation;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Not a test so much as a camera: walks every tab of the side panel with every group open, scrolls
/// it a screenful at a time, and stitches the screenfuls into one tall picture per tab and window
/// size (the default and the smallest) in artifacts/screens/audit. It exists so the whole panel can
/// be looked over after a change instead of the one screen somebody happened to open. CI runs it on
/// every pull request; locally it only runs when REX_UI_AUDIT=1, because it takes minutes and
/// judges nothing by itself.
/// </summary>
[Collection("desktop")]
public sealed class UiAuditCapture
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(45);

    private static bool Enabled => Environment.GetEnvironmentVariable("REX_UI_AUDIT") == "1";

    [Fact(Timeout = 600_000)]
    public async Task EveryPageOfTheSidePanel()
    {
        Assert.SkipUnless(Enabled, "Set REX_UI_AUDIT=1 to capture the side panel for review.");
        using var package = new TestPackage(withFakeTools: true);
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { TipsSeen = [.. Tips.All.Select(t => t.Id)], TourSeenVersion = 99 });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        var folder = Path.Combine(RepoPaths.Screens, "audit");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.png"))
        {
            File.Delete(old);
        }

        var monitor = app.MonitorBounds();
        foreach (var (label, width, height) in new[] { ("default", 1180, 780), ("smallest", 720, 480) })
        {
            // Never larger than the screen: a window hanging off its edge photographs as blank there
            // (the CI runner's screen is 1024 pixels wide).
            var scale = app.DpiScale();
            var pixelsWide = Math.Min((int)(width * scale), monitor.Width - 80);
            var pixelsHigh = Math.Min((int)(height * scale), monitor.Height - 80);
            app.MoveWindow(monitor.Left + 40, monitor.Top + 40, pixelsWide, pixelsHigh);
            await Task.Delay(600, TestContext.Current.CancellationToken);
            // A narrow window hides the panel to give the phone the room; the audit wants it open.
            var status = await app.SendAsync(new IpcRequest("status"));
            if (status.Data?["sidebarVisible"]?.GetValue<bool>() != true)
            {
                app.Ui.Invoke("SidebarToggle");
                await Task.Delay(600, TestContext.Current.CancellationToken);
            }

            foreach (var tab in new[] { "TabControls", "TabApps", "TabPhone", "TabSettings", "TabInfo" })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                app.Ui.Select(tab);
                Note(Path.Combine(folder, label), $"selected {tab} in {watch.ElapsedMilliseconds} ms");
                await Task.Delay(700, TestContext.Current.CancellationToken);
                ExpandEverything(app);
                await Task.Delay(500, TestContext.Current.CancellationToken);
                await CapturePagesAsync(app, Path.Combine(folder, $"{label}-{tab[3..].ToLowerInvariant()}"));
            }
        }

        await app.QuitAsync();
    }

    /// <summary>Opens every expander in the window; combo boxes also expand, so they are left alone.</summary>
    private static void ExpandEverything(AppProcess app)
    {
        // Expanders only. Combo boxes and menus expand too, and the title bar's system menu is one
        // of them: opening it drops the window into a modal menu loop.
        var root = AutomationElement.FromHandle(app.MainWindowHandle());
        var expandable = root.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.IsExpandCollapsePatternAvailableProperty, true),
            new PropertyCondition(AutomationElement.ClassNameProperty, "Expander")));
        foreach (AutomationElement element in expandable)
        {
            try
            {
                if (element.GetCurrentPattern(ExpandCollapsePattern.Pattern) is ExpandCollapsePattern pattern &&
                    pattern.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                {
                    pattern.Expand();
                }
            }
            catch (InvalidOperationException)
            {
                // Off screen or gone since the search; the next page catches it.
            }
            catch (ElementNotAvailableException)
            {
            }
        }
    }

    private static void Note(string prefix, string text) =>
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(prefix)!, "audit-log.txt"), $"{DateTime.Now:HH:mm:ss.fff} {Path.GetFileName(prefix)}: {text}{Environment.NewLine}");

    private static async Task<double> OffsetAsync(AppProcess app) =>
        (await app.SendAsync(new IpcRequest("status"))).Data?["sidebarScrollOffset"]?.GetValue<double>() ?? 0;

    /// <summary>
    /// Photographs the panel a screenful at a time and stitches the screenfuls into one tall
    /// picture: the tab strip from the first, then each screenful's scrolling area placed at its
    /// own offset. The panel is scrolled with the wheel, the way a person does, and the offset is
    /// read back from the app: the scroll viewer is not in UI Automation's control view.
    /// </summary>
    private static async Task CapturePagesAsync(AppProcess app, string prefix)
    {
        // Right to the top: a tab can remember where it was, and Settings with every group open is long.
        for (var attempt = 0; attempt < 200 && await OffsetAsync(app) > 0.5; attempt++)
        {
            await app.ScrollSidebarAsync(20);
        }

        var scale = app.DpiScale();
        var pages = new List<(double Offset, System.Drawing.Bitmap Shot)>();
        try
        {
            for (var page = 0; page < 60; page++)
            {
                // The pointer off the panel, so no row is photographed under it, hovered.
                var window = app.WindowBounds();
                app.MovePointerTo(window.Left + 40, window.Top + window.Height / 2);
                await Task.Delay(300, TestContext.Current.CancellationToken);
                pages.Add((await OffsetAsync(app), await app.CaptureWindowAsync()));
                var before = pages[^1].Offset;
                await app.ScrollSidebarAsync(-4);
                await Task.Delay(250, TestContext.Current.CancellationToken);
                var after = await OffsetAsync(app);
                Note(prefix, $"page {page} offset {before:0} -> {after:0}");
                if (after - before < 1)
                {
                    break;
                }
            }

            var status = (await app.SendAsync(new IpcRequest("status"))).Data!;
            var bounds = app.WindowBounds();
            var viewport = status["sidebarViewport"]!;
            var band = new System.Drawing.Rectangle(
                viewport["left"]!.GetValue<int>() - bounds.Left,
                viewport["top"]!.GetValue<int>() - bounds.Top,
                viewport["width"]!.GetValue<int>(),
                viewport["height"]!.GetValue<int>());
            using var stitched = Stitch(pages, band, scale);
            stitched.Save($"{prefix}.png", System.Drawing.Imaging.ImageFormat.Png);
            Note(prefix, $"stitched {pages.Count} screenfuls into {stitched.Width}x{stitched.Height}");
        }
        finally
        {
            foreach (var (_, shot) in pages)
            {
                shot.Dispose();
            }
        }
    }

    /// <summary>The scrolling area's own padding (MainWindow.xaml), which stays put while its content scrolls.</summary>
    private const double PaddingTop = 4, PaddingBottom = 16;

    /// <summary>
    /// One tall picture of the panel: everything above the scrolling area from the first screenful
    /// (the tab strip), then each screenful's scrolling area at its offset, later ones on top. The
    /// area's padding is left out of every screenful but the first: it does not scroll, so drawn
    /// in it would cover the content at each seam.
    /// </summary>
    internal static System.Drawing.Bitmap Stitch(IReadOnlyList<(double Offset, System.Drawing.Bitmap Shot)> pages, System.Drawing.Rectangle band, double scale)
    {
        var top = Math.Max(0, band.Top - 60);
        var header = band.Top - top;
        var last = pages.Count == 0 ? 0 : (int)Math.Round(pages[^1].Offset * scale);
        var padTop = (int)Math.Round(PaddingTop * scale);
        var padBottom = (int)Math.Round(PaddingBottom * scale);
        var inner = new System.Drawing.Rectangle(band.Left, band.Top + padTop, band.Width, Math.Max(1, band.Height - padTop - padBottom));
        var result = new System.Drawing.Bitmap(band.Width, header + last + band.Height);
        using var graphics = System.Drawing.Graphics.FromImage(result);
        graphics.Clear(System.Drawing.Color.Black);
        if (pages.Count > 0)
        {
            graphics.DrawImage(pages[0].Shot, new System.Drawing.Rectangle(0, 0, band.Width, header),
                new System.Drawing.Rectangle(band.Left, top, band.Width, header), System.Drawing.GraphicsUnit.Pixel);
        }

        foreach (var (offset, shot) in pages)
        {
            var y = header + padTop + (int)Math.Round(offset * scale);
            graphics.DrawImage(shot, new System.Drawing.Rectangle(0, y, inner.Width, inner.Height), inner, System.Drawing.GraphicsUnit.Pixel);
        }

        return result;
    }
}
