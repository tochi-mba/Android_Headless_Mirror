using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// The phone's sound on this PC through the window: the top bar's sound button and its panel, the
/// Sound on this PC group, and what the panel says when there is no sound to control.
/// </summary>
public sealed partial class AppUiTests
{
    private static JsonNode SoundOf(JsonObject status) => status["sound"]!;

    [Fact(Timeout = 120_000)]
    public async Task SoundPanel_SetsTheVolumeAndMutes()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitForStatusAsync(s => SoundOf(s)["available"]!.GetValue<bool>(), Startup, "the phone's sound");

        app.Ui.Invoke("QuickSound");
        await app.WaitForStatusAsync(s => SoundOf(s)["panelOpen"]!.GetValue<bool>(), Soon, "the sound panel");
        app.Ui.SetValue("SoundVolume", 40);
        await app.WaitUntilAsync(() => package.Sound() is { Volume: 0.4 }, Soon, "the fake mixer at 40%");
        Assert.Equal("40%", app.Ui.Read("SoundVolumeValue", i => i.Name));
        await app.SaveScreenshotAsync("ui-sound-panel.png");

        app.Ui.Invoke("SoundMute");
        await app.WaitUntilAsync(() => package.Sound() is { Muted: true }, Soon, "the fake mixer muted");
        Assert.Equal("Unmute", app.Ui.Read("SoundMute", i => i.Name).Split(' ')[0]);

        // Esc closes the panel and the keyboard goes back to the phone.
        app.Ui.Focus("SoundVolume");
        await app.PressChordAsync(0x1B);
        await app.WaitForStatusAsync(s => !SoundOf(s)["panelOpen"]!.GetValue<bool>(), Soon, "the panel to close");
        await app.QuitAsync();

        // Kept for this phone, in state.json.
        var profile = new StateStore(package.Paths.State).GetDevice("FAKE123")!;
        Assert.Equal(0.4, profile.SoundVolume);
        Assert.True(profile.SoundMuted);
    }

    [Fact(Timeout = 120_000)]
    public async Task SoundPanel_SaysWhyThereIsNoSound()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.NoAudioMarker, string.Empty);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        await app.WaitForStatusAsync(s => SoundOf(s)["why"]?.GetValue<string>() == SoundProblems.TooOld, Startup, "the phone to say it cannot send its sound");

        app.Ui.Invoke("QuickSound");
        await app.WaitUntilAsync(() => app.Ui.Read("SoundProblem", i => i.Name) == SoundProblems.TooOld, Soon, "the panel to say why");
        Assert.False(app.Ui.Exists("SoundVolume") && app.Ui.Read("SoundVolume", i => !i.IsOffscreen));
        Assert.Contains("needs Android 11", app.Ui.Read("QuickSound", i => i.HelpText), StringComparison.Ordinal);

        // With phone sound off, the panel offers to turn it on, which asks for a restart. The mirror
        // is started again without sound first: until then it still has its sound, and turning the
        // setting back on would need no restart at all.
        new ConfigStore(package.Paths.Config).Set("Mirror.Audio", "false");
        await app.WaitForStatusAsync(s => SoundOf(s)["why"]?.GetValue<string>() == SoundProblems.AudioOff, Soon, "the sound to be off");
        await app.WaitForStatusAsync(s => s["restartRequired"]!.GetValue<bool>(), Soon, "the offer to restart without sound");
        Assert.True((await app.SendAsync(new IpcRequest("session-restart"))).Ok);
        await app.WaitForStatusAsync(s => s["mirroring"]!.GetValue<bool>() && !s["restartRequired"]!.GetValue<bool>(), Startup, "the mirror without sound");
        // The restart may close the panel as the window changes hands; the button opens it again.
        await app.WaitUntilAsync(() =>
        {
            if (!app.Ui.Exists("SoundProblem"))
            {
                app.Ui.Invoke("QuickSound");
                return false;
            }

            return app.Ui.Read("SoundProblem", i => i.Name) == SoundProblems.AudioOff;
        }, Soon, "the panel to say sound is off");
        app.Ui.Invoke("SoundTurnOn");
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Mirror.Audio, Soon, "phone sound to be turned on");
        await app.WaitForStatusAsync(s => s["restartRequired"]!.GetValue<bool>(), Soon, "the offer to restart");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task SoundPanel_SettingsLinkOpensTheGroup()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabControls");

        app.Ui.Invoke("QuickSound");
        app.Ui.Invoke("SoundSettingsLink");

        await app.WaitForStatusAsync(s => s["sidebarTab"]!.GetValue<string>() == "settings" && !SoundOf(s)["panelOpen"]!.GetValue<bool>(), Soon, "the Settings tab");
        await app.WaitUntilAsync(() => app.Ui.IsExpanded("GroupSound"), Soon, "the Sound group to open");
        Assert.False(app.Ui.Read("SoundLevel", i => i.IsOffscreen));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Settings_SoundRowsSaveAndTheButtonFollowsItsChip()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupSound");

        foreach (var id in new[] { "SoundMuted", "SoundStartMuted", "SoundMuteHidden", "SoundMuteBehind", "SoundMuteLocked", "SoundLowerTyping" })
        {
            app.Ui.Toggle(id, on: true);
        }

        foreach (var id in new[] { "SoundPerPhone", "SoundFollowMixer", "SoundShowLevel", "SoundWheel" })
        {
            app.Ui.Toggle(id, on: false);
        }

        app.Ui.SetValue("SoundLevel", 0.7);
        app.Ui.SetValue("SoundStep", 0.1);
        app.Ui.SetValue("SoundLowerTo", 0.5);
        app.Ui.SetValue("SoundLowerFor", 2000);
        app.Ui.SetValue("SoundFade", 300);
        app.Ui.SetValue("SoundBalance", -0.4);
        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Sound.Balance == -0.4, Soon, "the sliders to be saved");

        var sound = ConfigFile.Load(package.Paths.Config).Sound;
        Assert.True(sound.Muted && sound.StartMuted && sound.MuteWhenHidden && sound.MuteWhenBehind && sound.MuteWhenLocked && sound.LowerWhileTyping);
        Assert.False(sound.RememberPerPhone || sound.FollowMixer || sound.ShowLevel || sound.WheelOnButton);
        Assert.Equal((0.7, 0.1, 0.5, 2000, 300), (sound.Volume, sound.Step, sound.LowerTo, sound.LowerForMs, sound.FadeMs));
        // The balance reaches the phone's sound at once.
        await app.WaitUntilAsync(() => package.Sound() is { Left: 1, Right: 0.6 }, Soon, "the balance on the fake mixer");

        // The top bar's sound button follows its chip.
        app.Ui.ExpandGroup("GroupStartup");
        Assert.True(app.Ui.Exists("QuickSound"));
        app.Ui.Toggle("quick-button sound", on: false);
        await app.WaitUntilAsync(() => !app.Ui.Exists("QuickSound"), Soon, "the sound button to go");
        app.Ui.Toggle("quick-button sound", on: true);
        await app.WaitUntilAsync(() => app.Ui.Exists("QuickSound"), Soon, "the sound button to come back");
        await app.QuitAsync();
    }
}
