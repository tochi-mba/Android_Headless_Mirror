using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// The scrcpy options behind the video, audio, session and input settings: what each setting puts
/// on the command line, which sessions get it (the main one, or every copy too), the combinations
/// scrcpy would refuse to start with, and how config.json values are kept to what scrcpy accepts.
/// </summary>
public sealed class LaunchOptionsTests
{
    private static RexConfig EverythingChanged()
    {
        var config = new RexConfig();
        config.Mirror.VideoBufferMs = 100;
        config.Mirror.DownsizeOnError = false;
        config.Mirror.RenderDriver = "opengl";
        config.Mirror.AudioSource = "mic";
        config.Mirror.AudioBitRate = "256K";
        config.Session.ScreenOffTimeoutSeconds = 300;
        config.Session.KeepPcAwake = true;
        config.Session.StartApp = "+?spotify";
        config.Input.RightClick = "click";
        config.Input.MiddleClick = "nothing";
        config.Input.BackButton = "back";
        config.Input.ForwardButton = "home";
        config.Input.ShiftClicks = false;
        config.Input.MouseHover = false;
        config.Input.ClipboardAutosync = false;
        config.Input.LegacyPaste = true;
        config.Input.Gamepad = "uhid";
        return config;
    }

    [Fact]
    public void EveryChangedSettingReachesTheMainSession()
    {
        var main = ScrcpyArguments.Build(EverythingChanged(), "S", false, "Main", null, null);
        foreach (var expected in new[]
        {
            "--video-buffer=100", "--no-downsize-on-error", "--render-driver=opengl", "--audio-source=mic",
            "--audio-bit-rate=256K", "--screen-off-timeout=300", "--disable-screensaver", "--start-app=+?spotify",
            "--mouse-bind=+-bh", "--no-mouse-hover", "--no-clipboard-autosync", "--legacy-paste", "--gamepad=uhid",
        })
        {
            Assert.Contains(expected, main);
        }
    }

    [Fact]
    public void CopiesShowAndTakeInputTheSameWayButLeaveThePhoneAndTheAppToTheMainSession()
    {
        var copy = ScrcpyArguments.Build(EverythingChanged(), "S", false, "Copy", null, null, copyIndex: 0);
        foreach (var same in new[]
        {
            "--video-buffer=100", "--no-downsize-on-error", "--render-driver=opengl", "--mouse-bind=+-bh",
            "--no-mouse-hover", "--no-clipboard-autosync", "--legacy-paste",
        })
        {
            Assert.Contains(same, copy);
        }

        // A copy that changed the timeout would put the phone's own back when it closed; one that
        // started the app would restart it under the main view; each would add another controller.
        foreach (var once in new[]
        {
            "--audio-source=mic", "--audio-bit-rate=256K", "--screen-off-timeout=300", "--disable-screensaver",
            "--start-app=+?spotify", "--gamepad=uhid",
        })
        {
            Assert.DoesNotContain(once, copy);
        }
    }

    [Fact]
    public void ScrcpysOwnDefaultsAreLeftUnsaid()
    {
        var args = ScrcpyArguments.Build(new RexConfig(), "S", false, "T", null, null);
        foreach (var option in new[]
        {
            "--video-buffer", "--no-downsize-on-error", "--render-driver", "--audio-source", "--audio-bit-rate",
            "--screen-off-timeout", "--disable-screensaver", "--start-app", "--mouse-bind", "--no-key-repeat",
            "--no-mouse-hover", "--no-clipboard-autosync", "--legacy-paste", "--gamepad",
        })
        {
            Assert.DoesNotContain(args, argument => argument.StartsWith(option, StringComparison.Ordinal));
        }

        Assert.Equal(ScrcpyArguments.DefaultMouseBind, new InputSettings().MouseBind);
    }

    [Fact]
    public void KeyRepeatCanOnlyBeTurnedOffForTheRawKeyKeyboard()
    {
        // scrcpy refuses to start with --no-key-repeat and its hardware keyboard.
        var config = new RexConfig();
        config.Input.KeyRepeat = false;
        Assert.DoesNotContain("--no-key-repeat", ScrcpyArguments.Build(config, "S", false, "T", null, null, ScrcpyArguments.FullKeyboardMode));
        Assert.Contains("--no-key-repeat", ScrcpyArguments.Build(config, "S", false, "T", null, null, ScrcpyArguments.CompatibilityKeyboardMode));
    }

    [Theory]
    [InlineData("auto", true)]
    [InlineData("playback", true)]
    [InlineData("output", false)]
    [InlineData("mic", false)]
    [InlineData("voice-call", false)]
    public void AudioKeepsPlayingOnThePhoneOnlyWithASourceScrcpyCanDuplicate(string source, bool duplicated)
    {
        var config = new RexConfig();
        config.Mirror.AudioDup = true;
        config.Mirror.AudioSource = source;
        Assert.Equal(duplicated, config.Mirror.AudioDupPossible);
        Assert.Equal(duplicated, ScrcpyArguments.Build(config, "S", false, "T", null, null).Contains("--audio-dup"));
    }

