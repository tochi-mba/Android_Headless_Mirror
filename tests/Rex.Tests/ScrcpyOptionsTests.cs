using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// More of scrcpy in Settings: what each new option puts on the command line and in which sessions
/// (the main one, copies, the second screen, the other phone), how config.json values are kept to
/// what scrcpy accepts, reading the phone's encoders, and stopping at a time limit.
/// </summary>
public sealed class ScrcpyOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RexConfig EveryOptionSet()
    {
        var config = new RexConfig();
        config.Mirror.VideoEncoder = "c2.exynos.h264.encoder";
        config.Mirror.Crop = "1080:1200:0:600";
        config.Mirror.CaptureOrientation = "@0";
        config.Mirror.StartOrientation = "90";
        config.Mirror.Angle = 15;
        config.Mirror.SmoothScaling = false;
        config.Mirror.ShowTouches = true;
        config.Mirror.TimeLimitMinutes = 30;
        config.Mirror.AudioOutputBufferMs = 40;
        config.Mirror.RequireAudio = true;
        config.Copies.MaxFps = 30;
        return config;
    }

    private static readonly string[] Picture =
        ["--video-encoder=c2.exynos.h264.encoder", "--crop=1080:1200:0:600", "--capture-orientation=@0", "--angle=15", "--no-mipmaps"];

    private static readonly string[] MainOnly =
        ["--show-touches", "--time-limit=1800", "--display-orientation=90", "--audio-output-buffer=40", "--require-audio"];

    [Fact]
    public void TheMainSessionTakesEveryOption()
    {
        var main = ScrcpyArguments.Build(EveryOptionSet(), "S", false, "Main", null, null);
        Assert.All(Picture.Concat(MainOnly), expected => Assert.Contains(expected, main));
        Assert.Contains("--max-fps=60", main);
    }

    [Fact]
    public void CopiesShowTheSamePictureAtTheirOwnFrameRateAndLeaveTheRestToTheMainSession()
    {
        var copy = ScrcpyArguments.Build(EveryOptionSet(), "S", false, "Copy", null, null, copyIndex: 0, displayOrientation: 1);
        Assert.All(Picture, expected => Assert.Contains(expected, copy));
        Assert.Contains("--max-fps=30", copy);
        Assert.DoesNotContain("--max-fps=60", copy);
        Assert.DoesNotContain("--show-touches", copy);
        Assert.DoesNotContain(copy, a => a.StartsWith("--time-limit", StringComparison.Ordinal));
        Assert.DoesNotContain(copy, a => a.StartsWith("--audio-output-buffer", StringComparison.Ordinal));
        Assert.DoesNotContain("--require-audio", copy);
        // A copy opens showing the picture the way the main view shows it now, not as it started.
        Assert.Equal(["--display-orientation=90"], copy.Where(a => a.StartsWith("--display-orientation", StringComparison.Ordinal)));
        Assert.Equal("--display-orientation=90", copy[^1]);
    }

    [Fact]
    public void TheSecondScreenKeepsTheEncoderAndScalingButNotThePhonesOwnPictureOptions()
    {
        var config = EveryOptionSet();
        var spec = ScreenSpec.For(config.SecondScreen, (1280, 720), (1080, 2400), "com.example.one", fresh: false);
        var screen = ScrcpyArguments.BuildScreen(config, "S", false, "Screen", (0, 0), spec);
        Assert.Contains("--video-encoder=c2.exynos.h264.encoder", screen);
        Assert.Contains("--no-mipmaps", screen);
        Assert.Contains("--max-fps=60", screen);
        foreach (var option in new[] { "--crop", "--capture-orientation", "--angle", "--display-orientation", "--max-fps=30", "--show-touches", "--time-limit", "--require-audio" })
        {
            Assert.DoesNotContain(screen, a => a.StartsWith(option, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheOtherPhoneTakesWhatIsAboutThisPcAndLeavesWhatIsAboutTheMainPhone()
    {
        var config = EveryOptionSet();
        config.SecondPhone.Sound = "both";
        var other = ScrcpyArguments.BuildOtherPhone(config, "OTHER", false, "Other", null);
        Assert.Contains("--no-mipmaps", other);
        Assert.Contains("--show-touches", other);
        Assert.Contains("--audio-output-buffer=40", other);
        foreach (var option in new[] { "--video-encoder", "--crop", "--capture-orientation", "--angle", "--display-orientation", "--time-limit", "--require-audio" })
        {
            Assert.DoesNotContain(other, a => a.StartsWith(option, StringComparison.Ordinal));
        }

        // Its own options are its own: the main phone's stay as they were.
        Assert.Equal("c2.exynos.h264.encoder", config.Mirror.VideoEncoder);
        Assert.Equal(30, config.Mirror.TimeLimitMinutes);
    }

    [Fact]
    public void WithoutSoundTheSoundOptionsAreLeftOut()
    {
        var config = EveryOptionSet();
        config.Mirror.Audio = false;
        var main = ScrcpyArguments.Build(config, "S", false, "Main", null, null);
        Assert.DoesNotContain(main, a => a.StartsWith("--audio-output-buffer", StringComparison.Ordinal));
        Assert.DoesNotContain("--require-audio", main);
    }

    [Fact]
    public void ShippedValuesAddNothingToTheCommandLine()
    {
        var main = ScrcpyArguments.Build(new RexConfig(), "S", false, "Main", null, null);
        foreach (var option in Picture.Concat(MainOnly).Select(o => o.Split('=')[0]))
        {
            Assert.DoesNotContain(main, a => a.StartsWith(option, StringComparison.Ordinal));
        }

        var copy = ScrcpyArguments.Build(new RexConfig(), "S", false, "Copy", null, null, copyIndex: 0);
        Assert.Contains("--max-fps=60", copy);
    }

    [Fact]
    public void AnOrientationInTheExtraArgumentsStillWinsOverTheStartOne()
    {
        var config = new RexConfig();
        config.Mirror.StartOrientation = "90";
        config.Mirror.ExtraArgs = "--display-orientation=180";
        var main = ScrcpyArguments.Build(config, "S", false, "Main", null, null);
        Assert.True(main.ToList().IndexOf("--display-orientation=90") < main.ToList().IndexOf("--display-orientation=180"));
        Assert.Equal(2, DisplayOrientation.Initial(config.Mirror));

        config.Mirror.ExtraArgs = string.Empty;
        Assert.Equal(1, DisplayOrientation.Initial(config.Mirror));
        config.Mirror.StartOrientation = "flip270";
        Assert.Equal(7, DisplayOrientation.Initial(config.Mirror));
        config.Mirror.StartOrientation = "sideways";
        Assert.Equal(DisplayOrientation.Upright, DisplayOrientation.Initial(config.Mirror));
    }

    [Fact]
    public void ValuesThatSlipPastNormalizeStillNeverReachScrcpy()
    {
        var config = new RexConfig();
        config.Mirror.VideoEncoder = "bad name; reboot";
        config.Mirror.Crop = "big";
        config.Mirror.CaptureOrientation = "@45";
        config.Mirror.Angle = 400;
        var main = ScrcpyArguments.Build(config, "S", false, "Main", null, null);
        foreach (var option in new[] { "--video-encoder", "--crop", "--capture-orientation", "--angle" })
        {
            Assert.DoesNotContain(main, a => a.StartsWith(option, StringComparison.Ordinal));
        }
    }

    // ----- Keeping config.json to what scrcpy accepts -----

    [Fact]
    public void TheNewSettingsAreKeptSane()
    {
        var config = new RexConfig();
        config.Mirror.VideoEncoder = "  c2.exynos.h264.encoder ";
        config.Mirror.Crop = " 100:200:0:0 ";
        config.Mirror.CaptureOrientation = "@90";
        config.Mirror.StartOrientation = " flip90 ";
        config.Mirror.Angle = -15;
        config.Mirror.TimeLimitMinutes = 5000;
        config.Mirror.AudioOutputBufferMs = -1;
        config.Session.RestartLimit = 0;
        config.Copies.MaxFps = 999;
        config.Normalize();
        Assert.Equal("c2.exynos.h264.encoder", config.Mirror.VideoEncoder);
        Assert.Equal("100:200:0:0", config.Mirror.Crop);
        Assert.Equal("@90", config.Mirror.CaptureOrientation);
        Assert.Equal("flip90", config.Mirror.StartOrientation);
        Assert.Equal(345, config.Mirror.Angle);
        Assert.Equal(MirrorSettings.TimeLimitUpperBound, config.Mirror.TimeLimitMinutes);
        Assert.Equal(0, config.Mirror.AudioOutputBufferMs);
        Assert.Equal(1, config.Session.RestartLimit);
        Assert.Equal(MirrorSettings.FpsUpperBound, config.Copies.MaxFps);

        config.Mirror.VideoEncoder = "two words";
        config.Mirror.Crop = "1:2:3";
        config.Mirror.CaptureOrientation = "upright";
        config.Mirror.StartOrientation = null!;
        config.Mirror.Angle = 720;
        config.Mirror.AudioOutputBufferMs = 5000;
        config.Session.RestartLimit = 99;
        config.Copies.MaxFps = -5;
        config.Normalize();
        Assert.Equal(string.Empty, config.Mirror.VideoEncoder);
        Assert.Equal(string.Empty, config.Mirror.Crop);
        Assert.Equal(string.Empty, config.Mirror.CaptureOrientation);
        Assert.Equal("0", config.Mirror.StartOrientation);
        Assert.Equal(0, config.Mirror.Angle);
        Assert.Equal(MirrorSettings.AudioOutputBufferUpperBound, config.Mirror.AudioOutputBufferMs);
        Assert.Equal(SessionSettings.RestartLimitUpperBound, config.Session.RestartLimit);
        Assert.Equal(0, config.Copies.MaxFps);
    }

    [Theory]
    [InlineData("--no-mipmaps\n--show-touches")]
    [InlineData("--crop=1:1:0:0\0")]
    [InlineData("--window-title=\"unclosed")]
    public void ExtraArgumentsScrcpyCouldNotReadAreDropped(string extra)
    {
        var mirror = new MirrorSettings { ExtraArgs = extra };
        mirror.Normalize();
        Assert.Equal(string.Empty, mirror.ExtraArgs);
        mirror.ExtraArgs = new string('a', 4097);
        mirror.Normalize();
        Assert.Equal(string.Empty, mirror.ExtraArgs);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("1080:1200:0:600", null)]
    [InlineData(" 1:1:8192:8192 ", null)]
    [InlineData("1080:1200:0", "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.")]
    [InlineData("1080x1200", "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.")]
    [InlineData("a:b:c:d", "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.")]
    [InlineData("-1:2:3:4", "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.")]
    [InlineData("10000:2:3:4", "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.")]
    [InlineData("1::3:4", "Write it as width:height:x:y in the phone's pixels, such as 1080:1200:0:600.")]
    [InlineData("0:1200:0:0", "The width and height must be at least 1 pixel.")]
    [InlineData("1080:0:0:0", "The width and height must be at least 1 pixel.")]
    [InlineData("9000:1200:0:0", "Each number must be at most 8192.")]
    [InlineData("100:100:0:9000", "Each number must be at most 8192.")]
    public void ACropIsCheckedBeforeScrcpySeesIt(string? crop, string? why) => Assert.Equal(why, MirrorSettings.WhyNotCrop(crop));

    [Theory]
    [InlineData("c2.exynos.h264.encoder", true)]
    [InlineData("OMX.google.h264.encoder", true)]
    [InlineData("c2_android-avc", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("two words", false)]
    [InlineData("--max-fps=1", false)]
    [InlineData("name;reboot", false)]
    public void AnEncoderNameIsOnlyLettersDigitsDotsDashesAndUnderscores(string? name, bool valid)
    {
        Assert.Equal(valid, MirrorSettings.IsValidEncoderName(name));
        Assert.False(MirrorSettings.IsValidEncoderName(new string('a', MirrorSettings.EncoderNameMaxLength + 1)));
    }

    [Theory]
    [InlineData("Mirror.Crop", "1080:1200", "Write it as width:height:x:y")]
    [InlineData("Mirror.VideoEncoder", "two words", "letters, digits, dots, dashes and underscores")]
    [InlineData("Mirror.StartOrientation", "sideways", "Use one of 0, 90, 180, 270, flip0")]
    [InlineData("Mirror.CaptureOrientation", "@45", "Use one of @, @0, @90, @180 or @270")]
    public void ConfigSetRefusesWhatScrcpyWouldNotTake(string path, string value, string why)
    {
        var refused = Assert.Throws<FormatException>(() => ConfigValidation.Check(path, value, new RexConfig()));
        Assert.Contains(why, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Mirror.Crop", "")]
    [InlineData("Mirror.Crop", "1080:1200:0:600")]
    [InlineData("Mirror.VideoEncoder", "")]
    [InlineData("Mirror.VideoEncoder", "c2.exynos.h264.encoder")]
    [InlineData("Mirror.StartOrientation", "flip180")]
    [InlineData("Mirror.CaptureOrientation", "")]
    [InlineData("Mirror.CaptureOrientation", "@")]
    public void ConfigSetTakesWhatScrcpyTakes(string path, string value) => ConfigValidation.Check(path, value, new RexConfig());

    [Fact]
    public void RestartsInARowNeedRestartingToBeOn()
    {
        var config = new RexConfig();
        Assert.True(SettingsDependencies.Of(config).Restarts);
        config.Session.RestartOnUnexpectedExit = false;
        Assert.False(SettingsDependencies.Of(config).Restarts);
    }

    // ----- The phone's encoders -----

    private const string Listing = """
        [server] INFO: List of video encoders:
            --video-codec=h264 --video-encoder=c2.exynos.h264.encoder       (hw) [vendor]
            --video-codec=h264 --video-encoder=c2.android.avc.encoder       (sw)
            --video-codec=h264 --video-encoder=OMX.google.h264.encoder      (sw) (alias for c2.android.avc.encoder)
            --video-codec=h265 --video-encoder=c2.exynos.hevc.encoder       (hw) [vendor]
            --video-codec=av1 --video-encoder=c2.google.av1.encoder         (hybrid)
            --video-codec=av1 --video-encoder=c2.unknown.av1.encoder
            --video-codec=h264 --video-encoder=c2.exynos.h264.encoder       (hw) [vendor]
            --video-codec=h264 --video-encoder=bad;name                     (hw)
        [server] INFO: List of audio encoders:
            --audio-codec=opus --audio-encoder=c2.android.opus.encoder      (sw)
        """;

    [Fact]
    public void ScrcpysEncoderListIsRead()
    {
        var encoders = EncoderList.Parse(Listing.Split('\n'));
        Assert.Equal(
            [
                new VideoEncoder("h264", "c2.exynos.h264.encoder", "hw"),
                new VideoEncoder("h264", "c2.android.avc.encoder", "sw"),
                new VideoEncoder("h264", "OMX.google.h264.encoder", "sw"),
                new VideoEncoder("h265", "c2.exynos.hevc.encoder", "hw"),
                new VideoEncoder("av1", "c2.google.av1.encoder", "hybrid"),
                new VideoEncoder("av1", "c2.unknown.av1.encoder", ""),
            ],
            encoders);
        Assert.Equal(["c2.exynos.h264.encoder (hardware)", "c2.android.avc.encoder (software)", "OMX.google.h264.encoder (software)"],
            EncoderList.For(encoders, "H264").Select(e => e.Label));
        Assert.Equal(["c2.google.av1.encoder (hybrid)", "c2.unknown.av1.encoder"], EncoderList.For(encoders, "av1").Select(e => e.Label));
        Assert.Empty(EncoderList.Parse(["", "INFO: nothing here", "--video-codec=h264"]));
    }

    [Fact]
    public void AnEncoderIsKeptOnlyWhereThePhoneMakesTheNewCodecWithIt()
    {
        var encoders = EncoderList.Parse(Listing.Split('\n'));
        Assert.Equal(string.Empty, EncoderList.KeepFor("c2.exynos.h264.encoder", "h265", encoders));
        Assert.Equal("c2.exynos.h264.encoder", EncoderList.KeepFor("c2.exynos.h264.encoder", "h264", encoders));
        Assert.Equal(string.Empty, EncoderList.KeepFor("c2.exynos.h264.encoder", "h264", null));
        Assert.Equal(string.Empty, EncoderList.KeepFor(string.Empty, "h264", encoders));
    }

    [Fact]
    public async Task TheEncodersAreReadWithScrcpyWithoutCleaningUp()
    {
        var runner = new FakeProcessRunner { Respond = _ => new ProcessResult(0, Listing, string.Empty) };
        var read = await EncoderList.ReadAsync(runner, "scrcpy.exe", "S1", Ct);
        Assert.True(read.Ok);
        Assert.Equal(6, read.Encoders.Count);
        Assert.Equal(["--serial=S1", "--list-encoders", "--no-cleanup"], runner.Calls[0].Arguments);
    }

    [Theory]
    [InlineData(0, "", "", false, "The phone listed no video encoders.")]
    [InlineData(0, "", "", true, "The phone took too long to list its encoders.")]
    [InlineData(1, "", "ERROR: Could not find any ADB device\n", false, "Could not find any ADB device")]
    public async Task AFailedReadOfTheEncodersSaysWhy(int exit, string stdout, string stderr, bool timedOut, string why)
    {
        var runner = new FakeProcessRunner { Respond = _ => new ProcessResult(exit, stdout, stderr, timedOut) };
        var read = await EncoderList.ReadAsync(runner, "scrcpy.exe", "S1", Ct);
        Assert.False(read.Ok);
        Assert.Empty(read.Encoders);
        Assert.Equal(why, read.Error);
    }

    [Fact]
    public void TheEncodersAreGivenWithWhatIsChosenNow()
    {
        var mirror = new MirrorSettings { VideoCodec = "h265", VideoEncoder = "c2.exynos.hevc.encoder" };
        var json = EncoderList.ToJson("S1", mirror, [new VideoEncoder("h265", "c2.exynos.hevc.encoder", "hw")]);
        Assert.Equal("""{"serial":"S1","codec":"h265","chosen":"c2.exynos.hevc.encoder","encoders":[{"codec":"h265","name":"c2.exynos.hevc.encoder","kind":"hw"}]}""",
            json.ToJsonString());
    }

    [Fact]
    public void AnEncoderThePhoneDoesNotHaveIsSaidInWords()
    {
        Assert.Equal(
            "The phone has no video encoder called c2.nope.encoder for H.265. Choose another in Settings, Picture, or let the phone choose.",
            ScrcpyArguments.MissingEncoder(["INFO: scrcpy 4.1", "[server] ERROR: Video encoder 'c2.nope.encoder' for h265 not found"]));
        Assert.Null(ScrcpyArguments.MissingEncoder(["ERROR: Could not find any ADB device"]));
        Assert.Equal("H.264", ScrcpyArguments.CodecName("h264"));
        Assert.Equal("AV1", ScrcpyArguments.CodecName("AV1"));
        Assert.Equal("vp9", ScrcpyArguments.CodecName("vp9"));
    }

    // ----- Stopping at a time limit -----

    [Theory]
    [InlineData(0, 3600, false)]
    [InlineData(30, 1797, false)]
    [InlineData(30, 1798, true)]
    [InlineData(30, 1800, true)]
    [InlineData(30, 60, false)]
    public void AnEndAtTheTimeLimitIsAStop(int minutes, int ranSeconds, bool reached) =>
        Assert.Equal(reached, MirrorTimeLimit.Reached(minutes, TimeSpan.FromSeconds(ranSeconds)));

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(45, "45 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(120, "2 hours")]
    [InlineData(61, "1 hour 1 minute")]
    [InlineData(150, "2 hours 30 minutes")]
    public void MinutesAreSaidInWords(int minutes, string words) => Assert.Equal(words, TimeWords.Minutes(minutes));

    [Fact]
    public void TheStopSaysHowLongItRan() =>
        Assert.Equal("Stopped after 30 minutes, as set in Settings. Choose Start mirror to mirror again.", MirrorTimeLimit.Stopped(30));

    [Fact]
    public void EveryNewSettingHasAControlAndSaysWhenItApplies()
    {
        string[] nextStart =
        [
            "Mirror.VideoEncoder", "Mirror.Crop", "Mirror.CaptureOrientation", "Mirror.StartOrientation", "Mirror.Angle",
            "Mirror.SmoothScaling", "Mirror.ShowTouches", "Mirror.TimeLimitMinutes", "Mirror.AudioOutputBufferMs", "Mirror.RequireAudio",
        ];
        Assert.All(nextStart, path => Assert.True(SettingsCatalogue.AppliesAtNextStart(path), path));
        Assert.All(nextStart.Append("Session.RestartLimit").Append("Copies.MaxFps"), path => Assert.True(SettingsCatalogue.Controls.ContainsKey(path), path));
        // Copies and the session's own restarts follow at once: copies restart by themselves.
        Assert.False(SettingsCatalogue.AppliesAtNextStart("Copies.MaxFps"));
        Assert.False(SettingsCatalogue.AppliesAtNextStart("Session.RestartLimit"));
    }
}
