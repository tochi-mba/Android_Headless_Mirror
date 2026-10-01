using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// Keys from anywhere without a window: how a chord is read and written, which keys may work from
/// anywhere, what the show-or-hide key does, how a shortcut box records, and the settings' rules.
/// </summary>
public sealed class GlobalKeysTests
{
    private const int M = 'M';

    [Theory]
    [InlineData("Ctrl+Alt+M", "Ctrl+Alt+M")]
    [InlineData("ctrl+alt+m", "Ctrl+Alt+M")]
    [InlineData(" Ctrl + Alt + M ", "Ctrl+Alt+M")]
    [InlineData("Alt+Ctrl+M", "Ctrl+Alt+M")]
    [InlineData("Control+Shift+F9", "Ctrl+Shift+F9")]
    [InlineData("Windows+Ctrl+Q", "Ctrl+Win+Q")]
    [InlineData("Ctrl+Alt+Shift+Win+Z", "Ctrl+Alt+Shift+Win+Z")]
    [InlineData("F13", "F13")]
    [InlineData("f24", "F24")]
    [InlineData("Ctrl+PgUp", "Ctrl+PageUp")]
    [InlineData("Ctrl+PgDn", "Ctrl+PageDown")]
    [InlineData("Ctrl+Del", "Ctrl+Delete")]
    [InlineData("Ctrl+Ins", "Ctrl+Insert")]
    [InlineData("Ctrl+Return", "Ctrl+Enter")]
    [InlineData("Escape", "Esc")]
    [InlineData("Ctrl+Alt+Plus", "Ctrl+Alt+Plus")]
    [InlineData("Ctrl+Alt+Minus", "Ctrl+Alt+Minus")]
    [InlineData("Ctrl+Alt+0", "Ctrl+Alt+0")]
    [InlineData("Ctrl+Alt+Backspace", "Ctrl+Alt+Backspace")]
    [InlineData("Ctrl+Space", "Ctrl+Space")]
    [InlineData("Ctrl+Comma", "Ctrl+Comma")]
    [InlineData("Ctrl+Period", "Ctrl+Period")]
    [InlineData("Ctrl+Num7", "Ctrl+Num7")]
    [InlineData("Ctrl+NumPlus", "Ctrl+NumPlus")]
    [InlineData("Ctrl+Pause", "Ctrl+Pause")]
    [InlineData("Ctrl+ScrollLock", "Ctrl+ScrollLock")]
    public void AChordIsReadAndWrittenTheSameWay(string written, string expected)
    {
        var chord = KeyChord.Parse(written);

        Assert.Equal(expected, chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }

    [Fact]
    public void AChordKnowsItsModifiersAndMatchesOnlyThem()
    {
        var chord = KeyChord.Parse("Ctrl+Alt+M");

        Assert.True(chord.Ctrl && chord.Alt && !chord.Shift && !chord.Win);
        Assert.True(chord.Matches(M, KeyMods.Ctrl | KeyMods.Alt));
        Assert.False(chord.Matches(M, KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift));
        Assert.False(chord.Matches('N', KeyMods.Ctrl | KeyMods.Alt));
        Assert.True(KeyChord.IsModifier(0xA2));
        Assert.False(KeyChord.IsModifier(M));
        Assert.True(KeyChord.IsNamed(M));
        Assert.False(KeyChord.IsNamed(0xDE));
        // A key with no name of its own is still written down rather than lost.
        Assert.Equal("Ctrl+0xDE", new KeyChord(KeyMods.Ctrl, 0xDE).ToString());
        Assert.Throws<FormatException>(() => KeyChord.Parse("Ctrl+Nothing"));
    }

    [Theory]
    [InlineData("Alt+Q")]
    [InlineData("Win+Q")]
    [InlineData("Shift+F9")]
    [InlineData("Alt+Shift+Q")]
    [InlineData("F12")]
    [InlineData("Shift+F13")]
    [InlineData("Q")]
    public void AKeyFromAnywhereNeedsCtrlUnlessItIsF13ToF24(string written)
    {
        Assert.Contains("needs Ctrl", GlobalKeyRules.WhyNot(written, "screenshot", []), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("F13")]
    [InlineData("F24")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Ctrl+Alt+M")]
    [InlineData("Ctrl+Win+Q")]
    public void KeysWithCtrlAndF13ToF24AloneAreAllowed(string written)
    {
        Assert.Null(GlobalKeyRules.WhyNot(written, GlobalKeyRules.ShowHide, []));
    }

    public static TheoryData<string> WindowsOwn
    {
        get
        {
            var rows = new TheoryData<string>();
            foreach (var chord in GlobalKeyRules.WindowsOwn)
            {
                rows.Add(chord.ToString());
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(WindowsOwn))]
    public void WindowsOwnKeysAreNeverTaken(string written)
    {
        // Ctrl+Alt+Delete and its kin are refused as Windows' own, whatever else they are.
        var why = GlobalKeyRules.WhyNot(written, "screenshot", []);
        Assert.True(why is "Windows uses this key itself." || why!.Contains("cannot be part", StringComparison.Ordinal), why);
    }

    [Theory]
    [InlineData("Esc")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Ctrl+PrintScreen")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl++")]
    [InlineData("Ctrl+Alt+Q+W")]
    [InlineData("Ctrl+Banana")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void KeysThatCannotBeShortcutsAreRefused(string? written)
    {
        Assert.NotNull(GlobalKeyRules.WhyNot(written, GlobalKeyRules.ShowHide, []));
    }

    [Fact]
    public void AKeyTheWindowUsesForSomethingElseIsRefused()
    {
        Assert.Equal("In this window the key already means: Save a screenshot.", GlobalKeyRules.WhyNot("Ctrl+Alt+S", "home", []));
        Assert.Null(GlobalKeyRules.WhyNot("Ctrl+Alt+S", "screenshot", []));
        Assert.Contains("Show or hide the side panel", GlobalKeyRules.WhyNot("Ctrl+Alt+B", GlobalKeyRules.ShowHide, []), StringComparison.Ordinal);
        Assert.Equal("Another shortcut from anywhere uses this key.",
            GlobalKeyRules.WhyNot("Ctrl+Shift+F9", "mute", [KeyChord.Parse("Ctrl+Shift+F9")]));
        // Shift makes it another key.
        Assert.Null(GlobalKeyRules.WhyNot("Ctrl+Shift+F9", "mute", [KeyChord.Parse("Ctrl+F9")]));
    }

    [Fact]
    public void GlobalKeysSettingsAreKeptSane()
    {
        var keys = new GlobalKeysSettings
        {
            ShowHide = "ctrl + shift + f9",
            WhenBehind = "sideways",
            WhenInFront = "NOTHING",
            HideTo = "",
            Actions =
            [
                new() { Key = "ctrl+shift+f10", Action = "Screenshot" },
                new() { Key = "Ctrl+Shift+F11", Action = "no-such-action" },
                new() { Key = "Alt+Q", Action = "home" },
                new() { Key = "Ctrl+Shift+F10", Action = "mute" },
                new() { Key = "Ctrl+Shift+F9", Action = "back" },
                null!,
                new() { Key = "Ctrl+Shift+F12", Action = "mute" },
            ],
        };
        keys.Normalize();

        Assert.Equal("Ctrl+Shift+F9", keys.ShowHide);
        Assert.Equal(("front", "nothing", "tray"), (keys.WhenBehind, keys.WhenInFront, keys.HideTo));
        Assert.Equal(["Ctrl+Shift+F10=screenshot", "Ctrl+Shift+F12=mute"], keys.Actions.Select(a => a.Key + "=" + a.Action));
        Assert.Equal(
            [("Ctrl+Shift+F9", GlobalKeyRules.ShowHide), ("Ctrl+Shift+F10", "screenshot"), ("Ctrl+Shift+F12", "mute")],
            keys.Keys().Select(k => (k.Chord.ToString(), k.Id)));

        // An unreadable, refused or missing show-or-hide key goes back to the default; empty stays off.
        foreach (var bad in new[] { "Alt+M", "Ctrl+Alt+Delete", "nonsense", null })
        {
            var refused = new GlobalKeysSettings { ShowHide = bad! };
            refused.Normalize();
            Assert.Equal(GlobalKeysSettings.DefaultShowHide, refused.ShowHide);
        }

        var off = new GlobalKeysSettings { ShowHide = "  ", Actions = null! };
        off.Normalize();
        Assert.Equal(string.Empty, off.ShowHide);
        Assert.Null(off.ShowHideChord);
        Assert.Empty(off.Actions);

        // At most twenty, the first ones kept.
        var many = new GlobalKeysSettings
        {
            Actions = [.. Enumerable.Range(1, 25).Select(n => new GlobalKeyAction { Key = "Ctrl+Shift+" + (char)('A' + n), Action = MirrorActions.Ids[n] })],
        };
        many.Normalize();
        Assert.Equal(GlobalKeysSettings.MostActions, many.Actions.Count);
        Assert.Equal(MirrorActions.Ids[1], many.Actions[0].Action);

        // A copy owns its list.
        var copy = keys.Copy();
        copy.Actions[0].Key = "Ctrl+Shift+F1";
        Assert.Equal("Ctrl+Shift+F10", keys.Actions[0].Key);
    }

    [Theory]
    [InlineData(false, false, false, false, false, "front", "hide", "tray", GlobalStep.Show)]
    [InlineData(false, false, false, false, true, "front", "hide", "tray", GlobalStep.ShowFullscreen)]
    [InlineData(true, true, false, false, false, "front", "hide", "tray", GlobalStep.Show)]
    [InlineData(true, true, false, false, true, "front", "hide", "taskbar", GlobalStep.ShowFullscreen)]
    [InlineData(true, false, false, false, false, "front", "hide", "tray", GlobalStep.BringToFront)]
    [InlineData(true, false, false, true, false, "front", "hide", "tray", GlobalStep.BringToFront)]
    [InlineData(true, false, false, false, false, "hide", "hide", "tray", GlobalStep.HideToTray)]
    [InlineData(true, false, false, false, false, "hide", "hide", "taskbar", GlobalStep.Minimise)]
    [InlineData(true, false, false, false, false, "hide", "nothing", "taskbar", GlobalStep.Minimise)]
    [InlineData(true, false, true, false, false, "front", "hide", "tray", GlobalStep.HideToTray)]
    [InlineData(true, false, true, false, false, "front", "hide", "taskbar", GlobalStep.Minimise)]
    [InlineData(true, false, true, false, false, "front", "nothing", "tray", GlobalStep.Nothing)]
    [InlineData(true, false, true, false, true, "front", "nothing", "tray", GlobalStep.Nothing)]
    [InlineData(true, false, true, true, false, "front", "hide", "tray", GlobalStep.HideToTray)]
    [InlineData(true, false, true, true, false, "front", "hide", "taskbar", GlobalStep.Minimise)]
    [InlineData(true, false, true, true, false, "front", "nothing", "tray", GlobalStep.Nothing)]
    [InlineData(true, false, false, true, false, "hide", "hide", "tray", GlobalStep.HideToTray)]
    [InlineData(false, true, false, false, false, "hide", "nothing", "taskbar", GlobalStep.Show)]
    [InlineData(true, false, true, false, true, "hide", "hide", "tray", GlobalStep.HideToTray)]
    [InlineData(false, false, false, true, false, "front", "hide", "tray", GlobalStep.Show)]
    [InlineData(true, false, true, true, true, "front", "hide", "taskbar", GlobalStep.Minimise)]
    [InlineData(true, false, false, true, true, "front", "nothing", "tray", GlobalStep.BringToFront)]
    public void WhatTheShowHideKeyDoes(bool visible, bool minimised, bool inFront, bool fullscreen, bool showFullscreen,
        string whenBehind, string whenInFront, string hideTo, GlobalStep expected)
    {
        var now = new WindowNow(visible, minimised, inFront, fullscreen);
        var settings = new GlobalKeysSettings { WhenBehind = whenBehind, WhenInFront = whenInFront, HideTo = hideTo, ShowFullscreen = showFullscreen };

        var step = GlobalKeyPolicy.ForShowHide(now, settings);

        Assert.Equal(expected, step);
        // Fullscreen is left before hiding, never before showing or doing nothing.
        Assert.Equal(fullscreen && step is GlobalStep.HideToTray or GlobalStep.Minimise, GlobalKeyPolicy.LeavesFullscreenFirst(now, step));
    }

    [Fact]
    public void TheChordBoxRecordsWhatIsPressed()
    {
        const int Ctrl = 0xA2, Shift = 0xA0, F9 = KeyChord.F1 + 8;
        var recorder = new ChordRecorder();

        // Modifiers first: nothing chosen yet, but they show.
        Assert.True(recorder.Press(Ctrl, KeyMods.Ctrl));
        Assert.True(recorder.Press(Shift, KeyMods.Ctrl | KeyMods.Shift));
        Assert.Equal((ChordState.Waiting, KeyMods.Ctrl | KeyMods.Shift), (recorder.State, recorder.Held));
        Assert.True(recorder.Press(F9, KeyMods.Ctrl | KeyMods.Shift));
        Assert.Equal(ChordState.Holding, recorder.State);
        // Another modifier coming up while the key is held changes nothing.
        recorder.Release(Shift, KeyMods.Ctrl);
        Assert.Equal(ChordState.Holding, recorder.State);
        recorder.Release(F9, KeyMods.Ctrl);
        Assert.Equal(ChordState.Done, recorder.State);
        Assert.Equal("Ctrl+Shift+F9", recorder.Chord.ToString());

        // A modifier pressed and let go on its own leaves the box waiting.
        recorder.Start();
        recorder.Press(Ctrl, KeyMods.Ctrl);
        recorder.Release(Ctrl, KeyMods.None);
        Assert.Equal((ChordState.Waiting, KeyMods.None, (KeyChord?)null), (recorder.State, recorder.Held, recorder.Chord));

        // Backspace and Delete on their own turn the shortcut off; with Ctrl they are a chord.
        Assert.True(recorder.Press(KeyChord.Backspace, KeyMods.None));
        Assert.Equal(ChordState.Cleared, recorder.State);
        recorder.Start();
        Assert.True(recorder.Press(KeyChord.Delete, KeyMods.None));
        Assert.Equal(ChordState.Cleared, recorder.State);
        recorder.Start();
        recorder.Press(KeyChord.Delete, KeyMods.Ctrl);
        Assert.Equal(ChordState.Holding, recorder.State);

        // Esc puts back what was there; Tab on its own is not the box's.
        recorder.Start();
        Assert.True(recorder.Press(KeyChord.Escape, KeyMods.None));
        Assert.Equal(ChordState.Cancelled, recorder.State);
        recorder.Start();
        Assert.False(recorder.Press(KeyChord.Tab, KeyMods.None));
        Assert.Equal(ChordState.Waiting, recorder.State);

        // A plain key is recorded as it is; whether it may be used is for the rules to say.
        recorder.Start();
        recorder.Press('Q', KeyMods.None);
        Assert.Equal("Q", recorder.Chord.ToString());

        // A key without a name is taken but not recorded.
        recorder.Start();
        Assert.True(recorder.Press(0xDE, KeyMods.Ctrl));
        Assert.Equal(ChordState.Waiting, recorder.State);
        // A key coming up that is not the one held does not finish the chord.
        recorder.Press(M, KeyMods.Ctrl);
        recorder.Release('N', KeyMods.Ctrl);
        Assert.Equal(ChordState.Holding, recorder.State);
    }

    [Fact]
    public void AHeldKeyActsOnce()
    {
        var taken = new KeyRepeat();
        var acted = new List<string>();
        void Down(int key)
        {
            if (taken.IsHeld(key))
            {
                acted.Add("repeat");
                return;
            }

            taken.Take(key);
            acted.Add("act");
        }

        Down(M);
        Down(M);
        Down(M);
        Assert.True(taken.Release(M));
        Assert.False(taken.Release(M));
        Down(M);
        taken.Clear();
        Assert.False(taken.IsHeld(M));

        Assert.Equal(["act", "repeat", "repeat", "act"], acted);
    }

    public static TheoryData<string, string> WindowKeyRows
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var (chord, id) in WindowKeys.All)
            {
                rows.Add(chord.ToString(), id);
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(WindowKeyRows))]
    public void TheWindowsOwnKeysLookAtEveryModifier(string written, string id)
    {
        var chord = KeyChord.Parse(written);

        Assert.Equal(id, WindowKeys.ActionFor(chord.Key, chord.Mods));
        Assert.Equal(Shortcuts.Gesture(id), written);
        Assert.Null(WindowKeys.ActionFor(chord.Key, chord.Mods | KeyMods.Shift));
        Assert.Null(WindowKeys.ActionFor(chord.Key, chord.Mods | KeyMods.Win));
    }

    [Fact]
    public void TabAndFunctionKeysNeedExactlyTheirModifiers()
    {
        Assert.Equal("tab-controls", WindowKeys.ActionFor('1', KeyMods.Ctrl | KeyMods.Alt));
        Assert.Null(WindowKeys.ActionFor('1', KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift));
        Assert.Equal("tour", WindowKeys.ActionFor(KeyChord.F1, KeyMods.None));
        Assert.Null(WindowKeys.ActionFor(KeyChord.F1, KeyMods.Ctrl | KeyMods.Alt));
        Assert.Null(WindowKeys.ActionFor(KeyChord.F1 + 10, KeyMods.Shift));
        // Keys from anywhere, browse keys and gestures are not the window's.
        Assert.DoesNotContain(WindowKeys.All, k => k.Id == GlobalKeyRules.ShowHide || Shortcuts.Find(k.Id)!.Browse);
    }

    [Fact]
    public void TheEffectiveShortcutsShowThePersonsKeys()
    {
        var config = new RexConfig();
        config.GlobalKeys.ShowHide = "Ctrl+Shift+F9";
        config.GlobalKeys.Actions = [new() { Key = "Ctrl+Shift+F10", Action = "screenshot" }];

        var effective = Shortcuts.Effective(config);

        Assert.Equal("Ctrl+Shift+F9", effective.Single(s => s.Id == GlobalKeyRules.ShowHide).Gesture);
        Assert.Equal(Shortcuts.Gesture("home"), effective.Single(s => s.Id == "home").Gesture);
        var screenshot = effective.Single(s => s.Id == "global-screenshot");
        Assert.Equal(("Ctrl+Shift+F10", "screenshot", true), (screenshot.Gesture, screenshot.Action, screenshot.Global));

        config.GlobalKeys.ShowHide = string.Empty;
        Assert.DoesNotContain(Shortcuts.Effective(config), s => s.Id == GlobalKeyRules.ShowHide);
        config.GlobalKeys.ShowHide = "Ctrl+Shift+F9";
        config.GlobalKeys.Enabled = false;
        Assert.DoesNotContain(Shortcuts.Effective(config), s => s.Global);
    }

    [Fact]
    public void ConfigSetRefusesABadChord()
    {
        using var package = new Support.TestPackage();
        var store = new ConfigStore(package.Paths.Config);

        var refused = Assert.Throws<FormatException>(() => store.Set("globalkeys.showhide", "Alt+Q"));
        Assert.Contains("needs Ctrl", refused.Message, StringComparison.Ordinal);
        Assert.Equal(GlobalKeysSettings.DefaultShowHide, store.Get("GlobalKeys.ShowHide").Value);

        Assert.Equal("Ctrl+Shift+F9", store.Set("GlobalKeys.ShowHide", "ctrl+shift+f9").Value);
        Assert.Equal(string.Empty, store.Set("GlobalKeys.ShowHide", "").Value);

        store.Set("GlobalKeys.ShowHide", "Ctrl+Shift+F9");
        store.Set("GlobalKeys.Actions", "[{\"Key\":\"Ctrl+Shift+F10\",\"Action\":\"screenshot\"}]");
        Assert.Equal("screenshot", ConfigFile.Load(package.Paths.Config).GlobalKeys.Actions.Single().Action);
        // The show-or-hide key cannot take a key an action already has.
        Assert.Throws<FormatException>(() => store.Set("GlobalKeys.ShowHide", "Ctrl+Shift+F10"));
    }

    [Theory]
    [InlineData("[{\"Key\":\"Ctrl+Shift+F10\",\"Action\":\"nonsense\"}]", "is not an action")]
    [InlineData("[{\"Key\":\"Ctrl+Shift+F10\"}]", "is not an action")]
    [InlineData("[{\"Key\":\"Alt+Q\",\"Action\":\"home\"}]", "Alt+Q for Home: A shortcut from anywhere needs Ctrl")]
    [InlineData("[{\"Key\":\"Ctrl+Shift+F9\",\"Action\":\"home\"}]", "Another shortcut from anywhere")]
    [InlineData("[{\"Key\":\"Ctrl+Shift+F10\",\"Action\":\"home\"},{\"Key\":\"Ctrl+Shift+F10\",\"Action\":\"back\"}]", "Another shortcut from anywhere")]
    [InlineData("[null]", "Expected a list")]
    [InlineData("{\"Key\":1}", "Expected a list")]
    [InlineData("not json", "Expected a list")]
    public void AListOfKeysFromAnywhereIsCheckedBeforeItIsSaved(string raw, string reason)
    {
        var current = new RexConfig();
        current.GlobalKeys.ShowHide = "Ctrl+Shift+F9";

        var refused = Assert.Throws<FormatException>(() => ConfigValidation.Check("GlobalKeys.Actions", raw, current));

        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AtMostTwentyKeysFromAnywhereAreAccepted()
    {
        var entries = Enumerable.Range(0, GlobalKeysSettings.MostActions + 1)
            .Select(n => $"{{\"Key\":\"Ctrl+Shift+{(char)('A' + n)}\",\"Action\":\"{MirrorActions.Ids[n]}\"}}");
        var raw = "[" + string.Join(',', entries) + "]";

        Assert.Contains("At most 20", Assert.Throws<FormatException>(() => ConfigValidation.Check("GlobalKeys.Actions", raw, new RexConfig())).Message, StringComparison.Ordinal);
        ConfigValidation.Check("GlobalKeys.Actions", "[" + string.Join(',', entries.Take(GlobalKeysSettings.MostActions)) + "]", new RexConfig());
        // Every other setting, and an empty key (which turns it off), passes untouched.
        ConfigValidation.Check("GlobalKeys.ShowHide", " ", new RexConfig());
        ConfigValidation.Check("Mirror.MaxFps", "60", new RexConfig());
        Assert.ThrowsAny<FormatException>(() => ConfigValidation.Check("Mirror.ExtraArgs", "\"unterminated", new RexConfig()));
        ConfigValidation.Check("Mirror.ExtraArgs", "--no-mipmaps", new RexConfig());
    }
}
