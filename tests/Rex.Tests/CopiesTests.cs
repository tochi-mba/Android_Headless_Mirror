using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// Copies of the phone, without a phone: where they go and how many fit, the order they are
/// started and stopped in, what the Controls tab says about them, and how each copy's scrcpy
/// session is kept from disturbing the main one.
/// </summary>
public sealed class CopiesTests
{
    private const double Upright = 1080.0 / 2400.0;
    private const double OnItsSide = 2400.0 / 1080.0;

    // ----- Layout -----

    [Fact]
    public void Capacity_IsHowManyUprightPhonesFitSideBySideAtFullHeight()
    {
        // 700 tall at 0.45 is 315 wide; with a 12 gap, 1000 holds three (3*315 + 2*12 = 969).
        Assert.Equal(3, CopiesLayout.Capacity(1000, 700, Upright, 12, most: 6));
        Assert.Equal(2, CopiesLayout.Capacity(1000, 700, Upright, 12, most: 2));
        Assert.Equal(2, CopiesLayout.Capacity(960, 700, Upright, 12, most: 6));
        Assert.Equal(1, CopiesLayout.Capacity(300, 700, Upright, 12, most: 6));

        // A phone on its side, or no room at all, gets its own view and nothing more.
        Assert.Equal(1, CopiesLayout.Capacity(4000, 700, OnItsSide, 12, most: 6));
        Assert.Equal(1, CopiesLayout.Capacity(0, 700, Upright, 12, most: 6));
        Assert.Equal(1, CopiesLayout.Capacity(1000, 700, Upright, 12, most: 0));
        Assert.True(CopiesLayout.IsUpright(Upright));
        Assert.False(CopiesLayout.IsUpright(OnItsSide));
        Assert.False(CopiesLayout.IsUpright(1));
        Assert.False(CopiesLayout.IsUpright(0));
    }

    [Fact]
    public void Cells_AreEqualCentredAndTheGapApart()
    {
        var cells = CopiesLayout.Cells(1000, 700, Upright, 3, 12);
        Assert.Equal(3, cells.Count);
        Assert.All(cells, cell =>
        {
            Assert.Equal(700, cell.Height, 3);
            Assert.Equal(315, cell.Width, 3);
            Assert.Equal(0, cell.Y, 3);
        });
        Assert.Equal(12, cells[1].X - cells[0].Right, 3);
        Assert.Equal(12, cells[2].X - cells[1].Right, 3);
        // Centred: the same space is left on either side.
        Assert.Equal(cells[0].X, 1000 - cells[2].Right, 3);

        // One view fills the area, so zoom has all of it; a phone on its side is one view too.
        Assert.Equal([new RectD(0, 0, 1000, 700)], CopiesLayout.Cells(1000, 700, Upright, 1, 12));
        Assert.Equal([new RectD(0, 0, 1000, 700)], CopiesLayout.Cells(1000, 700, OnItsSide, 3, 12));
        Assert.Equal([new RectD(0, 0, 0, 0)], CopiesLayout.Cells(0, 700, Upright, 3, 12));
    }

    [Fact]
    public void Cells_ShrinkTogetherWhenTheAreaNarrows()
    {
        // 600 wide cannot hold two at 315: both shrink, still upright, still the gap apart.
        var cells = CopiesLayout.Cells(600, 700, Upright, 2, 12);
        Assert.Equal(294, cells[0].Width, 3);
        Assert.Equal(cells[0].Width / Upright, cells[0].Height, 3);
        Assert.Equal(12, cells[1].X - cells[0].Right, 3);
        Assert.Equal(0, cells[0].X, 3);
        Assert.Equal((700 - cells[0].Height) / 2, cells[0].Y, 3);

        // A negative gap is no gap.
        var touching = CopiesLayout.Cells(1000, 700, Upright, 2, -5);
        Assert.Equal(touching[0].Right, touching[1].X, 3);
    }