    [Fact]
    public void AudioOptionsAreDroppedWithTheAudio()
    {
        var config = EverythingChanged();
        config.Mirror.Audio = false;
        var args = ScrcpyArguments.Build(config, "S", false, "T", null, null);
        Assert.Contains("--no-audio", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--audio-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("back", "home", "recents", "notifications", true, "bhsn:++++")]
    [InlineData("back", "home", "recents", "notifications", false, "bhsn")]
    [InlineData("click", "click", "click", "click", true, "++++:++++")]
    [InlineData("nothing", "back", "home", "recents", true, "-bhs:++++")]
    [InlineData("notifications", "recents", "nothing", "click", false, "ns-+")]
    public void MouseButtonsBecomeScrcpysBindingLetters(string right, string middle, string back, string forward, bool shiftClicks, string bind)
    {
        var input = new InputSettings { RightClick = right, MiddleClick = middle, BackButton = back, ForwardButton = forward, ShiftClicks = shiftClicks };
        Assert.Equal(bind, input.MouseBind);
    }

    [Fact]
    public void RecordingsAreNamedForTheirContainer()
    {
        var at = new DateTime(2026, 9, 30, 22, 5, 1);
        Assert.Equal("android-20260930-220501.mp4", ScrcpyArguments.RecordingFileName("mp4", at));
        Assert.Equal("android-20260930-220501.mkv", ScrcpyArguments.RecordingFileName("MKV", at));
        Assert.Equal("android-20260930-220501.mp4", ScrcpyArguments.RecordingFileName("avi", at));
    }

    [Fact]
    public void SettingsAreKeptToWhatScrcpyAccepts()
    {
        var config = new RexConfig();
        config.Mirror.AudioSource = "MIC";
        config.Mirror.AudioBitRate = "256k";
        config.Mirror.VideoBufferMs = 99_999;
        config.Mirror.RenderDriver = "vulkan";
        config.Mirror.RecordFormat = "AVI";
        config.Session.ScreenOffTimeoutSeconds = 2;
        config.Session.StartApp = "  com.example.player  ";
        config.Input.RightClick = "explode";
        config.Input.MiddleClick = "HOME";
        config.Input.Gamepad = "aoa";
        config.Normalize();

        Assert.Equal("mic", config.Mirror.AudioSource);
        Assert.Equal("256K", config.Mirror.AudioBitRate);
        Assert.Equal(MirrorSettings.VideoBufferUpperBound, config.Mirror.VideoBufferMs);
        Assert.Equal(string.Empty, config.Mirror.RenderDriver);
        Assert.Equal("mp4", config.Mirror.RecordFormat);
        Assert.Equal(5, config.Session.ScreenOffTimeoutSeconds);
        Assert.Equal("com.example.player", config.Session.StartApp);
        Assert.Equal("back", config.Input.RightClick);
        Assert.Equal("home", config.Input.MiddleClick);
        Assert.Equal("disabled", config.Input.Gamepad);

        config.Mirror.AudioBitRate = "loud";
        config.Mirror.VideoBufferMs = -5;
        config.Session.ScreenOffTimeoutSeconds = -4;
        config.Session.StartApp = "--no-video";
        config.Normalize();
        Assert.Equal(MirrorSettings.DefaultAudioBitRate, config.Mirror.AudioBitRate);
        Assert.Equal(0, config.Mirror.VideoBufferMs);
        Assert.Equal(0, config.Session.ScreenOffTimeoutSeconds);
        Assert.Equal(string.Empty, config.Session.StartApp);

        config.Session.ScreenOffTimeoutSeconds = int.MaxValue;
        config.Normalize();
        Assert.Equal(SessionSettings.ScreenOffTimeoutUpperBound, config.Session.ScreenOffTimeoutSeconds);

        Assert.NotSame(config.Input, config.Copy().Input);
        Assert.Equal(config.Input, config.Copy().Input);
    }

    [Theory]
    [InlineData("com.spotify.music", true)]
    [InlineData("?spotify", true)]
    [InlineData("+?Spotify Premium", true)]
    [InlineData("+com.example", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("+?", false)]
    [InlineData("--no-video", false)]
    [InlineData("-x", false)]
    [InlineData("com.example\tapp", false)]
    public void AnAppToStartMustNameAnAppAndNeverAnOption(string value, bool valid) =>
        Assert.Equal(valid, SessionSettings.IsValidStartApp(value));

    [Fact]
    public void AnAppToStartHasALimitToItsName()
    {
        Assert.True(SessionSettings.IsValidStartApp(new string('a', SessionSettings.StartAppMaxLength)));
        Assert.False(SessionSettings.IsValidStartApp(new string('a', SessionSettings.StartAppMaxLength + 1)));
    }
}
