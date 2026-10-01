using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// The phone's sound on this PC without a window: what the rules make of it, how it fades and is
/// balanced, which settings are kept sane, what scrcpy's warnings mean, and where a choice is kept.
/// </summary>
public sealed class SoundTests
{
    private static readonly TimeSpan LongAgo = TimeSpan.FromHours(1);

    [Theory]
    // Nothing acting.
    [InlineData(false, false, false, false, 9999, "", 0.8, false, null)]
    // Each rule alone, switched on.
    [InlineData(false, true, false, false, 9999, "hidden", 0.8, true, SoundPolicy.WhyHidden)]
    [InlineData(false, false, true, false, 9999, "behind", 0.8, true, SoundPolicy.WhyBehind)]
    [InlineData(false, false, false, true, 9999, "locked", 0.8, true, SoundPolicy.WhyLocked)]
    [InlineData(false, false, false, false, 100, "typing", 0.24, false, SoundPolicy.WhyTyping)]
    // Each rule's condition without its switch does nothing.
    [InlineData(false, true, false, false, 9999, "", 0.8, false, null)]
    [InlineData(false, false, true, false, 9999, "", 0.8, false, null)]
    [InlineData(false, false, false, true, 9999, "", 0.8, false, null)]
    [InlineData(false, false, false, false, 100, "", 0.8, false, null)]
    // The person's mute comes first and needs no words.
    [InlineData(true, false, false, false, 9999, "", 0.8, true, null)]
    [InlineData(true, true, true, true, 0, "hidden behind locked typing", 0.8, true, null)]
    // The order of reasons: locked, hidden, behind, then typing.
    [InlineData(false, true, true, true, 0, "hidden behind locked typing", 0.8, true, SoundPolicy.WhyLocked)]
    [InlineData(false, true, true, false, 0, "hidden behind locked typing", 0.8, true, SoundPolicy.WhyHidden)]
    [InlineData(false, false, true, false, 0, "hidden behind locked typing", 0.8, true, SoundPolicy.WhyBehind)]
    [InlineData(false, true, false, false, 0, "behind typing", 0.24, false, SoundPolicy.WhyTyping)]
    [InlineData(false, false, true, false, 0, "hidden typing", 0.24, false, SoundPolicy.WhyTyping)]
    [InlineData(false, true, false, true, 9999, "hidden locked", 0.8, true, SoundPolicy.WhyLocked)]
    [InlineData(false, false, true, true, 9999, "behind", 0.8, true, SoundPolicy.WhyBehind)]
    // Lowering lasts exactly as long as it is set to.
    [InlineData(false, false, false, false, 799, "typing", 0.24, false, SoundPolicy.WhyTyping)]
    [InlineData(false, false, false, false, 800, "typing", 0.8, false, null)]
    [InlineData(false, false, false, false, 801, "typing", 0.8, false, null)]
    [InlineData(false, false, false, false, 0, "typing", 0.24, false, SoundPolicy.WhyTyping)]
    [InlineData(false, false, false, false, 0, "typing full", 0.8, false, null)]
    [InlineData(false, false, false, false, 0, "typing silent", 0, false, SoundPolicy.WhyTyping)]
    public void WhatTheSoundShouldBeNow(bool muted, bool hidden, bool behind, bool locked, int sinceKeyMs, string rules,
        double level, bool silent, string? why)
    {
        var settings = new SoundSettings
        {
            MuteWhenHidden = rules.Contains("hidden", StringComparison.Ordinal),
            MuteWhenBehind = rules.Contains("behind", StringComparison.Ordinal),
            MuteWhenLocked = rules.Contains("locked", StringComparison.Ordinal),
            LowerWhileTyping = rules.Contains("typing", StringComparison.Ordinal),
            LowerTo = rules.Contains("full", StringComparison.Ordinal) ? 1 : rules.Contains("silent", StringComparison.Ordinal) ? 0 : 0.3,
        };

        var target = SoundPolicy.Decide(new SoundInputs(0.8, muted, hidden, behind, locked, TimeSpan.FromMilliseconds(sinceKeyMs)), settings);

        Assert.Equal(level, target.Level, 6);
        Assert.Equal(silent, target.Muted);
        Assert.Equal(why, target.Why);
    }

    [Fact]
    public void AFadeArrivesExactlyAndNeverOvershoots()
    {
        var tick = TimeSpan.FromMilliseconds(33);
        var level = 0.0;
        var steps = 0;
        while (level < 1)
        {
            level = SoundFade.Next(level, 1, tick, 150);
            steps++;
            Assert.InRange(level, 0, 1);
        }

        Assert.Equal(1, level);
        Assert.Equal(5, steps);

        // Down as well, and a target that changes halfway is followed from where the level is.
        level = SoundFade.Next(1, 0.2, TimeSpan.FromMilliseconds(75), 150);
        Assert.Equal(0.5, level, 6);
        level = SoundFade.Next(level, 0.8, TimeSpan.FromMilliseconds(75), 150);
        Assert.Equal(0.8, level, 6);
        Assert.Equal(0.2, SoundFade.Next(1, 0.2, TimeSpan.FromSeconds(10), 150));

        // No fade arrives at once; time going backwards moves nothing.
        Assert.Equal(0.3, SoundFade.Next(1, 0.3, TimeSpan.Zero, 0));
        Assert.Equal(0.6, SoundFade.Next(0.6, 1, TimeSpan.FromMilliseconds(-50), 150));
    }