    [Fact]
    public void ShownCount_HidesCopiesRatherThanSqueezingThemIntoSlivers()
    {
        Assert.Equal(1, CopiesLayout.ShownCount(1, 5000, 12));
        Assert.Equal(3, CopiesLayout.ShownCount(3, 1000, 12));
        // 300 holds two at the 120 minimum with a 12 gap (252), not three (384).
        Assert.Equal(2, CopiesLayout.ShownCount(3, 300, 12));
        Assert.Equal(1, CopiesLayout.ShownCount(3, 50, 12));
        Assert.Equal(1, CopiesLayout.ShownCount(3, -10, 12));
    }

    [Fact]
    public void WhyNoMore_SaysWhichLimitWasReached()
    {
        Assert.Null(CopiesLayout.WhyNoMore(1, 1000, 700, Upright, 12, most: 4));
        Assert.True(CopiesLayout.CanAdd(2, 1000, 700, Upright, 12, most: 4));
        Assert.Contains("upright", CopiesLayout.WhyNoMore(1, 4000, 700, OnItsSide, 12, 4), StringComparison.Ordinal);
        Assert.Equal("That is the most copies set in Settings (1).", CopiesLayout.WhyNoMore(2, 4000, 700, Upright, 12, most: 2));
        Assert.Contains("Make the window wider", CopiesLayout.WhyNoMore(3, 1000, 700, Upright, 12, most: 6), StringComparison.Ordinal);
        Assert.False(CopiesLayout.CanAdd(3, 1000, 700, Upright, 12, most: 6));
    }

    [Fact]
    public void MapPoint_LandsAtTheSameSpotOfTheOtherView()
    {
        // The middle of a copy is the middle of the main view, whatever their sizes.
        Assert.Equal((150, 350), CopiesLayout.MapPoint(500, 350, 400, 0, 200, 700, 0, 0, 300, 700));
        // Corners stay corners; points outside are held to the edge.
        Assert.Equal((0, 0), CopiesLayout.MapPoint(400, 0, 400, 0, 200, 700, 0, 0, 300, 700));
        Assert.Equal((300, 700), CopiesLayout.MapPoint(9999, 9999, 400, 0, 200, 700, 0, 0, 300, 700));
        // A view with no size maps to the middle rather than dividing by zero.
        Assert.Equal((150, 350), CopiesLayout.MapPoint(5, 5, 0, 0, 0, 0, 0, 0, 300, 700));
    }

    // ----- Starting and stopping -----

    [Fact]
    public void Plan_StartsOneAtATimeOnlyWhileTheMainPictureIsUp()
    {
        var plan = new CopiesPlan();
        Assert.Equal(CopyStep.Nothing, plan.Next(mainIsMirroring: true));

        plan.Want(2);
        Assert.Equal(CopyStep.Nothing, plan.Next(mainIsMirroring: false));
        Assert.Equal(new CopyStep(CopyStepKind.Launch, 0), plan.Next(true));

        plan.Started(0);
        Assert.True(plan.Starting);
        Assert.Equal(CopyStep.Nothing, plan.Next(true));

        plan.Ready(0);
        Assert.False(plan.Starting);
        Assert.Equal(new CopyStep(CopyStepKind.Launch, 1), plan.Next(true));
        plan.Started(1);
        plan.Ready(1);
        Assert.Equal([0, 1], plan.Running);
        Assert.Equal(CopyStep.Nothing, plan.Next(true));
    }

    [Fact]
    public void Plan_StopsTheLastFirstAndEverythingWhenTheMainPictureGoes()
    {
        var plan = new CopiesPlan();
        plan.Want(3);
        foreach (var index in new[] { 0, 1, 2 })
        {
            plan.Started(index);
            plan.Ready(index);
        }

        plan.Want(1);
        Assert.Equal(new CopyStep(CopyStepKind.Stop, 2), plan.Next(true));
        plan.Stopped(2);
        Assert.Equal(new CopyStep(CopyStepKind.Stop, 1), plan.Next(true));
        plan.Stopped(1);
        Assert.Equal(CopyStep.Nothing, plan.Next(true));

        // The phone goes away: its copies close, but what was wanted is kept for its return.
        Assert.Equal(new CopyStep(CopyStepKind.Stop, 0), plan.Next(false));
        plan.Stopped(0);
        Assert.Equal(CopyStep.Nothing, plan.Next(false));
        Assert.Equal(1, plan.Wanted);
        Assert.Equal(new CopyStep(CopyStepKind.Launch, 0), plan.Next(true));

        plan.Want(-4);
        Assert.Equal(0, plan.Wanted);
    }

