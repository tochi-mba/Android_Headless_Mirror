using Rex.Core;

namespace Rex.Tests;

/// <summary>
/// Your own keys: every key the app ships with still does what it did, a key of one's own moves an
/// action and frees the old key, a key can never do two things or fire while typing into the
/// phone, browse mode takes single keys, scrcpy's own key is passed on, and config.json keeps only
/// what differs from how the app ships.
/// </summary>
public sealed class KeysTests
{
    private static KeyMap Map(params (string Action, string Key)[] window) =>
        new(new KeysSettings { Window = [.. window.Select(w => new KeyBinding { Action = w.Action, Key = w.Key })] });

    private const int H = 0x48, J = 0x4A, S = 0x53, Down = 0x28, Space = 0x20;

    [Fact]
    public void EveryShippedKeyStillDoesWhatItDid()
    {
        var map = new KeyMap(new KeysSettings());
        foreach (var shortcut in Shortcuts.All.Where(s => s.IsKey && !s.Browse && !s.Global && Shortcuts.Favourite(s.Id) == 0 && Shortcuts.ProfileNumber(s.Id) == 0))
        {
            var chord = KeyChord.Parse(shortcut.Gesture);
            Assert.Equal(shortcut.Id, map.ActionFor(chord.Key, chord.Mods));
            Assert.Equal(chord, map.ChordFor(shortcut.Id));
        }

        Assert.Equal("home", map.ActionFor(H, KeyMods.Ctrl | KeyMods.Alt));
        Assert.Null(map.ActionFor(H, KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift));
    }

    [Fact]
    public void AnyActionCanHaveAKeyNotOnlyThoseThatShipWithOne()
    {
        var power = KeyMap.WindowIds.Single(w => w.Id == "power");
        Assert.Null(power.Shipped);
        Assert.Equal("power", Map(("power", "Ctrl+Alt+J")).ActionFor(J, KeyMods.Ctrl | KeyMods.Alt));
    }

    [Fact]
    public void AKeyOfOnesOwnMovesTheActionAndFreesTheOldKey()
    {
        var map = Map(("home", "Ctrl+Alt+J"));
        Assert.Equal("home", map.ActionFor(J, KeyMods.Ctrl | KeyMods.Alt));
        Assert.Null(map.ActionFor(H, KeyMods.Ctrl | KeyMods.Alt));
        Assert.Equal("Ctrl+Alt+J", map.ChordFor("home").ToString());

        // An empty key takes it away altogether.
        Assert.Null(Map(("home", "")).ChordFor("home"));
    }

    [Theory]
    [InlineData("home", "Ctrl+Alt+S", "This key already does: Screenshot.")]
    [InlineData("home", "J", "A key for the window needs Ctrl, Alt or Win, or is F1 to F24 on its own, so typing into the phone never sets it off.")]
    [InlineData("home", "Shift+J", "A key for the window needs Ctrl, Alt or Win, or is F1 to F24 on its own, so typing into the phone never sets it off.")]
    [InlineData("home", "Esc", "A key for the window needs Ctrl, Alt or Win, or is F1 to F24 on its own, so typing into the phone never sets it off.")]
    [InlineData("home", "Ctrl+Tab", "Tab cannot be a shortcut.")]
    [InlineData("home", "Ctrl+Shift+Esc", "Windows uses this key itself.")]
    [InlineData("home", "Ctrl+Alt+Shift+1", "This key already does: Open favourite app 1.")]
    [InlineData("home", "Ctrl+Alt+F2", "This key already does: Apply profile 2.")]
    [InlineData("teleport", "Ctrl+Alt+J", "'teleport' is not something a key can do. Run 'rex keys' to see what can.")]
    public void AKeyThatCannotBeUsedIsRefusedInWords(string action, string key, string why) =>
        Assert.Equal(why, new KeyMap(new KeysSettings()).WhyNotWindow(action, key));

    [Theory]
    [InlineData("home", "F9")]
    [InlineData("home", "Ctrl+J")]
    [InlineData("home", "Win+Alt+J")]
    [InlineData("home", "")]
    [InlineData("fullscreen-exit", "Esc")]
    [InlineData("home", "Ctrl+Alt+H")]
    public void AKeyThatCanBeUsedIsTaken(string action, string key) => Assert.Null(new KeyMap(new KeysSettings()).WhyNotWindow(action, key));

    [Fact]
    public void AKeyCannotBeReadIsRefusedWithWhy() =>
        Assert.NotNull(new KeyMap(new KeysSettings()).WhyNotWindow("home", "Ctrl+Nonsense"));

