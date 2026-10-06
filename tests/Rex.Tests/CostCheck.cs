using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// What the app costs, measured the same way on every run so a change can be compared: how long it
/// takes to start mirroring, then its CPU, memory, handles and threads while mirroring, zoomed in,
/// with Settings open, hidden in the tray, shown again, and mirroring without the soft background
/// (on a runner with no graphics card the live pictures are the processor's work, so this last
/// state is the app's own cost with that set apart). The numbers are checked against the
/// committed tests/perf-baseline.json with its own tolerance, and written to artifacts/perf as a
/// file that can replace the baseline as it is. CI runs it on every pull request; locally it only
/// runs when REX_PERF=1, and its numbers are this PC's, not the runner's.
/// </summary>
[Collection("desktop")]
public sealed class CostCheck
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private static IpcRequest Zoom(string direction) => new("zoom", new Dictionary<string, string> { ["direction"] = direction });

    private static bool Enabled => Environment.GetEnvironmentVariable("REX_PERF") == "1";

    public static string BaselineFile => Path.Combine(RepoPaths.Root, "tests", "perf-baseline.json");

    /// <summary>The states measured, in the order they are visited.</summary>
    public static readonly string[] States = ["mirroring", "zoomed", "settings", "hidden", "shown-again", "plain"];

    [Fact(Timeout = 300_000)]
    public async Task TheAppCostsNoMoreThanItsBaseline()
    {
        Assert.SkipUnless(Enabled, "Set REX_PERF=1 to measure what the app costs.");
        using var package = new TestPackage(withFakeTools: true);
        new StateStore(package.Paths.State).SetLockScreenMode("FAKE123", LockScreenModes.Pattern);
        var store = new StateStore(package.Paths.State);
        store.SetUi(store.Ui with { TipsSeen = [.. Tips.All.Select(t => t.Id)], TourSeenVersion = 99 });
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal) { ["start.ms"] = app.MillisecondsSinceStart() };

        // Each state is given time to settle before it is measured; the settling is what is waited for.
        async Task Measure(string state)
        {
            await Task.Delay(3000, TestContext.Current.CancellationToken);
            foreach (var (metric, value) in await app.MeasureCostAsync(state))
            {
                metrics[metric] = value;
            }
        }

        app.MovePointerToCorner();
        await Measure("mirroring");
        // What the numbers depend on, said beside them: how the live pictures are being made.
        var shown = (await app.SendAsync(new IpcRequest("status"))).Data!;
        var how = $"Capture: {shown["capture"]}. Without a graphics card: {shown["window"]!["withoutGraphicsCard"]}. " +
                  $"Live pictures: {shown["window"]!["livePictureRate"]} frames a second.";

        Assert.True((await app.SendAsync(Zoom("in"))).Ok);
        Assert.True((await app.SendAsync(Zoom("in"))).Ok);
        await Measure("zoomed");
        Assert.True((await app.SendAsync(Zoom("reset"))).Ok);

        app.Ui.Select("TabSettings");
        await Measure("settings");
        app.Ui.Select("TabControls");

        Assert.True((await app.SendAsync(new IpcRequest("hide"))).Ok);
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>(), Soon, "the window to hide");
        await Measure("hidden");

        Assert.True((await app.SendAsync(new IpcRequest("show"))).Ok);
        await app.WaitForStatusAsync(s => s["windowVisible"]!.GetValue<bool>(), Soon, "the window to show");
        await Measure("shown-again");

        new ConfigStore(package.Paths.Config).Set("Ambient.Enabled", "false");
        await app.WaitForStatusAsync(s => !s["ambientVisible"]!.GetValue<bool>(), Soon, "the soft background to go");
        await Measure("plain");
        await app.QuitAsync();

        var (tolerance, baseline) = File.Exists(BaselineFile)
            ? CostBaseline.Read(await File.ReadAllTextAsync(BaselineFile, TestContext.Current.CancellationToken))
            : (new CostTolerance(), new Dictionary<string, double>());
        var lines = CostBaseline.Compare(baseline, metrics, tolerance);
        var report = "## What the app costs\n\n" + how + "\n\n" + CostBaseline.Markdown(lines);

        var folder = Path.Combine(RepoPaths.Root, "artifacts", "perf");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "perf-baseline.json"),
            CostBaseline.Write(tolerance, metrics, About), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "report.md"), report, TestContext.Current.CancellationToken);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
        {
            await File.AppendAllTextAsync(summary, report, TestContext.Current.CancellationToken);
        }

        Assert.All(States, state => Assert.Contains(state + ".cpu", metrics.Keys));
        var over = lines.Where(l => l.Verdict is CostVerdict.Over or CostVerdict.Missing).ToArray();
        Assert.True(over.Length == 0, "The app costs more than its baseline:\n" + CostBaseline.Markdown(over));
    }

    private const string About =
        "What the app costs on CI's Windows runner with the fake phone, from CostCheck. CPU is percent of one " +
        "core, memory in megabytes. A run fails when a number rises above baseline x factor + allowance. To " +
        "move the baseline, copy perf-baseline.json from the run's desktop-ui-perf artefact over this file.";
}