    [Fact]
    public void Plan_TriesAFailedCopyOnceMoreThenGivesItUpWithTheReason()
    {
        var plan = new CopiesPlan();
        var gaveUp = new List<(int Index, string Reason)>();
        plan.GaveUp += (index, reason) => gaveUp.Add((index, reason));
        plan.Want(2);

        plan.Started(0);
        plan.Failed(0, "no encoder");
        Assert.False(plan.Starting);
        Assert.Equal(2, plan.Wanted);
        Assert.Empty(gaveUp);
        Assert.Equal(new CopyStep(CopyStepKind.Launch, 0), plan.Next(true));

        plan.Started(0);
        plan.Failed(0, "no encoder");
        Assert.Equal(1, plan.Wanted);
        Assert.Equal([(0, "no encoder")], gaveUp);

        // A copy that starts after one failure has its count forgiven.
        plan.Started(0);
        plan.Failed(0, "flaky");
        plan.Started(0);
        plan.Ready(0);
        plan.Failed(0, "ended");
        Assert.Equal(1, plan.Wanted);
        Assert.Single(gaveUp);
    }

    // ----- What the Controls tab says -----

    [Fact]
    public void Status_SaysWhereTheCopiesStand()
    {
        Assert.Equal("Extra live views of the phone, side by side, once it is mirrored.",
            new CopiesStatus(false, 0, 0, false, 1, true, "x").Summary);
        Assert.Equal("2 copies will open again once the phone is mirrored.", new CopiesStatus(false, 2, 0, false, 1, true, null).Summary);
        Assert.Equal("Opening copy 2 of 2…", new CopiesStatus(true, 2, 1, true, 2, true, null).Summary);
        Assert.Equal("1 copy out of sight while the phone is on its side. It comes back when the phone is upright.",
            new CopiesStatus(true, 1, 1, false, 1, false, null).Summary);
        Assert.Equal("2 copies open, 1 hidden for want of room. Make the window wider to see it.",
            new CopiesStatus(true, 2, 2, false, 2, true, null).Summary);
        Assert.Equal("1 of 2 open. Trying the next one again shortly.", new CopiesStatus(true, 2, 1, false, 2, true, null).Summary);
        Assert.StartsWith("1 copy open. Touch or type on any of them", new CopiesStatus(true, 1, 1, false, 2, true, null).Summary, StringComparison.Ordinal);
        Assert.EndsWith(" Too many.", new CopiesStatus(true, 0, 0, false, 1, true, "Too many.").Summary, StringComparison.Ordinal);

        Assert.True(new CopiesStatus(true, 0, 0, false, 1, true, null).CanAdd);
        Assert.False(new CopiesStatus(true, 0, 0, true, 1, true, null).CanAdd);
        Assert.False(new CopiesStatus(true, 0, 0, false, 1, true, "no room").CanAdd);
        Assert.False(new CopiesStatus(false, 0, 0, false, 1, true, null).CanAdd);
        Assert.True(new CopiesStatus(false, 1, 0, false, 1, true, null).CanRemove);
        Assert.Equal(0, new CopiesStatus(false, 2, 2, false, 1, true, null).Hidden);
    }

    // ----- Each copy's own session -----

