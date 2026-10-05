using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// Two phones side by side: which phone goes beside the main one, names that tell them apart, what
/// happens when one connects or its session ends, how they share the area, the other phone's own
/// command line, where actions go while it is in use, and whose sound plays.
/// </summary>
public sealed class SecondPhoneTests
{
    private static AdbDevice Usb(string serial, string state = "device") => new(serial, state, false, "p", "Model");

    private static AdbDevice Wifi(string serial) => new(serial, "device", true, "p", "Model");

    private static readonly IReadOnlyDictionary<string, string> NoHardware = new Dictionary<string, string>();

    private static readonly IReadOnlySet<string> NoneDeclined = new HashSet<string>();

    // ----- Settings -----

    [Fact]
    public void SecondPhoneSettingsAreKeptSane()
    {
        var settings = new SecondPhoneSettings
        {
            WhenConnected = "sometimes",
            Side = "above",
            Sound = "loud",
            ScreenOff = "dim",
            MaxSize = 999,
            BitRate = "fast",
        };
        settings.Normalize();
        Assert.Equal("ask", settings.WhenConnected);
        Assert.Equal("right", settings.Side);
        Assert.Equal("main", settings.Sound);
        Assert.Equal("same", settings.ScreenOff);
        Assert.Equal(0, settings.MaxSize);
        Assert.Equal(string.Empty, settings.BitRate);

        var good = new SecondPhoneSettings { MaxSize = 1280, BitRate = " 8m " };
        good.Normalize();
        Assert.Equal(1280, good.MaxSize);
        Assert.Equal("8M", good.BitRate);

        var copy = good.Copy();
        copy.Enabled = false;
        Assert.True(good.Enabled);
    }

    [Theory]
    [InlineData("same", true, true)]
    [InlineData("same", false, false)]
    [InlineData("off", false, true)]
    [InlineData("on", true, false)]
    public void ItsOwnScreenFollowsItsSettingOrTheMainPhones(string screenOff, bool mainTurnsOff, bool turnsOff) =>
        Assert.Equal(turnsOff, new SecondPhoneSettings { ScreenOff = screenOff }.TurnsScreenOff(mainTurnsOff));

    [Theory]
    [InlineData("main", false)]
    [InlineData("active", true)]
    [InlineData("both", true)]
    public void ItsSessionCarriesSoundOnlyWhenItCanBeHeard(string sound, bool carries) =>
        Assert.Equal(carries, new SecondPhoneSettings { Sound = sound }.OtherHasSound);

    // ----- Which phone goes beside -----

    [Fact]
    public void TheOtherPhoneIsAReadyPhoneThatIsNotTheMainOne()
    {
        var main = Usb("MAIN1");
        Assert.Null(PhonePick.Other([main], main, NoHardware, null, NoneDeclined));
        Assert.Null(PhonePick.Other([main, Usb("WAIT1", "unauthorized")], main, NoHardware, null, NoneDeclined));
        Assert.Equal("OTHER1", PhonePick.Other([main, Usb("OTHER1")], main, NoHardware, null, NoneDeclined)!.Serial);
        Assert.Null(PhonePick.Other([main, Usb("OTHER1")], main, NoHardware, null, new HashSet<string> { "OTHER1" }));
    }

    [Fact]
    public void TheRememberedPhoneComesFirstThenUsb()
    {
        var main = Usb("MAIN1");
        var devices = new[] { main, Wifi("192.168.1.7:5555"), Usb("OTHER1"), Usb("OTHER2") };
        Assert.Equal("OTHER2", PhonePick.Other(devices, main, NoHardware, "OTHER2", NoneDeclined)!.Serial);
        Assert.Equal("OTHER1", PhonePick.Other(devices, main, NoHardware, "GONE", NoneDeclined)!.Serial);
        Assert.Equal("192.168.1.7:5555", PhonePick.Other([main, Wifi("192.168.1.7:5555")], main, NoHardware, null, NoneDeclined)!.Serial);
    }

