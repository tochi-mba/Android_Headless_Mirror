using Rex.Core;

namespace Rex.Tests;

/// <summary>The second screen: the display it asks for, its session's arguments, sharing the area, what happens next, and where keys go.</summary>
public sealed class SecondScreenTests
{
    private static SecondScreenSettings Screen(Action<SecondScreenSettings>? change = null)
    {
        var settings = new SecondScreenSettings();
        change?.Invoke(settings);
        settings.Normalize();
        return settings;
    }

    [Theory]
    [InlineData("follow", false, 1201, 801, "1200x800", true)]
    [InlineData("phone", false, 0, 0, "1080x2400", false)]
    [InlineData("phone", true, 0, 0, "1080x2400", false)]
    [InlineData("720p", false, 0, 0, "1280x720", false)]
    [InlineData("720p", true, 0, 0, "720x1280", false)]
    [InlineData("1080p", false, 0, 0, "1920x1080", false)]
    [InlineData("1440p", false, 0, 0, "2560x1440", false)]
    [InlineData("custom", true, 0, 0, "1601x901", false)]
    [InlineData("follow", false, 100, 50, "320x320", true)]
    public void ASecondScreenAsksForTheRightDisplay(string size, bool portrait, int viewWidth, int viewHeight, string expected, bool follows)
    {
        var settings = Screen(s =>
        {
            s.Size = size;
            s.Portrait = portrait;
            s.CustomWidth = 1601;
            s.CustomHeight = 901;
        });
        var spec = ScreenSpec.For(settings, (viewWidth, viewHeight), (1080, 2400), "com.example.one", fresh: false);
        // Custom sizes are kept as typed (the encoder rounds odd ones); everything else is made even.
        Assert.Equal(size == "custom" ? "1600x900" : expected, spec.NewDisplay);
        Assert.Equal(follows, spec.Follows);
    }

    /// <summary>
    /// The phone's own size keeps the phone's own shape, upright, whatever the Upright switch says:
    /// it is chosen to suit the phone's apps, and a portrait-only app on a lying display becomes a
    /// small box in its middle. Read while the phone lay on its side, it still stands up; odd sides
    /// are evened for the encoder; a square phone stays square.
    /// </summary>
    [Theory]
    [InlineData(1080, 2400, "1080x2400")]
    [InlineData(2400, 1080, "1080x2400")]
    [InlineData(719, 1601, "718x1600")]
    [InlineData(1000, 1000, "1000x1000")]
    public void ThePhonesOwnSizeKeepsThePhonesShape(int width, int height, string expected)
    {
        foreach (var upright in new[] { false, true })
        {
            var spec = ScreenSpec.For(Screen(s => { s.Size = "phone"; s.Portrait = upright; }), (800, 600), (width, height), "a.b", fresh: false);
            Assert.Equal(expected, spec.NewDisplay);
            Assert.False(spec.Follows);
        }
    }

    [Fact]
    public void ThePhonesOwnSizeFallsBackWhenItIsNotKnownYet()
    {
        var spec = ScreenSpec.For(Screen(s => s.Size = "phone"), (800, 600), (0, 0), "com.example.one", fresh: true);
        Assert.Equal("1920x1080", spec.NewDisplay);
        Assert.Equal("+com.example.one", spec.StartApp);
        Assert.Equal("1920 x 1080", spec.Describe());
        Assert.Equal("fits the window", ScreenSpec.For(Screen(), (800, 600), (0, 0), "a.b", false).Describe());
        Assert.Equal("1280x720/240", (spec with { Width = 1280, Height = 720, Dpi = 240 }).NewDisplay);
    }