    [Fact]
    public void CopyArguments_ChangeNothingOnThePhoneAndUseAPortOfTheirOwn()
    {
        var config = new RexConfig();
        config.Session.PowerOffOnClose = true;
        config.Mirror.AudioDup = true;
        config.Copies.MaxSize = 1024;

        var main = ScrcpyArguments.Build(config, "S", false, "Main", null, @"C:\rec\a.mp4");
        var copy = ScrcpyArguments.Build(config, "S", false, "Copy", null, @"C:\rec\a.mp4", copyIndex: 1);

        Assert.Contains("--port=27183", main);
        Assert.Contains("--port=27185", copy);
        Assert.Equal(27184, ScrcpyArguments.CopyPort(0));
        Assert.Equal(27184, ScrcpyArguments.CopyPort(-3));

        foreach (var power in new[] { "--turn-screen-off", "--stay-awake", "--keep-active", "--power-off-on-close" })
        {
            Assert.Contains(power, main);
            Assert.DoesNotContain(power, copy);
        }

        Assert.Contains("--no-cleanup", copy);
        Assert.Contains("--no-power-on", copy);
        Assert.DoesNotContain("--no-cleanup", main);
        Assert.Contains("--no-audio", copy);
        Assert.DoesNotContain("--audio-dup", copy);
        Assert.DoesNotContain(copy, a => a.StartsWith("--record=", StringComparison.Ordinal));
        Assert.Contains("--record=C:\\rec\\a.mp4", main);
        Assert.Contains("--max-size=1024", copy);
        Assert.Contains("--max-size=1920", main);

        // Everything that makes the embedded window work is the same in both.
        foreach (var fixedArgument in new[] { "--window-borderless", "--mouse=sdk", "--keyboard=uhid", "--shortcut-mod=rctrl", "--no-window-aspect-ratio-lock" })
        {
            Assert.Contains(fixedArgument, copy);
        }

        // With no copy resolution of its own, a copy matches the main picture.
        config.Copies.MaxSize = 0;
        Assert.Contains("--max-size=1920", ScrcpyArguments.Build(config, "S", false, "Copy", null, null, copyIndex: 0));
    }

    [Theory]
    [InlineData("--port=27200")]
    [InlineData("--no-cleanup")]
    [InlineData("--no-power-on")]
    public void TheCopyFlagsCannotBeSetByHand(string extra) =>
        Assert.Throws<FormatException>(() => ScrcpyArguments.SplitExtraArgs(extra));

    [Fact]
    public void ACopyOpensShowingThePictureTheWayTheMainViewDoes()
    {
        var config = new RexConfig();
        config.Mirror.ExtraArgs = "--display-orientation=180";

        // Last, so it wins over the extra arguments' own starting orientation.
        var copy = ScrcpyArguments.Build(config, "S", false, "Copy", null, null, copyIndex: 0, displayOrientation: 5);
        Assert.Equal("--display-orientation=flip90", copy[^1]);
        Assert.Contains("--display-orientation=180", copy);

        // The main session is never given one: it starts as its arguments say.
        var main = ScrcpyArguments.Build(config, "S", false, "Main", null, null, displayOrientation: 5);
        Assert.DoesNotContain("--display-orientation=flip90", main);
        Assert.DoesNotContain(ScrcpyArguments.Build(new RexConfig(), "S", false, "Copy", null, null, copyIndex: 0), a => a.StartsWith("--display-orientation", StringComparison.Ordinal));
    }

    // ----- How the PC view is shown, which every copy follows -----

    [Theory]
    // Quarter turns add up, clockwise, and come round again.
    [InlineData("0", "rotate-right", "90")]
    [InlineData("90", "rotate-right", "180")]
    [InlineData("270", "rotate-right", "0")]
    [InlineData("0", "rotate-left", "270")]
    [InlineData("90", "rotate-left", "0")]
    // A flip is taken before the turn, so flipping a picture on its side turns it the other way.
    [InlineData("0", "flip-horizontal", "flip0")]
    [InlineData("90", "flip-horizontal", "flip270")]
    [InlineData("270", "flip-horizontal", "flip90")]
    [InlineData("180", "flip-horizontal", "flip180")]
    [InlineData("flip0", "flip-horizontal", "0")]
    [InlineData("0", "flip-vertical", "flip180")]
    [InlineData("flip90", "rotate-right", "flip180")]
    public void Orientation_ComposesTurnsAndFlipsTheWayScrcpyDoes(string from, string action, string expected)
    {
        var transform = DisplayOrientation.TransformFor(action);
        Assert.NotNull(transform);
        Assert.Equal(expected, DisplayOrientation.Name(DisplayOrientation.Apply(DisplayOrientation.Parse(from)!.Value, transform.Value)));
    }