    [Fact]
    public void OnePhoneOnUsbAndWifiIsNeverShownBesideItself()
    {
        var main = Usb("R5CR10ABCDE");
        // Wireless debugging's own name carries the phone's serial.
        var paired = Wifi("adb-R5CR10ABCDE-Xy12zW._adb-tls-connect._tcp");
        Assert.Null(PhonePick.Other([main, paired], main, NoHardware, null, NoneDeclined));

        // An address does not; once the phone's serial is read, it does.
        var address = Wifi("192.168.1.7:5555");
        Assert.Equal(address, PhonePick.Other([main, address], main, NoHardware, null, NoneDeclined));
        var read = new Dictionary<string, string> { ["192.168.1.7:5555"] = "r5cr10abcde" };
        Assert.Null(PhonePick.Other([main, address], main, read, null, NoneDeclined));
    }

    [Fact]
    public void AnotherPhoneOnTwoTransportsIsOneCandidateOnUsb()
    {
        var main = Usb("MAIN1");
        var hardware = new Dictionary<string, string> { ["192.168.1.9:5555"] = "PIXEL7" };
        var devices = new[] { main, Wifi("192.168.1.9:5555"), Usb("PIXEL7") };
        Assert.Equal("PIXEL7", PhonePick.Other(devices, main, hardware, "192.168.1.9:5555", NoneDeclined)!.Serial);
    }

    [Fact]
    public void AHardwareSerialIsTakenFromWhatIsKnown()
    {
        Assert.Equal("KNOWN", PhonePick.HardwareOf(Usb("S1"), new Dictionary<string, string> { ["S1"] = "KNOWN" }));
        Assert.Equal("S1", PhonePick.HardwareOf(Usb("S1"), new Dictionary<string, string> { ["S1"] = string.Empty }));
        Assert.Equal("R5CR", PhonePick.HardwareOf(Wifi("adb-R5CR-abc._adb-tls-connect._tcp"), NoHardware));
        Assert.Equal(string.Empty, PhonePick.HardwareOf(Wifi("10.0.0.2:5555"), NoHardware));
    }

    // ----- Names -----

    [Fact]
    public void TwoPhonesOfTheSameModelAreToldApart()
    {
        var names = PhoneNames.Distinct([
            ("R5CR10F7PP", "Galaxy S21 Ultra", string.Empty),
            ("192.168.1.4:5555", "Galaxy S21 Ultra", "R5CR22AB12"),
            ("PIXEL7", "Pixel 7", string.Empty),
        ]);
        Assert.Equal("Galaxy S21 Ultra ·F7PP", names["R5CR10F7PP"]);
        Assert.Equal("Galaxy S21 Ultra ·AB12", names["192.168.1.4:5555"]);
        Assert.Equal("Pixel 8 ·1.20", PhoneNames.Distinct([("192.168.1.20:5555", "Pixel 8", string.Empty), ("P8", "Pixel 8", string.Empty)])["192.168.1.20:5555"]);
        Assert.Equal("Pixel 7", names["PIXEL7"]);
    }

    [Theory]
    [InlineData("ABC", "ABC")]
    [InlineData("R5CR10F7PP", "F7PP")]
    [InlineData("192.168.1.15:5555", "1.15")]
    public void TheEndOfASerialIsThePhonesOwn(string serial, string end) => Assert.Equal(end, PhoneNames.End(serial));

    // ----- When one connects, and when its session ends -----