    [Fact]
    public void AKeyAShortcutFromAnywhereTakesIsRefused()
    {
        var global = new GlobalKeysSettings { Actions = [new GlobalKeyAction { Key = "Ctrl+Shift+F9", Action = "screenshot" }] };
        Assert.Equal("A shortcut from anywhere uses this key.", KeyMap.WhyNotFromAnywhere(global, "Ctrl+Shift+F9"));
        Assert.Equal("A shortcut from anywhere uses this key.", KeyMap.WhyNotFromAnywhere(global, global.ShowHide));
        Assert.Null(KeyMap.WhyNotFromAnywhere(global, "Ctrl+Alt+J"));
        Assert.Null(KeyMap.WhyNotFromAnywhere(global, "not a key"));
        Assert.Null(KeyMap.WhyNotFromAnywhere(global with { Enabled = false }, "Ctrl+Shift+F9"));
    }

    [Fact]
    public void BrowseModeTakesSingleKeysOfOnesOwn()
    {
        var map = new KeyMap(new KeysSettings { Browse = [new KeyBinding { Action = "swipe-up", Key = "J" }] });
        Assert.Equal("swipe-up", map.BrowseActionFor(J));
        Assert.Null(map.BrowseActionFor(Down));
        Assert.Equal("tap", map.BrowseActionFor(Space));
        Assert.Equal("back", map.BrowseActionFor(0x08));

        // Space taps only while it is not given something else.
        Assert.Equal("like", new KeyMap(new KeysSettings { Browse = [new KeyBinding { Action = "like", Key = "Space" }] }).BrowseActionFor(Space));
    }

    [Theory]
    [InlineData("swipe-up", "Ctrl+J", "Browse mode keys are single keys, with nothing held.")]
    [InlineData("swipe-up", "Esc", "Esc leaves browse mode.")]
    [InlineData("swipe-up", "Tab", "Tab cannot play anything.")]
    [InlineData("swipe-up", "L", "In browse mode this key already plays: Like.")]
    [InlineData("nothing", "J", "'nothing' is not something browse mode can play.")]
    public void ABrowseKeyThatCannotBeUsedIsRefused(string action, string key, string why) =>
        Assert.Equal(why, new KeyMap(new KeysSettings()).WhyNotBrowse(action, key));

    [Fact]
    public void ABrowseKeyCanBeTakenAwayOrGivenToAnyAction()
    {
        var map = new KeyMap(new KeysSettings());
        Assert.Null(map.WhyNotBrowse("like", ""));
        Assert.Null(map.WhyNotBrowse("home", "H"));
        Assert.NotNull(map.WhyNotBrowse("home", "Ctrl+"));
    }

    [Fact]
    public void ConfigJsonKeepsOnlyKeysThatCanBeUsedEachOnceInTheAppsOwnWords()
    {
        var keys = new KeysSettings
        {
            Window =
            [
                new KeyBinding { Action = " home ", Key = "ctrl+alt+j" },
                new KeyBinding { Action = "home", Key = "Ctrl+Alt+K" },
                new KeyBinding { Action = "back", Key = "Ctrl+Alt+J" },
                new KeyBinding { Action = "teleport", Key = "Ctrl+Alt+Q" },
                new KeyBinding { Action = "recents", Key = "" },
                null!,
            ],
            Browse = [new KeyBinding { Action = "like", Key = "k" }, new KeyBinding { Action = "tap", Key = "Ctrl+K" }],
        };
        keys.Normalize();
        Assert.Equal([("home", "Ctrl+Alt+J"), ("recents", "")], keys.Window.Select(b => (b.Action, b.Key)));
        Assert.Equal([("like", "K")], keys.Browse.Select(b => (b.Action, b.Key)));

        // Every action can be given a key of its own (here, none) at once.
        var every = new KeysSettings { Window = [.. KeyMap.WindowIds.Select(w => new KeyBinding { Action = w.Id, Key = "" })] };
        every.Normalize();
        Assert.Equal(KeyMap.WindowIds.Count, every.Window.Count);
        Assert.True(KeyMap.WindowIds.Count <= KeysSettings.MostBindings);
    }

    [Fact]
    public void APutBackKeyLeavesTheList()
    {
        var bindings = new List<KeyBinding>();
        KeyMap.Set(bindings, "home", "Ctrl+Alt+J", KeyChord.Parse("Ctrl+Alt+H"));
        Assert.Single(bindings);
        KeyMap.Set(bindings, "home", "ctrl+alt+h", KeyChord.Parse("Ctrl+Alt+H"));
        Assert.Empty(bindings);
        KeyMap.Set(bindings, "power", "", null);
        Assert.Empty(bindings);
        KeyMap.Set(bindings, "home", "", KeyChord.Parse("Ctrl+Alt+H"));
        Assert.Equal([("home", "")], bindings.Select(b => (b.Action, b.Key)));
    }

