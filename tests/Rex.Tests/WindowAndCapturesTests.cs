using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The window and captures your way: how screenshots and recordings are named and never
/// overwritten, the window size that fits the phone, the command log for a bug report (which
/// never says where the screen was touched), and how the new values are kept sane.
/// </summary>
public sealed class WindowAndCapturesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime At = new(2026, 10, 5, 14, 3, 9);

    // ----- Names -----

    [Theory]
    [InlineData("android-{date}-{time}", null)]
    [InlineData("{phone} {date} {n}", null)]
    [InlineData("{MODEL}-{Time}", null)]
    [InlineData("screenshot", null)]
    [InlineData("", "A name is needed, such as android-{date}-{time}.")]
    [InlineData(null, "A name is needed, such as android-{date}-{time}.")]
    [InlineData("a/b", "A file name cannot hold \\ / : * ? \" < > | or control characters.")]
    [InlineData("a:b", "A file name cannot hold \\ / : * ? \" < > | or control characters.")]
    [InlineData("{day}", "{day} is not one of {date}, {time}, {phone}, {model}, {n}.")]
    [InlineData("{date", "Braces only go around {date}, {time}, {phone}, {model}, {n}.")]
    [InlineData("date}", "Braces only go around {date}, {time}, {phone}, {model}, {n}.")]
    [InlineData("shot.", "A file name cannot end with a dot.")]
    [InlineData("nul", "Windows keeps that name for itself.")]
    public void ANameTemplateIsCheckedBeforeAnythingIsSaved(string? template, string? why) => Assert.Equal(why, CaptureName.WhyNot(template));

    [Fact]
    public void ANameTemplateHasALimit() =>
        Assert.Equal("A name can be at most 100 characters.", CaptureName.WhyNot(new string('a', CaptureName.MaxLength + 1)));

    [Fact]
    public void TheTokensBecomeTheMomentAndThePhone()
    {
        Assert.Equal("android-20261005-140309", CaptureName.Format(CaptureName.Default, At, "Galaxy S21 Ultra", "SM-G998B", 1));
        Assert.Equal("Galaxy S21 Ultra SM-G998B 7", CaptureName.Format("{phone} {MODEL} {n}", At, "Galaxy S21 Ultra", "SM-G998B", 7));
        // Whatever a file name cannot hold in a phone's name becomes a dash; nothing at all becomes "phone".
        Assert.Equal("My-Phone- 1", CaptureName.Format("{phone} {n}", At, "My/Phone:", "", 1));
        Assert.Equal("phone", CaptureName.Format("{model}", At, "", "...", 1));
    }

    [Fact]
    public void ANameThatIsTakenIsNeverOverwritten()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shot.png", "shot (2).png" };
        Assert.Equal("shot (3).png", CaptureName.Unique("shot", At, "", "", ".png", taken.Contains));
        Assert.Equal("fresh.png", CaptureName.Unique("fresh", At, "", "", ".png", taken.Contains));

        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shot-1.png", "shot-2.png" };
        Assert.Equal("shot-3.png", CaptureName.Unique("shot-{n}", At, "", "", ".png", counted.Contains));
    }

    [Fact]
    public void RecordingsAreNamedAsScreenshotsAre()
    {
        Assert.Equal("android-20261005-140309.mp4", ScrcpyArguments.RecordingFileName("mp4", At));
        Assert.Equal("S21 20261005.mkv", ScrcpyArguments.RecordingFileName("mkv", At, "{phone} {date}", "S21", "SM-G998B"));
        Assert.Equal("S21 (2).mp4", ScrcpyArguments.RecordingFileName("webm", At, "{phone}", "S21", "", name => name == "S21.mp4"));
    }

    [Fact]
    public void ANewNameNeverAsksForARestart()
    {
        var config = new RexConfig();
        config.Mirror.RecordOnStart = true;
        var before = ScrcpyArguments.LaunchSettings(config, isTcp: false);
        config.App.CaptureNames = "{phone}-{n}";
        Assert.Equal(before, ScrcpyArguments.LaunchSettings(config, isTcp: false));
        Assert.False(SettingsCatalogue.AppliesAtNextStart("App.CaptureNames"));
    }

    [Fact]
    public void ConfigSetRefusesANameThatCannotNameFiles()
    {
        var refused = Assert.Throws<FormatException>(() => ConfigValidation.Check("App.CaptureNames", "a|b", new RexConfig()));
        Assert.Contains("cannot hold", refused.Message, StringComparison.Ordinal);
        ConfigValidation.Check("App.CaptureNames", "{phone}-{n}", new RexConfig());
    }

    // ----- Fitting the window -----

    [Fact]
    public void TheWindowKeepsItsHeightAndTakesThePhonesWidth()
    {
        // A window of 1180 x 780 whose mirror area is 820 x 700: 360 to the side, 80 above and below.
        var size = WindowFit.Size((1180, 780), (820, 700), [0.45], 0, (3000, 2000), (300, 200));
        Assert.Equal((360 + 315, 780), size);
    }

    [Fact]
    public void SeveralViewsFitSideBySideWithTheirGaps()
    {
        var size = WindowFit.Size((1180, 780), (820, 700), [0.5, 0.5, 0.5], 12, (3000, 2000), (720, 480));
        Assert.Equal((360 + 24 + 1050, 780), size);
    }

    [Fact]
    public void ATooWideFitIsAsWideAsTheScreenAndItsHeightFollows()
    {
        // A landscape picture: 700 tall would need 1244 wide; the screen gives 1200.
        var size = WindowFit.Size((1180, 780), (820, 700), [16.0 / 9], 0, (1200, 1000), (720, 480));
        Assert.Equal((1200, Math.Round(80 + (1200 - 360) / (16.0 / 9))), size);
    }

    [Fact]
    public void ATooTallFitIsAsTallAsTheScreenAndItsWidthFollows()
    {
        // An upright phone in a window taller than the screen it is on now.
        var size = WindowFit.Size((1180, 780), (820, 700), [0.45], 0, (1200, 600), (300, 200));
        Assert.Equal((Math.Round(360 + (600 - 80) * 0.45), 600), size);
    }

    [Fact]
    public void AWindowThatCannotBeNarrowerGrowsTallerInstead()
    {
        // 384 wide would fit; the window is never narrower than 720, so it grows to 80 + 420 / 0.2 tall.
        Assert.Equal((720, 2180), WindowFit.Size((800, 500), (500, 420), [0.2], 0, (3000, 2400), (720, 480)));
        // As tall as the screen lets it be, and never shorter than its smallest height.
        Assert.Equal((720, 2000), WindowFit.Size((800, 500), (500, 420), [0.2], 0, (3000, 2000), (720, 480)));
        Assert.Equal((720, 480), WindowFit.Size((800, 200), (600, 100), [3.0], 0, (3000, 2000), (720, 480)));
    }

    [Fact]
    public void WithNothingToFitTheWindowStaysAsItIs()
    {
        Assert.Equal((1180.0, 780.0), WindowFit.Size((1180, 780), (820, 700), [], 0, (3000, 2000), (720, 480)));
        Assert.Equal((1180.0, 780.0), WindowFit.Size((1180, 780), (0, 700), [0.45], 0, (3000, 2000), (720, 480)));
        Assert.Equal((1180.0, 780.0), WindowFit.Size((1180, 780), (820, 0), [0.45], 0, (3000, 2000), (720, 480)));
        Assert.Equal((1180.0, 780.0), WindowFit.Size((1180, 780), (820, 700), [double.NaN, -1], 0, (3000, 2000), (720, 480)));
    }

    [Fact]
    public void FittingTheWindowIsAnActionWithAKeyOfItsOwn()
    {
        Assert.Equal(ActionKind.App, MirrorActions.Find("fit-window")!.Kind);
        Assert.Equal("Ctrl+Alt+F", Shortcuts.Gesture("fit-window"));
        Assert.Equal("fit-window", WindowKeys.ActionFor(0x46, KeyMods.Ctrl | KeyMods.Alt));
    }

    // ----- The command log -----

    [Fact]
    public async Task WithVerboseOffNothingIsWritten()
    {
        var lines = new List<string>();
        var runner = new LoggedProcessRunner(new FakeProcessRunner(), lines.Add, () => false);
        await runner.RunAsync("adb.exe", ["devices"], cancellationToken: Ct);
        await runner.RunBytesAsync("adb.exe", ["exec-out", "screencap", "-p"], cancellationToken: Ct);
        await runner.RunDetachedAsync("adb.exe", ["start-server"], cancellationToken: Ct);
        Assert.Empty(lines);
    }

    [Fact]
    public async Task WithVerboseOnEveryCommandIsWrittenWithHowItEnded()
    {
        var lines = new List<string>();
        var fake = new FakeProcessRunner { Respond = args => new ProcessResult(args[0] == "slow" ? 0 : 1, "", "", TimedOut: args[0] == "slow") };
        var runner = new LoggedProcessRunner(fake, lines.Add, () => true);
        Assert.Equal(1, (await runner.RunAsync("C:\\tools\\adb.exe", ["-s", "FAKE123", "shell", "getprop"], cancellationToken: Ct)).ExitCode);
        await runner.RunAsync("adb", ["slow"], cancellationToken: Ct);
        await runner.RunBytesAsync("adb.exe", ["exec-out", "screencap", "-p"], cancellationToken: Ct);
        Assert.Equal(0, await runner.RunDetachedAsync("adb.exe", ["start-server"], cancellationToken: Ct));
        Assert.Equal(4, fake.Calls.Count + fake.Detached.Count);
        Assert.Matches(@"^Ran adb -s FAKE123 shell getprop · exit 1 · \d+ ms$", lines[0]);
        Assert.Matches(@"^Ran adb slow · timed out · \d+ ms$", lines[1]);
        Assert.StartsWith("Ran adb exec-out screencap -p · exit 0", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("Ran adb start-server · exit 0", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void WhereTheScreenWasTouchedNeverReachesTheLog()
    {
        Assert.Equal("adb -s S shell input (where the screen was touched is left out)",
            LoggedProcessRunner.Describe("adb.exe", ["-s", "S", "shell", "input", "swipe", "100", "200", "300", "400", "250"]));
        Assert.Equal("adb shell settings put system \"font scale\" \"\"",
            LoggedProcessRunner.Describe("adb", ["shell", "settings", "put", "system", "font scale", ""]));
    }

    // ----- Keeping the new values sane -----

    [Fact]
    public void TheNewValuesAreKeptSane()
    {
        var config = new RexConfig();
        config.App.PanelScale = 3;
        config.App.CaptureNames = "a/b";
        config.Zoom.KeyStep = 0.01;
        config.Ambient.WhenZoomed = "sometimes";
        config.PatternGuide.DotSize = 9;
        config.Normalize();
        Assert.Equal(AppSettings.LargestPanelScale, config.App.PanelScale);
        Assert.Equal(CaptureName.Default, config.App.CaptureNames);
        Assert.Equal(0.1, config.Zoom.KeyStep);
        Assert.Equal("show", config.Ambient.WhenZoomed);
        Assert.Equal(1.6, config.PatternGuide.DotSize);

        config.App.PanelScale = double.NaN;
        config.App.CaptureNames = "  {phone}-{n} ";
        config.Zoom.KeyStep = double.PositiveInfinity;
        config.Ambient.WhenZoomed = "HIDE";
        config.PatternGuide.DotSize = double.NaN;
        config.Normalize();
        Assert.Equal(1.0, config.App.PanelScale);
        Assert.Equal("{phone}-{n}", config.App.CaptureNames);
        Assert.Equal(0.25, config.Zoom.KeyStep);
        Assert.Equal("hide", config.Ambient.WhenZoomed);
        Assert.Equal(1.0, config.PatternGuide.DotSize);

        config.App.PanelScale = 0.1;
        config.PatternGuide.DotSize = 0.1;
        config.Zoom.KeyStep = 5;
        config.Normalize();
        Assert.Equal(AppSettings.SmallestPanelScale, config.App.PanelScale);
        Assert.Equal(0.6, config.PatternGuide.DotSize);
        Assert.Equal(1, config.Zoom.KeyStep);
    }

    [Fact]
    public void TheShippedValuesKeepTheWindowAsItWas()
    {
        var app = new RexConfig().App;
        Assert.True(app.ShowTopBar && app.ShowStatusBar && app.RememberPlacement);
        Assert.False(app.FitWindowOnStart || app.ConfirmQuit || app.NotifyMirrorStops || app.ScreenshotFlash || app.OpenScreenshots);
        Assert.Equal(1.0, app.PanelScale);
        Assert.False(new RexConfig().Logging.Verbose);
    }
}
