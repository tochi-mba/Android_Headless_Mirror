using System.Windows.Automation;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Not a test so much as a camera: walks every tab of the side panel with every group open and
/// saves a screenshot of each scroll page, at the default window size and at the smallest, into
/// artifacts/screens/audit. It exists so the whole panel can be looked over after a change instead
/// of the one screen somebody happened to open. It only runs when REX_UI_AUDIT=1, because it takes
/// a minute and judges nothing by itself.
/// </summary>
[Collection("desktop")]
public sealed class UiAuditCapture
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(45);

    private static bool Enabled => Environment.GetEnvironmentVariable("REX_UI_AUDIT") == "1";

    [Fact(Timeout = 240_000)]
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
            var scale = app.DpiScale();
            app.MoveWindow(monitor.Left + 40, monitor.Top + 40, (int)(width * scale), (int)(height * scale));
            await Task.Delay(600, TestContext.Current.CancellationToken);
            // A narrow window hides the panel to give the phone the room; the audit wants it open.
            var status = await app.SendAsync(new IpcRequest("status"));
            if (status.Data?["sidebarVisible"]?.GetValue<bool>() != true)
            {
                app.Ui.Invoke("SidebarToggle");
                await Task.Delay(600, TestContext.Current.CancellationToken);
            }

            foreach (var tab in new[] { "TabControls", "TabPhone", "TabSettings", "TabInfo" })
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
    /// Saves one screenshot per screenful of the panel. The panel is scrolled with the wheel, the
    /// way a person does, and the offset is read back from the app: the scroll viewer is not in UI
    /// Automation's control view, so it cannot be driven from there.
    /// </summary>
    private static async Task CapturePagesAsync(AppProcess app, string prefix)
    {
        for (var attempt = 0; attempt < 30 && await OffsetAsync(app) > 0.5; attempt++)
        {
            await app.ScrollSidebarAsync(10);
        }

        Note(prefix, $"top offset={await OffsetAsync(app):0}");
        for (var page = 0; page < 40; page++)
        {
            await Task.Delay(300, TestContext.Current.CancellationToken);
            using (var shot = await app.CaptureWindowAsync())
            {
                shot.Save($"{prefix}-{page:00}.png", System.Drawing.Imaging.ImageFormat.Png);
            }

            var before = await OffsetAsync(app);
            await app.ScrollSidebarAsync(-4);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            var after = await OffsetAsync(app);
            Note(prefix, $"page {page} offset {before:0} -> {after:0}");
            if (after - before < 1)
            {
                return;
            }
        }
    }
}