    [Fact]
    public void Orientation_StartsWhereTheExtraArgumentsPutIt()
    {
        Assert.Equal(DisplayOrientation.Upright, DisplayOrientation.Initial([]));
        Assert.Equal(2, DisplayOrientation.Initial(["--display-orientation=180"]));
        Assert.Equal(5, DisplayOrientation.Initial(["--orientation", "flip90"]));
        // The last one given wins, as it does for scrcpy.
        Assert.Equal(1, DisplayOrientation.Initial(["--orientation=270", "--render-fit=letterbox", "--display-orientation=90"]));
        // Anything scrcpy would not take is not an orientation.
        Assert.Equal(DisplayOrientation.Upright, DisplayOrientation.Initial(["--display-orientation=45"]));
        Assert.Equal(DisplayOrientation.Upright, DisplayOrientation.Initial(["--display-orientation"]));

        Assert.Equal(8, DisplayOrientation.Names.Count);
        Assert.All(DisplayOrientation.Names, name => Assert.Equal(name, DisplayOrientation.Name(DisplayOrientation.Parse(name)!.Value)));
        Assert.Equal(7, DisplayOrientation.Parse(" FLIP270 "));
        Assert.Null(DisplayOrientation.Parse(null));
        Assert.Null(DisplayOrientation.TransformFor("rotate-device"));
    }

    [Theory]
    [InlineData("rotate-left", true)]
    [InlineData("rotate-right", true)]
    [InlineData("flip-horizontal", true)]
    [InlineData("flip-vertical", true)]
    [InlineData("pause", true)]
    [InlineData("resume", true)]
    [InlineData("reset-capture", true)]
    // Anything that acts on the phone happens once, through the main session.
    [InlineData("rotate-device", false)]
    [InlineData("sleep", false)]
    [InlineData("copy", false)]
    [InlineData("paste", false)]
    [InlineData("paste-text", false)]
    [InlineData("fps", false)]
    public void OnlyShortcutsAboutThePictureOnThisPcGoToEveryCopy(string action, bool everyView)
    {
        Assert.NotNull(ScrcpyShortcuts.For(action));
        Assert.Equal(everyView, ScrcpyShortcuts.AppliesToEveryView(action));
    }

    [Fact]
    public void Settings_AreKeptWithinWhatAPhoneAndAWindowCanHold()
    {
        var copies = new CopiesSettings { Most = 99, Gap = double.NaN, MaxSize = 100, Remember = false };
        copies.Normalize();
        Assert.Equal(CopiesSettings.MostUpperBound, copies.Most);
        Assert.Equal(12, copies.Gap);
        Assert.Equal(CopiesSettings.SmallestMaxSize, copies.MaxSize);
        Assert.False(copies.Remember);

        var low = new CopiesSettings { Most = 0, Gap = -3, MaxSize = -1 };
        low.Normalize();
        Assert.Equal(1, low.Most);
        Assert.Equal(0, low.Gap);
        Assert.Equal(0, low.MaxSize);

        var high = new CopiesSettings { Gap = 500, MaxSize = 99999 };
        high.Normalize();
        Assert.Equal(CopiesSettings.GapUpperBound, high.Gap);
        Assert.Equal(MirrorSettings.MaxSizeUpperBound, high.MaxSize);

        var fresh = new RexConfig();
        Assert.Equal((3, 12.0, 0, true), (fresh.Copies.Most, fresh.Copies.Gap, fresh.Copies.MaxSize, fresh.Copies.Remember));
        Assert.NotSame(fresh.Copies, fresh.Copy().Copies);
    }
}