    [Theory]
    [InlineData("[server] INFO: New display: 1280x720/240 (id=7)", 1280, 720, 240, 7)]
    [InlineData("INFO: New display: 1920x1080 (id=12)", 1920, 1080, 0, 12)]
    public void TheNewDisplayLineIsRead(string line, int width, int height, int dpi, int id) =>
        Assert.Equal((width, height, dpi, id), ScreenSpec.ParseNewDisplay(line));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("INFO: Texture: 1080x2400")]
    [InlineData("New display: oops (id=)")]
    public void OtherLinesAreNotANewDisplay(string? line) => Assert.Null(ScreenSpec.ParseNewDisplay(line));

    [Theory]
    [InlineData("follow", "follow", 0, 0)]
    [InlineData(" 1080P ", "1080p", 0, 0)]
    [InlineData("1600x900", "custom", 1600, 900)]
    public void ASizeIsReadFromTheCommandLine(string text, string size, int width, int height) =>
        Assert.Equal(new ScreenSize(size, width, height), ScreenSize.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("huge")]
    [InlineData("16x9")]
    public void ANonSizeIsRefused(string? text) => Assert.Null(ScreenSize.Parse(text));

    [Fact]
    public void TheSecondScreensSessionIsACopysWithADisplayOfItsOwn()
    {
        var config = new RexConfig();
        config.Mirror.ExtraArgs = "--crop=100:100:0:0 --display-id=2 --no-mipmaps --capture-orientation=0";
        config.SecondScreen.MaxSize = 1280;
        var spec = new ScreenSpec(1200, 800, 0, true, "com.example.one", Fresh: true);
        var args = ScrcpyArguments.BuildScreen(config, "S1", false, "T", (10, 20), spec, port: 27191);

        Assert.Contains("--no-cleanup", args);
        Assert.Contains("--no-power-on", args);
        Assert.Contains("--no-audio", args);
        Assert.Contains("--port=27191", args);
        Assert.DoesNotContain("--port=27184", args);
        Assert.Contains("--window-x=10", args);
        Assert.Contains("--window-y=20", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--window-width", StringComparison.Ordinal));
        Assert.Contains("--new-display=1200x800", args);
        Assert.Contains("--flex-display", args);
        Assert.Contains("--max-size=1280", args);
        Assert.Single(args, a => a.StartsWith("--max-size=", StringComparison.Ordinal));
        Assert.Contains("--no-vd-destroy-content", args);
        Assert.DoesNotContain("--no-vd-system-decorations", args);
        Assert.Contains("--display-ime-policy=local", args);
        Assert.Contains("--start-app=+com.example.one", args);
        Assert.Contains("--no-mipmaps", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--crop", StringComparison.Ordinal) || a.StartsWith("--display-id", StringComparison.Ordinal) || a.StartsWith("--capture-orientation", StringComparison.Ordinal));
        Assert.DoesNotContain("--turn-screen-off", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--push-target", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("phone", "--display-ime-policy=fallback")]
    [InlineData("never", "--display-ime-policy=hide")]
    public void TheKeyboardGoesWhereItIsAsked(string keyboard, string expected)
    {
        var config = new RexConfig();
        config.SecondScreen.Keyboard = keyboard;
        config.SecondScreen.KeepAppsOnClose = false;
        config.SecondScreen.Decorations = false;
        var args = ScrcpyArguments.BuildScreen(config, "S1", false, "T", (0, 0), new ScreenSpec(1920, 1080, 240, false, "not a package", false));
        Assert.Contains(expected, args);
        Assert.Contains("--window-width=1920", args);
        Assert.Contains("--window-height=1080", args);
        Assert.Contains("--new-display=1920x1080/240", args);
        Assert.DoesNotContain("--flex-display", args);
        Assert.DoesNotContain("--no-vd-destroy-content", args);
        Assert.Contains("--no-vd-system-decorations", args);
        Assert.Contains("--port=27190", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--start-app", StringComparison.Ordinal));
        // No limit of its own: none at all, not the phone's view's.
        Assert.DoesNotContain(args, a => a.StartsWith("--max-size", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePhonesOwnSessionIsUntouched()
    {
        var config = new RexConfig();
        var before = ScrcpyArguments.Build(config, "S1", false, "T", (0, 0, 400, 800), null);
        config.SecondScreen.Size = "1440p";
        config.SecondScreen.Placement = "instead";
        config.Views.Arrangement = "stack";
        Assert.Equal(before, ScrcpyArguments.Build(config, "S1", false, "T", (0, 0, 400, 800), null));
    }

    [Fact]
    public void EverySessionHasAPortOfItsOwn()
    {
        Assert.Equal(27190, ScrcpyArguments.FirstFreeScreenPort([]));
        Assert.Equal(27192, ScrcpyArguments.FirstFreeScreenPort([27190, 27191, 5037]));
        Assert.Null(ScrcpyArguments.FirstFreeScreenPort(Enumerable.Range(27190, 10)));
        Assert.NotEqual(ScrcpyArguments.MainPort, ScrcpyArguments.ScreenPort);
        Assert.True(ScrcpyArguments.CopyPort(4) < ScrcpyArguments.ScreenPort);
    }

    [Theory]
    [InlineData("ERROR: New virtual display is not supported", "This phone cannot make a display of its own for an app.")]
    [InlineData("ERROR: Could not create display", "The phone could not make the second screen.")]
    [InlineData("ERROR: Server connection failed", "The second screen could not connect to the phone.")]
    [InlineData("ERROR: Could not listen on port 27190", "The second screen could not connect to the phone.")]
    [InlineData("something else", "The second screen did not open.")]
    public void AFailureIsSaidInWords(string line, string words) => Assert.Equal(words, ScrcpyArguments.ScreenFailure([line]));

    [Fact]
    public void SecondScreenAndViewsSettingsAreKeptSane()
    {
        var settings = new SecondScreenSettings
        {
            Placement = "ABOVE", Side = "LEFT", Size = "8k", Keyboard = "x", CustomWidth = 9000, CustomHeight = 10, Dpi = 5, MaxSize = 999, MinWidth = 5,
        };
        settings.Normalize();
        Assert.Equal(("beside", "left", "follow", "here"), (settings.Placement, settings.Side, settings.Size, settings.Keyboard));
        Assert.Equal((SecondScreenSettings.LargestSide, SecondScreenSettings.SmallestSide), (settings.CustomWidth, settings.CustomHeight));
        Assert.Equal(SecondScreenSettings.SmallestDpi, settings.Dpi);
        Assert.Equal(0, settings.MaxSize);
        Assert.Equal(SecondScreenSettings.NarrowestView, settings.MinWidth);

        var high = new SecondScreenSettings { Dpi = 9000, MinWidth = 9000, MaxSize = 1920 };
        high.Normalize();
        Assert.Equal((SecondScreenSettings.LargestDpi, SecondScreenSettings.WidestMinimum, 1920), (high.Dpi, high.MinWidth, high.MaxSize));
        var none = new SecondScreenSettings { Dpi = -3 };
        none.Normalize();
        Assert.Equal(0, none.Dpi);
        Assert.NotSame(settings, settings.Copy());

        var views = new ViewsSettings { Arrangement = "grid", Outline = "ALWAYS", Captions = "sometimes" };
        views.Normalize();
        Assert.Equal(("auto", "always", "auto"), (views.Arrangement, views.Outline, views.Captions));
        Assert.NotSame(views, views.Copy());
        Assert.True(ViewsSettings.Shows("always", false));
        Assert.True(ViewsSettings.Shows("auto", true));
        Assert.False(ViewsSettings.Shows("auto", false));
        Assert.False(ViewsSettings.Shows("never", true));
    }

    // ----- Sharing the area -----

    private const double Upright = 1080.0 / 2400.0;

    [Fact]
    public void BesideThePhoneTheScreenTakesTheRest()
    {
        var a = ViewsLayout.Arrange(1000, 600, Upright, Screen(), new ViewsSettings(), 10);
        Assert.Equal(new RectD(0, 0, 270, 600), a.Phone);
        Assert.Equal(new RectD(280, 0, 720, 600), a.Screen);
        Assert.False(a.Stacked);
        Assert.False(a.Squeezed);

        var left = ViewsLayout.Arrange(1000, 600, Upright, Screen(s => s.Side = "left"), new ViewsSettings(), 10);
        Assert.Equal(new RectD(730, 0, 270, 600), left.Phone);
        Assert.Equal(new RectD(0, 0, 720, 600), left.Screen);
    }

    [Fact]
    public void InsteadOfThePhoneTheScreenFillsTheArea()
    {
        var a = ViewsLayout.Arrange(1000, 600, Upright, Screen(s => s.Placement = "instead"), new ViewsSettings(), 10);
        Assert.Null(a.Phone);
        Assert.Equal(new RectD(0, 0, 1000, 600), a.Screen);
        Assert.False(a.Squeezed);
        Assert.Null(ViewsLayout.Arrange(0, 600, Upright, Screen(), new ViewsSettings(), 10).Phone);
    }

    [Fact]
    public void ANarrowAreaShrinksThePhoneFirstThenShowsTheScreenAlone()
    {
        var narrow = ViewsLayout.Arrange(600, 800, Upright, Screen(), new ViewsSettings(), 10);
        Assert.Equal(600 - 10 - 360, narrow.Phone!.Value.Width);
        Assert.Equal(360, narrow.Screen.Width);

        var tiny = ViewsLayout.Arrange(400, 800, Upright, Screen(), new ViewsSettings(), 10);
        Assert.Null(tiny.Phone);
        Assert.True(tiny.Squeezed);
        Assert.Equal(400, tiny.Screen.Width);
    }

    [Fact]
    public void TheSplitterMovesTheLineWithinLimits()
    {
        var half = ViewsLayout.Arrange(1000, 600, Upright, Screen(), new ViewsSettings(), 10, split: 0.5);
        Assert.Equal(500, half.Phone!.Value.Width);
        var greedy = ViewsLayout.Arrange(1000, 600, Upright, Screen(), new ViewsSettings(), 10, split: 0.9);
        Assert.Equal(1000 - 10 - 360, greedy.Phone!.Value.Width);
        var meagre = ViewsLayout.Arrange(1000, 600, Upright, Screen(), new ViewsSettings(), 10, split: 0.06);
        Assert.Equal(ViewsLayout.NarrowestPhone, meagre.Phone!.Value.Width);

        Assert.Equal(0.25, ViewsLayout.SplitAt(250, 1000));
        Assert.Equal(0.05, ViewsLayout.SplitAt(-10, 1000));
        Assert.Equal(0.95, ViewsLayout.SplitAt(5000, 1000));
        Assert.Equal(0, ViewsLayout.SplitAt(10, 0));
    }

    [Fact]
    public void AWidePhoneGoesAboveTheScreen()
    {
        var wide = 2400.0 / 1080.0;
        var auto = ViewsLayout.Arrange(1000, 1000, wide, Screen(), new ViewsSettings(), 10);
        Assert.True(auto.Stacked);
        Assert.Equal(new RectD(0, 0, 1000, 450), auto.Phone);
        Assert.Equal(new RectD(0, 460, 1000, 540), auto.Screen);

        var below = ViewsLayout.Arrange(1000, 1000, wide, Screen(s => s.Side = "left"), new ViewsSettings(), 10);
        Assert.Equal(new RectD(0, 550, 1000, 450), below.Phone);
        Assert.Equal(new RectD(0, 0, 1000, 540), below.Screen);

        var forced = ViewsLayout.Arrange(1000, 1000, Upright, Screen(), new ViewsSettings { Arrangement = "stack" }, 10, split: 0.3);
        Assert.True(forced.Stacked);
        Assert.Equal(300, forced.Phone!.Value.Height);

        var side = ViewsLayout.Arrange(1000, 1000, wide, Screen(), new ViewsSettings { Arrangement = "side" }, 10);
        Assert.False(side.Stacked);

        var squeezed = ViewsLayout.Arrange(1000, 300, wide, Screen(), new ViewsSettings(), 10);
        Assert.True(squeezed.Squeezed);
        Assert.True(squeezed.Stacked);
        Assert.Null(squeezed.Phone);
    }

    // ----- What happens next -----

    private static ScreenSpec Spec(int width = 1200, bool follows = true, int dpi = 0) => new(width, 800, dpi, follows, "com.example.one", false);

    [Fact]
    public void ASecondScreenOpensRetriesOnceAndThenGivesUp()
    {
        var plan = new SecondScreenPlan();
        Assert.Equal(ScreenStep.Nothing, plan.Failed("too early"));
        Assert.Equal(ScreenStep.Launch, plan.Open("com.example.one", Spec()));
        Assert.Equal(ScreenState.Opening, plan.State);
        Assert.Equal(ScreenStep.Retry, plan.Failed("first"));
        Assert.Equal(ScreenState.Opening, plan.State);
        Assert.Equal(ScreenStep.GiveUp, plan.Failed("second"));
        Assert.Equal(ScreenState.Failed, plan.State);
        Assert.Equal("second", plan.Why);

        plan.Open("com.example.two", Spec());
        plan.Opened();
        Assert.Equal(ScreenState.Showing, plan.State);
        Assert.Null(plan.Why);
        Assert.Equal(ScreenStep.Retry, plan.Failed("it closed by itself"));
        plan.Opened();
        Assert.Equal(ScreenStep.Retry, plan.Failed("again, after showing"));
    }

    [Fact]
    public void ClosingKeepsTheAppForNextTime()
    {
        var plan = new SecondScreenPlan();
        Assert.Equal(ScreenStep.Nothing, plan.Close());
        plan.Open("com.example.one", Spec());
        plan.Opened();
        plan.Switched("com.example.two");
        Assert.Equal(ScreenStep.Close, plan.Close());
        Assert.Equal(ScreenState.Off, plan.State);
        Assert.Equal("com.example.two", plan.App);
        Assert.True(plan.ReopensWith(Screen(s => s.ReopenOnStart = true)));
        Assert.False(plan.ReopensWith(Screen()));
    }

    [Fact]
    public void NewSettingsMakeTheDisplayAgainOnlyWhenTheyChangeIt()
    {
        var plan = new SecondScreenPlan();
        Assert.Equal(ScreenStep.Nothing, plan.SettingsChanged(Spec()));
        plan.Open("com.example.one", Spec());
        plan.Opened();
        // A display that follows the view changes size by itself.
        Assert.Equal(ScreenStep.Nothing, plan.SettingsChanged(Spec(width: 1400)));
        Assert.Equal(ScreenStep.Reopen, plan.SettingsChanged(Spec(dpi: 240)));
        Assert.Equal(ScreenState.Opening, plan.State);
        plan.Opened();
        Assert.Equal(ScreenStep.Reopen, plan.SettingsChanged(Spec(width: 1920, follows: false)));
        plan.Opened();
        Assert.Equal(ScreenStep.Nothing, plan.SettingsChanged(Spec(width: 1920, follows: false)));
        Assert.Equal(ScreenStep.Reopen, plan.SettingsChanged(Spec(width: 1280, follows: false)));
    }

    [Theory]
    [InlineData("28", "9", "A second screen needs Android 10 or later; this phone has Android 9.")]
    [InlineData("26", "", "A second screen needs Android 10 or later; this phone has Android 26.")]
    [InlineData("29", "10", null)]
    [InlineData("", "", null)]
    public void OnlyAndroid10AndLaterCanHaveOne(string api, string version, string? why) => Assert.Equal(why, SecondScreenPlan.WhyNot(api, version));

    // ----- Where keys go -----

    [Theory]
    [InlineData("home", 'H', false, 1)]
    [InlineData("back", 'B', false, 1)]
    [InlineData("recents", 'S', false, 1)]
    [InlineData("notifications", 'N', false, 1)]
    [InlineData("quick-settings", 'N', false, 2)]
    [InlineData("collapse", 'N', true, 1)]
    [InlineData("rotate-left", 0x25, false, 1)]
    [InlineData("rotate-right", 0x27, false, 1)]
    [InlineData("flip-horizontal", 0x25, true, 1)]
    [InlineData("flip-vertical", 0x26, true, 1)]
    [InlineData("pause", 'Z', false, 1)]
    [InlineData("paste-text", 'V', true, 1)]
    public void DisplayKeysGoToTheSecondScreenWhenItHasTheKeyboard(string action, int key, bool shift, int repeat)
    {
        var route = ActionRouting.For(action, screenActive: true);
        Assert.Equal(RouteKind.Screen, route.Kind);
        Assert.Equal(new ScrcpyShortcut(key, shift, repeat), route.Shortcut);
        Assert.Equal(RouteKind.AsUsual, ActionRouting.For(action, screenActive: false).Kind);
    }

    [Theory]
    [InlineData("rotate-device")]
    [InlineData("rotation-portrait")]
    [InlineData("rotation-landscape")]
    [InlineData("rotation-auto")]
    public void TurningThePhoneIsRefusedOnASecondScreen(string action)
    {
        var route = ActionRouting.For(action, screenActive: true);
        Assert.Equal(RouteKind.Refuse, route.Kind);
        Assert.Equal(ActionRouting.NoTurning, route.Why);
    }

    [Theory]
    [InlineData("volume-up")]
    [InlineData("power")]
    [InlineData("screenshot")]
    [InlineData("zoom-in")]
    public void ThePhonesOwnActionsGoAsUsual(string action) => Assert.Equal(RouteKind.AsUsual, ActionRouting.For(action, screenActive: true).Kind);
}