    [Theory]
    [InlineData(true, "ask", "", "", OtherPhoneStep.Ask)]
    [InlineData(true, "always", "", "", OtherPhoneStep.Show)]
    [InlineData(true, "never", "", "", OtherPhoneStep.Nothing)]
    [InlineData(true, "never", "", "P1", OtherPhoneStep.Show)]
    [InlineData(true, "ask", "never", "P1", OtherPhoneStep.Nothing)]
    [InlineData(false, "always", "", "P1", OtherPhoneStep.Nothing)]
    [InlineData(true, "ask", "", "P2", OtherPhoneStep.Ask)]
    public void WhatHappensWhenASecondPhoneConnects(bool enabled, string when, string showBeside, string remembered, OtherPhoneStep step)
    {
        var settings = new SecondPhoneSettings { Enabled = enabled, WhenConnected = when };
        Assert.Equal(step, SecondPhonePlan.OnConnect(settings, "P1", showBeside, remembered));
    }

    [Fact]
    public void TheRememberedPhoneIsOnlyBroughtBackWhenRememberingIsOn()
    {
        var settings = new SecondPhoneSettings { Remember = false, WhenConnected = "never" };
        Assert.Equal(OtherPhoneStep.Nothing, SecondPhonePlan.OnConnect(settings, "P1", null, "P1"));
        Assert.True(ShowBesideAnswers.IsValid(string.Empty));
        Assert.True(ShowBesideAnswers.IsValid(ShowBesideAnswers.Never));
        Assert.False(ShowBesideAnswers.IsValid("sometimes"));
    }

    [Fact]
    public void AfterItsSessionEndsItRestartsWaitsOrGivesUp()
    {
        var short_ = TimeSpan.FromSeconds(5);
        Assert.Equal((OtherPhoneAfterExit.Stopped, 2), SecondPhonePlan.AfterExit(true, true, true, 2, short_));
        Assert.Equal((OtherPhoneAfterExit.WaitForPhone, 2), SecondPhonePlan.AfterExit(false, false, true, 2, short_));
        Assert.Equal((OtherPhoneAfterExit.Restart, 3), SecondPhonePlan.AfterExit(false, true, true, 2, short_));
        Assert.Equal((OtherPhoneAfterExit.GiveUp, SecondPhonePlan.MostRestarts), SecondPhonePlan.AfterExit(false, true, true, SecondPhonePlan.MostRestarts, short_));
        Assert.Equal((OtherPhoneAfterExit.GiveUp, 0), SecondPhonePlan.AfterExit(false, true, false, 0, short_));
        // A session that ran for a while was working: its count starts again.
        Assert.Equal((OtherPhoneAfterExit.Restart, 1), SecondPhonePlan.AfterExit(false, true, true, SecondPhonePlan.MostRestarts, SecondPhonePlan.Recovered));
    }

    [Theory]
    [InlineData(true, true, 6, true)]
    [InlineData(true, true, 4, false)]
    [InlineData(true, false, 60, false)]
    [InlineData(false, true, 60, false)]
    public void ItPausesOnlyOnceTheWindowHasBeenOutOfSightForAMoment(bool pause, bool hidden, int seconds, bool pauses) =>
        Assert.Equal(pauses, SecondPhonePlan.PausesNow(pause, hidden, TimeSpan.FromSeconds(seconds)));

    // ----- Sharing the area -----

    [Fact]
    public void SideBySideBothPhonesHaveTheSameHeightInTheirOwnShapes()
    {
        var arrangement = TwoPhonesLayout.Arrange(1000, 700, 0.45, 0.50, "right", "side", 12);
        Assert.False(arrangement.Stacked);
        Assert.False(arrangement.Squeezed);
        var other = arrangement.Other!.Value;
        Assert.Equal(700, arrangement.Main.Height, 3);
        Assert.Equal(700, other.Height, 3);
        Assert.Equal(315, arrangement.Main.Width, 3);
        Assert.Equal(350, other.Width, 3);
        Assert.Equal(12, other.X - (arrangement.Main.X + arrangement.Main.Width), 3);
        // Centred: the space left over is the same on both sides.
        Assert.Equal(arrangement.Main.X, 1000 - (other.X + other.Width), 3);
    }