    [Theory]
    [InlineData(-1.0, 1f, 0f)]
    [InlineData(-0.5, 1f, 0.5f)]
    [InlineData(0.0, 1f, 1f)]
    [InlineData(0.5, 0.5f, 1f)]
    [InlineData(1.0, 0f, 1f)]
    [InlineData(5.0, 0f, 1f)]
    [InlineData(double.NaN, 1f, 1f)]
    public void BalanceSplitsTheChannels(double balance, float left, float right)
    {
        Assert.Equal((left, right), SoundBalance.Channels(balance));
    }

    [Fact]
    public void SoundSettingsAreKeptSane()
    {
        var wild = new SoundSettings
        {
            Volume = 3,
            Step = 0,
            LowerTo = double.NaN,
            LowerForMs = 1,
            FadeMs = 99_999,
            Balance = -7,
        };
        wild.Normalize();
        Assert.Equal((1.0, SoundSettings.SmallestStep, 0.3, SoundSettings.ShortestLowering, SoundSettings.LongestFade, -1.0),
            (wild.Volume, wild.Step, wild.LowerTo, wild.LowerForMs, wild.FadeMs, wild.Balance));

        var other = new SoundSettings { Volume = double.NaN, Step = 9, LowerTo = -1, LowerForMs = 99_999, FadeMs = -5, Balance = double.PositiveInfinity };
        other.Normalize();
        Assert.Equal((1.0, SoundSettings.LargestStep, 0.0, SoundSettings.LongestLowering, 0, 0.0),
            (other.Volume, other.Step, other.LowerTo, other.LowerForMs, other.FadeMs, other.Balance));

        var nan = new SoundSettings { Step = double.NaN };
        nan.Normalize();
        Assert.Equal(0.05, nan.Step);

        var copy = new SoundSettings().Copy();
        Assert.Equal(new SoundSettings(), copy);
    }

    [Theory]
    [InlineData("WARN: Audio disabled: it is not supported before Android 11", SoundProblems.TooOld)]
    [InlineData("[server] WARN: On Android 11, audio capture must be started in the foreground, make sure that the device is unlocked when starting scrcpy.", SoundProblems.Locked)]
    [InlineData("[server] ERROR: Failed to start audio capture", SoundProblems.Refused)]
    [InlineData("WARN: Audio capture failed", SoundProblems.Refused)]
    [InlineData("WARN: Demuxer 'audio': stream explicitly disabled by the device", SoundProblems.Refused)]
    [InlineData("INFO: Texture: 1080x2400", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ScrcpysAudioWarningsBecomeWords(string? line, string? words)
    {
        Assert.Equal(words, SoundProblems.FromScrcpy(line));
    }

    [Fact]
    public void TheVolumeIsRememberedWhereTheSettingSays()
    {
        var perPhone = new SoundSettings { Volume = 0.7, Muted = false };
        var known = new DeviceProfile { SoundVolume = 0.4, SoundMuted = true };

        Assert.True(SoundMemory.PerPhone(perPhone, "R3CR"));
        Assert.False(SoundMemory.PerPhone(perPhone, null));
        Assert.False(SoundMemory.PerPhone(perPhone, string.Empty));
        Assert.Equal((0.4, true), SoundMemory.StartWith(perPhone, known));
        // A phone not yet remembered, or none at all, starts where Settings says.
        Assert.Equal((0.7, false), SoundMemory.StartWith(perPhone, new DeviceProfile()));
        Assert.Equal((0.7, false), SoundMemory.StartWith(perPhone, null));
        // A remembered value out of range is not trusted.
        Assert.Equal((0.7, true), SoundMemory.StartWith(perPhone, new DeviceProfile { SoundVolume = double.NaN, SoundMuted = true }));

        var shared = new SoundSettings { Volume = 0.7, RememberPerPhone = false };
        Assert.False(SoundMemory.PerPhone(shared, "R3CR"));
        Assert.Equal((0.7, false), SoundMemory.StartWith(shared, known));

        // Starting muted wins over anything remembered.
        Assert.Equal((0.4, true), SoundMemory.StartWith(perPhone with { StartMuted = true }, known with { SoundMuted = false }));
    }

    [Fact]
    public void APhonesSoundIsKeptInState()
    {
        using var package = new Support.TestPackage();
        var store = new StateStore(package.Paths.State);

        store.SetSound("R3CR", 0.35, true);
        store.SetSound("R3CR", 7, false);

        var profile = new StateStore(package.Paths.State).GetDevice("R3CR")!;
        Assert.Equal((1.0, false), (profile.SoundVolume, profile.SoundMuted));
    }

    [Theory]
    [InlineData("0", true, 0.0)]
    [InlineData("40", true, 0.4)]
    [InlineData("100", true, 1.0)]
    [InlineData("101", false, null)]
    [InlineData("-5", false, null)]
    [InlineData("4.5", false, null)]
    [InlineData("up", true, null)]
    [InlineData("TOGGLE", true, null)]
    [InlineData("louder", false, null)]
    public void TheSoundCommandTakesALevelOrAVerb(string verb, bool valid, double? level)
    {
        Assert.Equal(valid, SoundCommand.IsValid(verb));
        Assert.Equal(level, SoundCommand.Level(verb));
    }
}