    [Fact]
    public void KeysApplyAtOnceAndScrcpysOwnKeyStaysRightCtrl()
    {
        Assert.Contains("--shortcut-mod=rctrl", ScrcpyArguments.Build(new RexConfig(), "S", false, "T", null, null));
        Assert.False(SettingsCatalogue.AppliesAtNextStart("Keys.Window"));
        Assert.False(SettingsCatalogue.AppliesAtNextStart("Keys.Browse"));
    }

    [Fact]
    public void TheShortcutsAsThePersonHasThem()
    {
        var config = new RexConfig();
        config.Keys.Window = [new KeyBinding { Action = "home", Key = "Ctrl+Alt+J" }, new KeyBinding { Action = "power", Key = "Ctrl+Alt+Q" }, new KeyBinding { Action = "recents", Key = "" }];
        config.Keys.Browse = [new KeyBinding { Action = "like", Key = "K" }, new KeyBinding { Action = "mute", Key = "" }];
        var effective = Shortcuts.Effective(config);
        Assert.Equal("Ctrl+Alt+J", effective.Single(s => s.Id == "home").Gesture);
        Assert.Equal("Ctrl+Alt+Q", effective.Single(s => s.Id == "power").Gesture);
        Assert.DoesNotContain(effective, s => s.Id == "recents");
        Assert.Equal("K", effective.Single(s => s.Id == "browse-like").Gesture);
        Assert.DoesNotContain(effective, s => s.Id == "browse-mute");
    }

    [Fact]
    public void ConfigSetChecksAListOfKeysAsRexKeysWould()
    {
        ConfigValidation.Check("Keys.Window", """[{"Action":"home","Key":"Ctrl+Alt+J"}]""", new RexConfig());
        ConfigValidation.Check("Keys.Browse", """[{"Action":"like","Key":"K"}]""", new RexConfig());
        Assert.Contains("for back: This key already does: Home.",
            Assert.Throws<FormatException>(() => ConfigValidation.Check("Keys.Window", """[{"Action":"home","Key":"Ctrl+Alt+J"},{"Action":"back","Key":"Ctrl+Alt+J"}]""", new RexConfig())).Message,
            StringComparison.Ordinal);
        Assert.Contains("single keys", Assert.Throws<FormatException>(() => ConfigValidation.Check("Keys.Browse", """[{"Action":"like","Key":"Ctrl+K"}]""", new RexConfig())).Message,
            StringComparison.Ordinal);
        Assert.StartsWith("Expected a list", Assert.Throws<FormatException>(() => ConfigValidation.Check("Keys.Window", "{", new RexConfig())).Message, StringComparison.Ordinal);
        Assert.StartsWith("Expected a list", Assert.Throws<FormatException>(() => ConfigValidation.Check("Keys.Window", "null", new RexConfig())).Message, StringComparison.Ordinal);
        var tooMany = "[" + string.Join(',', Enumerable.Repeat("""{"Action":"home","Key":""}""", KeysSettings.MostBindings + 1)) + "]";
        Assert.StartsWith("At most", Assert.Throws<FormatException>(() => ConfigValidation.Check("Keys.Window", tooMany, new RexConfig())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACopyOfTheKeysIsItsOwn()
    {
        var keys = new KeysSettings { Window = [new KeyBinding { Action = "home", Key = "Ctrl+Alt+J" }], Browse = [new KeyBinding { Action = "like", Key = "K" }] };
        var copy = keys.Copy();
        copy.Window[0].Key = "Ctrl+Alt+Q";
        copy.Browse.Clear();
        Assert.Equal("Ctrl+Alt+J", keys.Window[0].Key);
        Assert.Single(keys.Browse);
    }

    [Fact]
    public void TheWindowAnswersToTheCurrentMap()
    {
        // Set as the app sets it, to the keys it ships with, so a test running alongside sees no difference.
        KeyMap.Current = new KeyMap(new KeysSettings());
        // Nothing in the tests sets the app's own map, so it is the one the app ships with.
        Assert.Equal("home", WindowKeys.ActionFor(H, KeyMods.Ctrl | KeyMods.Alt));
        Assert.Equal("favourite-1", WindowKeys.ActionFor(0x31, KeyMods.Ctrl | KeyMods.Alt | KeyMods.Shift));
        Assert.Contains(WindowKeys.All, k => k.Id == "screenshot" && k.Chord == KeyChord.Parse("Ctrl+Alt+S"));
        Assert.Equal("Ctrl+Alt+H", Shortcuts.Gesture("home"));
        Assert.Equal("Two fingers", Shortcuts.Gesture("phone-gesture"));
        Assert.Equal(string.Empty, Shortcuts.Gesture("power"));
        Assert.Equal("Down", Shortcuts.BrowseKey("swipe-up"));
        Assert.Equal(string.Empty, Shortcuts.BrowseKey("home"));
    }
}