    [Fact]
    public void OnTheLeftTheOtherPhoneComesFirst()
    {
        var arrangement = TwoPhonesLayout.Arrange(1000, 700, 0.45, 0.50, "left", "side", 12);
        Assert.True(arrangement.Other!.Value.X < arrangement.Main.X);
        var stacked = TwoPhonesLayout.Arrange(700, 1400, 2.0, 2.2, "left", "stack", 12);
        Assert.True(stacked.Other!.Value.Y < stacked.Main.Y);
        Assert.False(TwoPhonesLayout.Arrange(700, 1400, 2.0, 2.2, "right", "stack", 12).Other!.Value.Y < stacked.Main.Y);
    }

    [Fact]
    public void StackedBothHaveTheSameWidth()
    {
        var arrangement = TwoPhonesLayout.Arrange(800, 900, 2.0, 2.2, "right", "stack", 10);
        Assert.True(arrangement.Stacked);
        Assert.Equal(arrangement.Main.Width, arrangement.Other!.Value.Width, 3);
        Assert.Equal(2.0, arrangement.Main.Width / arrangement.Main.Height, 3);
        Assert.Equal(2.2, arrangement.Other.Value.Width / arrangement.Other.Value.Height, 3);
    }

    [Fact]
    public void AutoTakesWhicheverLeavesTheSmallerPhoneLarger()
    {
        // Two upright phones in a wide area sit side by side; two landscape pictures in a tall one stack.
        Assert.False(TwoPhonesLayout.Arrange(1200, 700, 0.45, 0.45, "right", "auto", 12).Stacked);
        Assert.True(TwoPhonesLayout.Arrange(700, 1200, 2.0, 2.0, "right", "auto", 12).Stacked);
    }

    [Fact]
    public void WithoutRoomForBothOnlyTheMainPhoneShows()
    {
        var squeezed = TwoPhonesLayout.Arrange(200, 700, 0.45, 0.50, "right", "side", 12);
        Assert.True(squeezed.Squeezed);
        Assert.Equal(new RectD(0, 0, 200, 700), squeezed.Main);
        Assert.True(TwoPhonesLayout.Arrange(200, 700, 0.45, 0.50, "right", "stack", 12).Stacked);
        Assert.True(TwoPhonesLayout.Arrange(0, 700, 0.45, 0.50, "right", "auto", 12).Squeezed);
        Assert.True(TwoPhonesLayout.Arrange(1000, 700, 0, 0.50, "right", "auto", 12).Squeezed);
    }

    [Fact]
    public void TwoPhonesTogetherAreOnePictureBesideASecondScreen()
    {
        Assert.Equal(0.95, TwoPhonesLayout.PairAspect(0.45, 0.50, stacked: false), 6);
        Assert.Equal(1 / (1 / 2.0 + 1 / 2.0), TwoPhonesLayout.PairAspect(2.0, 2.0, stacked: true), 6);
        Assert.Equal(0.45, TwoPhonesLayout.PairAspect(0.45, 0, stacked: false), 6);
        Assert.Equal(0, TwoPhonesLayout.PairAspect(-1, 0.5, stacked: false), 6);
    }

    // ----- Its own session -----

    [Fact]
    public void TheOtherPhoneIsAFullSessionOfItsOwnOnItsOwnPort()
    {
        var config = new RexConfig();
        config.Session.TurnScreenOff = true;
        config.Session.StartApp = "com.example.one";
        config.App.ShowFrameRate = true;
        var args = ScrcpyArguments.BuildOtherPhone(config, "OTHER1", false, "T", (1, 2, 300, 600));
        Assert.Contains("--serial=OTHER1", args);
        Assert.Contains("--port=27200", args);
        Assert.Single(args, a => a.StartsWith("--port=", StringComparison.Ordinal));
        Assert.Contains("--turn-screen-off", args);
        Assert.Contains("--stay-awake", args);
        Assert.DoesNotContain("--no-cleanup", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--start-app=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a == "--print-fps");
        // The main phone's sound only, by default.
        Assert.Contains("--no-audio", args);
        Assert.Contains("--window-width=300", args);
        // The settings it was built from are left as they were.
        Assert.Equal("com.example.one", config.Session.StartApp);
    }

