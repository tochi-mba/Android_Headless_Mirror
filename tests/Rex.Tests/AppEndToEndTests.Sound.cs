using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The phone's sound on this PC against the fake audio session: the rules that mute or lower it, a
/// volume remembered for the phone and applied again after a restart, changes made in the Windows
/// mixer, the keys, the command line, and a key from anywhere.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static readonly TimeSpan SoundTimeout = TimeSpan.FromSeconds(10);
    private const byte KeyPageUp = 0x21, KeyPageDown = 0x22;

    private static async Task<AppProcess> StartWithSoundAsync(TestPackage package)
    {
        var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(s => s["sound"]!["available"]!.GetValue<bool>(), StartupTimeout, "the phone's sound");
        return app;
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_MutesWhileHiddenOrBehindAndComesBack()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Sound.MuteWhenHidden = true;
            c.Sound.MuteWhenBehind = true;
        });
        using var app = await StartWithSoundAsync(package);
        await app.FocusAsync();
        await app.WaitUntilAsync(() => package.Sound() is { Muted: false }, SoundTimeout, "sound while the window is in front");

        await app.PutAnotherWindowInFrontAsync();
        var behind = await app.WaitForStatusAsync(s => s["sound"]!["mutedBy"]?.GetValue<string>() == SoundPolicy.WhyBehind, SoundTimeout, "muted while behind");
        Assert.False(behind["sound"]!["muted"]!.GetValue<bool>());
        await app.WaitUntilAsync(() => package.Sound() is { Muted: true }, SoundTimeout, "the fake mixer muted");

        await app.FocusAsync();
        await app.WaitUntilAsync(() => package.Sound() is { Muted: false }, SoundTimeout, "sound back in front");

        await app.SendAsync(new IpcRequest("hide"));
        await app.WaitForStatusAsync(s => s["sound"]!["mutedBy"]?.GetValue<string>() == SoundPolicy.WhyHidden, SoundTimeout, "muted while hidden");
        await app.WaitUntilAsync(() => package.Sound() is { Muted: true }, SoundTimeout, "the fake mixer muted while hidden");
        await app.SendAsync(new IpcRequest("show"));
        await app.FocusAsync();
        await app.WaitUntilAsync(() => package.Sound() is { Muted: false }, SoundTimeout, "sound back once shown");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_LowersWhileTypingAndComesBack()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Sound.LowerWhileTyping = true;
            c.Sound.LowerTo = 0.3;
            c.Sound.LowerForMs = 1500;
            c.Sound.FadeMs = 0;
        });
        using var app = await StartWithSoundAsync(package);
        await app.FocusAsync();

        await app.PressKeyAsync((byte)'A');
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.3 }, SoundTimeout, "the sound lowered while typing");
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 1 }, SoundTimeout, "the sound back after the last key");
        await app.QuitAsync();
    }

    [Fact(Timeout = 240_000)]
    public async Task Sound_IsRememberedForThePhoneAndAppliedAgainAfterARestart()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using (var first = await StartWithSoundAsync(package))
        {
            Assert.True((await first.SendAsync(new IpcRequest("sound", new Dictionary<string, string> { ["verb"] = "40" }))).Ok);
            await first.WaitUntilAsync(() => package.Sound() is { Volume: 0.4 }, SoundTimeout, "40% on the fake mixer");

            // A new mirror is a new audio session at full volume; the phone's own level is set on it again.
            File.WriteAllText(package.SoundFile, """{"available":true,"volume":1,"muted":false,"channels":2}""");
            await first.SendAsync(new IpcRequest("session-restart"));
            await first.WaitForPhaseAsync("mirroring", StartupTimeout);
            await first.WaitUntilAsync(() => package.Sound() is { Volume: 0.4 }, SoundTimeout, "40% again after the restart");
            await first.QuitAsync();
        }

        Assert.Equal(0.4, new StateStore(package.Paths.State).GetDevice("FAKE123")!.SoundVolume);
        File.WriteAllText(package.SoundFile, """{"available":true,"volume":1,"muted":false,"channels":2}""");
        using var second = await StartWithSoundAsync(package);
        await second.WaitUntilAsync(() => package.Sound() is { Volume: 0.4 }, SoundTimeout, "the phone's 40% at the next start");
        await second.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_FollowsTheWindowsMixerOrPutsItBack()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Sound.FadeMs = 0);
        using var app = await StartWithSoundAsync(package);

        package.ChangeSoundOutside(0.5, muted: false);
        await app.WaitForStatusAsync(s => s["sound"]!["volume"]!.GetValue<double>() == 0.5, SoundTimeout, "the mixer's 50% adopted");

        new ConfigStore(package.Paths.Config).Set("Sound.FollowMixer", "false");
        await app.WaitForStatusAsync(s => !s["sound"]!["followMixer"]!.GetValue<bool>(), SoundTimeout, "the window to stop following the mixer");
        package.ChangeSoundOutside(0.1, muted: true);
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.5, Muted: false }, SoundTimeout, "the app's level put back");
        Assert.Equal(0.5, (await app.SendAsync(new IpcRequest("status"))).Data!["sound"]!["volume"]!.GetValue<double>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_KeysAndActionsChangeIt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Sound.FadeMs = 0);
        using var app = await StartWithSoundAsync(package);
        await app.FocusAsync();

        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyPageDown);
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.95 }, SoundTimeout, "one step quieter");
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyPageDown);
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.9 }, SoundTimeout, "two steps quieter");
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyPageUp);
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.95 }, SoundTimeout, "a step louder");
        await app.PressChordAsync(KeyCtrl, KeyAlt, KeyShift, KeyM);
        await app.WaitUntilAsync(() => package.Sound() is { Muted: true }, SoundTimeout, "muted by the key");

        // The phone's own mute is another action: this PC's mute does not send it.
        Assert.DoesNotContain(package.AdbCalls(), line => line.Contains("KEYCODE_VOLUME_MUTE", StringComparison.Ordinal));

        await app.ActionAsync("sound-mute");
        await app.WaitUntilAsync(() => package.Sound() is { Muted: false }, SoundTimeout, "unmuted by the action");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_TheCommandLineSetsAndReportsIt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Sound.FadeMs = 0);
        using var app = await StartWithSoundAsync(package);

        Assert.Contains("25%", await app.RunCliAsync("sound", "25"), StringComparison.Ordinal);
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.25 }, SoundTimeout, "25% from the command line");
        Assert.Contains("muted", await app.RunCliAsync("sound", "mute"), StringComparison.Ordinal);
        Assert.Contains("\"muted\":true", await app.RunCliAsync("agent", "sound"), StringComparison.Ordinal);
        Assert.Contains("Usage", await app.RunCliAsync("sound", "louder"), StringComparison.Ordinal);
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_StartsMutedWhenAsked()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Sound.StartMuted = true);
        using var app = await StartWithSoundAsync(package);

        await app.WaitUntilAsync(() => package.Sound() is { Muted: true }, SoundTimeout, "the mirror to start muted");
        Assert.True((await app.SendAsync(new IpcRequest("status"))).Data!["sound"]!["muted"]!.GetValue<bool>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Sound_MutesFromAnywhere()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
            c.GlobalKeys.Actions = [new GlobalKeyAction { Key = "Ctrl+Shift+F10", Action = "sound-mute" }]);
        using var app = await StartWithSoundAsync(package);
        await app.SendAsync(new IpcRequest("hide"));
        await app.WaitForStatusAsync(s => !s["windowVisible"]!.GetValue<bool>(), SoundTimeout, "the window to hide");

        await app.PressChordAsync(KeyCtrl, KeyShift, KeyF10);

        await app.WaitUntilAsync(() => package.Sound() is { Muted: true }, SoundTimeout, "muted from anywhere");
        await app.QuitAsync();
    }
}