    [Fact]
    public void ItsScreenResolutionBitRateAndSoundFollowItsOwnSettings()
    {
        var config = new RexConfig();
        config.Session.TurnScreenOff = true;
        config.SecondPhone.ScreenOff = "on";
        config.SecondPhone.MaxSize = 1280;
        config.SecondPhone.BitRate = "4M";
        config.SecondPhone.Sound = "both";
        var args = ScrcpyArguments.BuildOtherPhone(config, "OTHER1", true, "T", null);
        Assert.DoesNotContain("--turn-screen-off", args);
        Assert.Contains("--max-size=1280", args);
        Assert.Contains("--video-bit-rate=4M", args);
        Assert.DoesNotContain("--no-audio", args);
        // On Wi-Fi it does not keep the phone awake, as the main session does not.
        Assert.DoesNotContain("--stay-awake", args);

        config.Mirror.Audio = false;
        Assert.Contains("--no-audio", ScrcpyArguments.BuildOtherPhone(config, "OTHER1", true, "T", null));
    }

    // ----- Where actions go -----

    [Theory]
    [InlineData("home", RouteKind.OtherPhoneAdb)]
    [InlineData("volume-up", RouteKind.OtherPhoneAdb)]
    [InlineData("rotation-landscape", RouteKind.OtherPhoneAdb)]
    [InlineData("rotate-left", RouteKind.OtherPhoneSession)]
    [InlineData("paste", RouteKind.OtherPhoneSession)]
    [InlineData("copy-add", RouteKind.Refuse)]
    [InlineData("copy-remove", RouteKind.Refuse)]
    [InlineData("second-screen", RouteKind.Refuse)]
    [InlineData("screenshot", RouteKind.AsUsual)]
    [InlineData("zoom-in", RouteKind.AsUsual)]
    [InlineData("not-an-action", RouteKind.AsUsual)]
    public void EachActionGoesToThePhoneInUse(string id, RouteKind kind)
    {
        var route = ActionRouting.ForOtherPhone(id);
        Assert.Equal(kind, route.Kind);
        Assert.Equal(kind == RouteKind.OtherPhoneSession, route.Shortcut is not null);
        Assert.Equal(kind == RouteKind.Refuse, route.Why is not null);
    }

    [Fact]
    public void EveryScrcpyActionHasAPlaceOnTheOtherPhone()
    {
        foreach (var action in MirrorActions.All.Where(a => a.Kind == ActionKind.Scrcpy))
        {
            Assert.NotEqual(RouteKind.Refuse, ActionRouting.ForOtherPhone(action.Id).Kind);
        }
    }

    // ----- Whose sound plays -----

    [Fact]
    public void ThePhoneNotInUseIsQuietWhenOnlyThePhoneInUseIsHeard()
    {
        var settings = new SoundSettings { MuteWhenLocked = true };
        var quiet = SoundPolicy.Decide(new SoundInputs(0.8, false, false, false, true, TimeSpan.MaxValue, OtherPhoneInUse: true), settings);
        Assert.True(quiet.Muted);
        // Before the locked PC: it is the stronger reason, and the one said.
        Assert.Equal(SoundPolicy.WhyOtherPhone, quiet.Why);
        Assert.Null(SoundPolicy.Decide(new SoundInputs(0.8, true, false, false, false, TimeSpan.MaxValue, OtherPhoneInUse: true), settings).Why);
        Assert.False(SoundPolicy.Decide(new SoundInputs(0.8, false, false, false, false, TimeSpan.MaxValue), settings).Muted);
    }
}
