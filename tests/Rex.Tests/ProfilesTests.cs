using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Profiles: settings by path, saving and applying them, the store, the presets, the rules that switch them, and the commands.</summary>
public sealed class ProfilesTests
{
    private static Dictionary<string, JsonNode> Settings(params (string Path, JsonNode Value)[] pairs) =>
        pairs.ToDictionary(p => p.Path, p => p.Value, StringComparer.Ordinal);

    private static Profile Make(string name, params (string Path, JsonNode Value)[] pairs) =>
        new(name, Settings(pairs), DateTimeOffset.UnixEpoch);

    // ----- Settings by path -----

    [Fact]
    public void EverySettingHasAPathAndAListIsOneValue()
    {
        var values = ConfigPaths.Values(new RexConfig());
        Assert.DoesNotContain("Version", values.Keys);
        Assert.Equal(1920, values["Mirror.MaxSize"].GetValue<int>());
        Assert.IsType<JsonArray>(values["App.TopBarButtons"]);
        Assert.DoesNotContain(values.Keys, path => path.StartsWith("App.TopBarButtons.", StringComparison.Ordinal));
        Assert.Equal(1920, ConfigPaths.Get(new RexConfig(), "Mirror.MaxSize")!.GetValue<int>());
        Assert.Null(ConfigPaths.Get(new RexConfig(), "Mirror.Nothing"));
    }

    [Fact]
    public void ValuesAreSetByPathAndWhatCannotBeSetIsNamed()
    {
        var (config, unknown) = ConfigPaths.Apply(new RexConfig(), Settings(
            ("mirror.maxfps", 90),
            ("Mirror.Audio", false),
            ("App.TopBarButtons", new JsonArray("home", "back")),
            ("Mirror.Nothing", 1),
            ("Mirror", 1),
            ("Mirror.MaxSize.Deeper", 1),
            ("Mirror.MaxSize", "big"),
            ("Mirror.VideoBitRate", true)));
        Assert.Equal(90, config.Mirror.MaxFps);
        Assert.False(config.Mirror.Audio);
        Assert.Equal(["home", "back"], config.App.TopBarButtons);
        Assert.Equal(1920, config.Mirror.MaxSize);
        Assert.Equal(["Mirror.Nothing", "Mirror", "Mirror.MaxSize.Deeper", "Mirror.MaxSize", "Mirror.VideoBitRate"], unknown);
    }

    [Fact]
    public void AValueOfTheWrongShapeIsLeftOutAndTheRestStillApply()
    {
        // 1.5 is a number like the frame rate, but not a whole one: only applying one at a time can tell.
        var (config, unknown) = ConfigPaths.Apply(new RexConfig(), Settings(
            ("Mirror.MaxFps", 1.5), ("Mirror.MaxSize", 1280), ("Bogus", 1), ("Mirror.Bogus", 1)));
        Assert.Equal(1280, config.Mirror.MaxSize);
        Assert.Equal(new RexConfig().Mirror.MaxFps, config.Mirror.MaxFps);
        Assert.Equal(["Mirror.MaxFps", "Bogus", "Mirror.Bogus"], unknown);
    }

    [Fact]
    public void ValuesAreNormalisedAsTheAppNormalisesThem()
    {
        var (config, unknown) = ConfigPaths.Apply(new RexConfig(), Settings(("Mirror.MaxSize", 999_999)));
        Assert.Empty(unknown);
        Assert.Equal(MirrorSettings.MaxSizeUpperBound, config.Mirror.MaxSize);
    }

    [Fact]
    public void TheDifferencesBetweenTwoConfigurationsAreTheirPaths()
    {
        var changed = new RexConfig();
        changed.Mirror.MaxFps = 30;
        changed.Sound.Muted = true;
        Assert.Equal(["Mirror.MaxFps", "Sound.Muted"], ConfigPaths.Differences(changed, new RexConfig()).Order(StringComparer.Ordinal));
        Assert.Empty(ConfigPaths.Differences(new RexConfig(), new RexConfig()));
    }

    // ----- A profile -----

    [Fact]
    public void AProfileNeverCarriesProfilesOwnSettings()
    {
        Assert.False(Profile.Carries("Profiles.Keys"));
        Assert.False(Profile.Carries("Version"));
        Assert.True(Profile.Carries("Mirror.MaxFps"));
        Assert.Equal("Mirror", Profile.GroupOf("Mirror.MaxFps"));
    }

    [Fact]
    public void SavingTakesWhatDiffersOrEverythingAsked()
    {
        var config = new RexConfig();
        config.Mirror.MaxFps = 30;
        config.Sound.Muted = true;
        config.Profiles.Keys = false;
        var now = DateTimeOffset.UtcNow;

        var changed = Profile.From("Mine", config, null, onlyChanged: true, now);
        Assert.Equal(["Mirror.MaxFps", "Sound.Muted"], changed.Settings.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Mirror", "Sound"], changed.Groups);
        Assert.Equal(now, changed.Saved);

        var picture = Profile.From("Picture", config, ["mirror"], onlyChanged: true, now);
        Assert.Equal(["Mirror.MaxFps"], picture.Settings.Keys);

        var everything = Profile.From("All", config, [], onlyChanged: false, now);
        Assert.Contains("Mirror.MaxSize", everything.Settings.Keys);
        Assert.DoesNotContain(everything.Settings.Keys, path => path.StartsWith("Profiles.", StringComparison.Ordinal));
    }

    [Fact]
    public void AProfileSetsOnlyItsOwnSettingsAndCountsWhatChangedSince()
    {
        var config = new RexConfig();
        config.Sound.Volume = 0.4;
        var profile = Make("Fast", ("Mirror.MaxFps", 120), ("Profiles.Keys", false), ("Gone.Setting", 1));
        var (applied, unknown) = profile.Apply(config);
        Assert.Equal(120, applied.Mirror.MaxFps);
        Assert.Equal(0.4, applied.Sound.Volume);
        Assert.True(applied.Profiles.Keys);
        Assert.Equal(["Gone.Setting"], unknown);

        Assert.Equal(0, Make("Fast", ("Mirror.MaxFps", 120)).ChangedSince(applied));
        applied.Mirror.MaxFps = 60;
        Assert.Equal(1, profile.ChangedSince(applied));
    }

    [Fact]
    public void ProfilesSettingsAreKeptSane()
    {
        var settings = new ProfilesSettings { AfterApplying = "explode", WhenFullscreen = " Gaming ", WhenOnBattery = "CON" };
        settings.Normalize();
        Assert.Equal("offer", settings.AfterApplying);
        Assert.Equal("Gaming", settings.WhenFullscreen);
        Assert.Equal(string.Empty, settings.WhenOnBattery);
        var copy = settings.Copy();
        copy.Keys = false;
        Assert.True(settings.Keys);
    }

    [Theory]
    [InlineData("Gaming", null)]
    [InlineData("Films 2.0_x-y (2)", null)]
    [InlineData("", "A profile needs a name.")]
    [InlineData("   ", "A profile needs a name.")]
    [InlineData(null, "A profile needs a name.")]
    [InlineData("12345678901234567890123456789012345678901", "A profile's name is at most 40 characters.")]
    [InlineData("a/b", "Use letters, digits, spaces, dots, hyphens, underscores and brackets, and no dot at either end.")]
    [InlineData(".hidden", "Use letters, digits, spaces, dots, hyphens, underscores and brackets, and no dot at either end.")]
    [InlineData("end.", "Use letters, digits, spaces, dots, hyphens, underscores and brackets, and no dot at either end.")]
    [InlineData("com1", "Windows keeps that name for itself; choose another.")]
    [InlineData("Order", "Windows keeps that name for itself; choose another.")]
    public void AProfilesNameIsAlsoItsFilesName(string? name, string? why)
    {
        Assert.Equal(why, ProfileNames.WhyNot(name));
        Assert.Equal(why is null, ProfileNames.IsValid(name));
    }

    [Fact]
    public void EveryPresetIsValidAndChangesWhatItSays()
    {
        Assert.Equal(6, ProfilePresets.All.Count);
        foreach (var preset in ProfilePresets.All)
        {
            Assert.True(ProfileNames.IsValid(preset.Name));
            var (applied, unknown) = preset.Apply(new RexConfig());
            Assert.Empty(unknown);
            // Each value survives normalising unchanged, and each one changes something.
            Assert.Equal(0, preset.ChangedSince(applied));
            Assert.NotEmpty(ConfigPaths.Differences(applied, new RexConfig()));
        }

        Assert.Same(ProfilePresets.All[4], ProfilePresets.Find("gaming"));
        Assert.Null(ProfilePresets.Find("Nothing"));
    }

    [Fact]
    public void ARuleCanSwitchToASavedProfileOrAPreset()
    {
        var choices = ProfilePresets.RuleChoices(["Mine", "gaming"]);
        Assert.Equal((string.Empty, "None"), choices[0]);
        Assert.Equal(("Mine", "Mine"), choices[1]);
        Assert.Equal(("gaming", "gaming"), choices[2]);
        // A saved profile goes by Gaming already, so the preset of that name is not offered twice.
        Assert.DoesNotContain(choices, c => c.Label == "Gaming (preset)");
        Assert.Contains(("Quiet", "Quiet (preset)"), choices);
        Assert.Equal(1 + ProfilePresets.All.Count, ProfilePresets.RuleChoices([]).Count);
    }

    // ----- Which profile is in effect -----

    [Theory]
    [InlineData(true, true, "Phone", "Full", "Battery", true, "Full")]
    [InlineData(false, true, "Phone", "Full", "Battery", true, "Battery")]
    [InlineData(false, false, "Phone", "Full", "Battery", true, "Phone")]
    [InlineData(false, false, "Phone", "Full", "Battery", false, null)]
    [InlineData(false, false, " ", "Full", "Battery", true, null)]
    [InlineData(false, false, null, "Full", "Battery", true, null)]
    [InlineData(true, true, "Phone", "", "", true, "Phone")]
    [InlineData(true, false, null, "", "Battery", true, null)]
    public void WhichProfileIsInEffect(bool fullscreen, bool battery, string? phone, string whenFullscreen, string whenBattery, bool perPhone, string? expected)
    {
        var settings = new ProfilesSettings { WhenFullscreen = whenFullscreen, WhenOnBattery = whenBattery, PerPhone = perPhone };
        Assert.Equal(expected, ProfilePolicy.Automatic(new ProfileInputs("Mine", phone, battery, fullscreen), settings));
    }

    [Fact]
    public void AnAutomaticProfilePutsBackOnlyWhatItChanged()
    {
        var config = new RexConfig();
        config.Mirror.MaxFps = 60;
        var profile = Make("Game", ("Mirror.MaxFps", 120), ("Mirror.MaxSize", 1920), ("Sound.Muted", true));
        var (applied, record, unknown) = ProfilePolicy.Start(config, profile);
        Assert.Empty(unknown);
        Assert.Equal("Game", record.Profile);
        // MaxSize already was 1920: not a change, so nothing to put back.
        Assert.Equal(["Mirror.MaxFps", "Sound.Muted"], record.Set.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(60, record.Before["Mirror.MaxFps"].GetValue<int>());

        // Untouched since, it all goes back.
        var back = ProfilePolicy.End(applied, record);
        Assert.Empty(ConfigPaths.Differences(back, config));

        // Changed meanwhile by the person, that one stays.
        applied.Sound.Muted = false;
        applied.Mirror.MaxFps = 90;
        var kept = ProfilePolicy.End(applied, record);
        Assert.Equal(90, kept.Mirror.MaxFps);
        Assert.False(kept.Sound.Muted);
    }

    // ----- The store -----

    [Fact]
    public void TheStoreSavesListsAndKeepsTheOrder()
    {
        using var package = new TestPackage();
        var store = ProfileStore.For(package.Paths);
        Assert.Empty(store.List());
        Assert.Null(store.Load("Gaming"));

        store.Save(Make("Gaming", ("Mirror.MaxFps", 120)));
        store.Save(Make("Films", ("Mirror.MaxSize", 0)));
        store.Save(Make("Battery", ("Mirror.MaxFps", 30)));
        Assert.Equal(["Gaming", "Films", "Battery"], store.List().Select(e => e.Name));

        store.Move("Battery", -5);
        store.Move("Gaming", 1);
        store.Move("Nothing", 1);
        Assert.Equal(["Battery", "Films", "Gaming"], store.List().Select(e => e.Name));

        // Saving under the same name in another case replaces it in its place.
        store.Save(Make("films", ("Mirror.MaxSize", 720)));
        Assert.Equal(["Battery", "films", "Gaming"], store.List().Select(e => e.Name));
        Assert.Equal(720, store.Load(" FILMS ")!.Settings["Mirror.MaxSize"].GetValue<int>());

        Assert.Throws<ArgumentException>(() => store.Save(Make("bad/name")));
    }

    [Fact]
    public void TheStoreRenamesDuplicatesAndDeletes()
    {
        using var package = new TestPackage();
        var store = ProfileStore.For(package.Paths);
        store.Save(Make("Gaming", ("Mirror.MaxFps", 120)));
        store.Save(Make("Films"));
        var now = DateTimeOffset.UtcNow;

        store.Rename("gaming", "Games");
        Assert.Equal(["Games", "Films"], store.List().Select(e => e.Name));
        store.Rename("Games", "GAMES");
        Assert.Equal("GAMES", store.List()[0].Name);
        Assert.Throws<KeyNotFoundException>(() => store.Rename("Nothing", "Other"));
        Assert.Throws<ArgumentException>(() => store.Rename("GAMES", "CON"));
        Assert.Throws<ArgumentException>(() => store.Rename("GAMES", "films"));

        Assert.Equal("GAMES (2)", store.Duplicate("games", now));
        Assert.Equal("GAMES (3)", store.Duplicate("GAMES", now));
        Assert.Equal(now, store.Load("GAMES (3)")!.Saved);
        Assert.Throws<KeyNotFoundException>(() => store.Duplicate("Nothing", now));
        var longName = new string('a', ProfileNames.Longest);
        store.Save(Make(longName));
        Assert.Equal(new string('a', ProfileNames.Longest - 5) + " (2)", store.Duplicate(longName, now));

        store.Delete("films");
        Assert.DoesNotContain(store.List(), e => e.Name == "Films");
        Assert.Throws<KeyNotFoundException>(() => store.Delete("Films"));
    }

    [Fact]
    public void ProfilesTravelAsFiles()
    {
        using var package = new TestPackage();
        var store = ProfileStore.For(package.Paths);
        var now = DateTimeOffset.UtcNow;
        store.Save(Make("Gaming", ("Mirror.MaxFps", 120)));
        var file = Path.Combine(package.Root, "shared.json");
        store.Export("gaming", file);
        Assert.Throws<KeyNotFoundException>(() => store.Export("Nothing", file));

        // A name already taken gets a number.
        Assert.Equal("Gaming (2)", store.Import(file, now));
        Assert.Equal(120, store.Load("Gaming (2)")!.Settings["Mirror.MaxFps"].GetValue<int>());

        // A file with no usable name is called Imported; one that is not a profile is refused.
        File.WriteAllText(file, """{"name": "bad/name", "settings": {"Mirror.MaxFps": 30}}""");
        Assert.Equal("Imported", store.Import(file, now));
        File.WriteAllText(file, """{"name": "x"}""");
        Assert.Throws<FormatException>(() => store.Import(file, now));
    }

    [Fact]
    public void AProfileFileThatCannotBeReadIsListedWithTheReason()
    {
        using var package = new TestPackage();
        var store = ProfileStore.For(package.Paths);
        store.Save(Make("Good"));
        File.WriteAllText(Path.Combine(store.Folder, "Broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(store.Folder, ".automatic.json"), "{}");
        var held = Path.Combine(store.Folder, "Held.json");
        File.WriteAllText(held, ProfileStore.ToJson(Make("Held")));
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // The saved one in its place, then the others by name.
            Assert.Equal(["Good", "Broken", "Held"], store.List().Select(e => e.Name));
            var entries = store.List().ToDictionary(e => e.Name);
            Assert.Null(entries["Broken"].Profile);
            Assert.Equal("It is not a profile.", entries["Broken"].Problem);
            Assert.StartsWith("It could not be read: ", entries["Held"].Problem, StringComparison.Ordinal);
            Assert.Null(store.Load("Held"));
        }
    }

    [Fact]
    public void TheOrderFileIsForgivingAboutWhatItHolds()
    {
        using var package = new TestPackage();
        var store = ProfileStore.For(package.Paths);
        store.Save(Make("B"));
        store.Save(Make("A"));
        var order = Path.Combine(store.Folder, "order.json");

        File.WriteAllText(order, """["A", 3, null]""");
        Assert.Equal(["A", "B"], store.List().Select(e => e.Name));
        File.WriteAllText(order, "{}");
        Assert.Equal(["A", "B"], store.List().Select(e => e.Name));
        File.WriteAllText(order, "[ broken");
        Assert.Equal(["A", "B"], store.List().Select(e => e.Name));
    }

    [Fact]
    public void AProfilesTextIsReadCarefully()
    {
        Assert.Null(ProfileStore.Parse("[]", "x"));
        Assert.Null(ProfileStore.Parse("""{"settings": []}""", "x"));
        Assert.Null(ProfileStore.Parse("nope", "x"));

        var parsed = ProfileStore.Parse("""{"name": 5, "saved": "yesterday", "settings": {"Mirror.MaxFps": 30, "Mirror.MaxSize": null}}""", "Fallback")!;
        Assert.Equal("Fallback", parsed.Name);
        Assert.Equal(DateTimeOffset.UnixEpoch, parsed.Saved);
        Assert.Equal(["Mirror.MaxFps"], parsed.Settings.Keys);

        var saved = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        var round = ProfileStore.Parse(ProfileStore.ToJson(new Profile("Mine", Settings(("Mirror.MaxFps", 30)), saved)), "x")!;
        Assert.Equal("Mine", round.Name);
        Assert.Equal(saved, round.Saved);
    }

    // ----- The book: what the app and the command line both do -----

    [Fact]
    public void ApplyingWritesTheConfigOnceAndRemembersTheOneAppliedByHand()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        Assert.Null(book.Apply("Nothing"));

        var applied = book.Apply("gaming")!;
        Assert.Equal("Gaming applied", applied.Words);
        Assert.Equal(120, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);
        Assert.Equal("Gaming", book.State.Ui.Profile);
        Assert.Equal(120, book.Config.Mirror.MaxFps);

        book.Store.Save(Make("Old", ("Gone.Setting", 1), ("Mirror.MaxFps", 45)));
        var old = book.Apply("Old", byHand: false)!;
        Assert.Equal("Old applied · 1 setting in it is not used any more", old.Words);
        Assert.Equal("Gaming", book.State.Ui.Profile);
        Assert.Equal("X applied · 2 settings in it are not used any more", new ProfileApplied(Make("X"), ["a", "b"]).Words);
    }

    [Fact]
    public void SavingAndUpdatingTakeTheCurrentSettings()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        package.EditConfig(c => c.Mirror.MaxFps = 30);
        var saved = book.Save(" Mine ", null, onlyChanged: true, DateTimeOffset.UtcNow);
        Assert.Equal("Mine", saved.Name);
        Assert.Equal(["Mirror.MaxFps"], saved.Settings.Keys);

        book.Store.Save(Make("Mine", ("Mirror.MaxFps", 30), ("Gone.Setting", 1)));
        package.EditConfig(c => c.Mirror.MaxFps = 45);
        var later = DateTimeOffset.UtcNow;
        var updated = book.Update("mine", later);
        Assert.Equal(45, updated.Settings["Mirror.MaxFps"].GetValue<int>());
        Assert.Equal(["Mirror.MaxFps"], updated.Settings.Keys);
        Assert.Equal(later, book.Store.Load("Mine")!.Saved);
        Assert.Throws<KeyNotFoundException>(() => book.Update("Nothing", later));
    }

    [Fact]
    public void RulesPhonesAndTheHandAppliedOneFollowARenameAndADelete()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        book.Store.Save(Make("Gaming"));
        book.Store.Save(Make("Films"));
        package.EditConfig(c =>
        {
            c.Profiles.WhenFullscreen = "gaming";
            c.Profiles.WhenOnBattery = "Gaming";
        });
        book.State.RememberDevice("PHONE1", "Galaxy", "SM-G998B");
        book.UseForPhone("PHONE1", " gaming ");
        book.UseForPhone("NOT-KNOWN", "Gaming");
        Assert.Null(book.State.GetDevice("NOT-KNOWN"));
        book.Apply("Gaming");

        book.Rename("Gaming", " Games ");
        var config = ConfigFile.Load(package.Paths.Config);
        Assert.Equal("Games", config.Profiles.WhenFullscreen);
        Assert.Equal("Games", config.Profiles.WhenOnBattery);
        Assert.Equal("Games", book.State.Ui.Profile);
        Assert.Equal("Games", book.State.GetDevice("PHONE1")!.Profile);

        // Renaming one no rule names writes no config.
        var written = File.GetLastWriteTimeUtc(package.Paths.Config);
        book.Rename("Films", "Movies");
        Assert.Equal(written, File.GetLastWriteTimeUtc(package.Paths.Config));

        book.Delete("games");
        config = ConfigFile.Load(package.Paths.Config);
        Assert.Equal(string.Empty, config.Profiles.WhenFullscreen);
        Assert.Equal(string.Empty, config.Profiles.WhenOnBattery);
        Assert.Equal(string.Empty, book.State.Ui.Profile);
        Assert.Equal(string.Empty, book.State.GetDevice("PHONE1")!.Profile);
        Assert.Throws<KeyNotFoundException>(() => book.Delete("Games"));
    }

    [Fact]
    public void NumberKeysCountTheProfilesThatCanBeRead()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        book.Store.Save(Make("First"));
        File.WriteAllText(Path.Combine(book.Store.Folder, "Broken.json"), "nope");
        book.Store.Save(Make("Second"));
        Assert.Equal("First", book.NameAt(1));
        Assert.Equal("Second", book.NameAt(2));
        Assert.Null(book.NameAt(3));
        Assert.Null(book.NameAt(0));
        Assert.Same(ProfilePresets.Find("Quiet"), book.Find("quiet"));
        Assert.Equal("First", book.Find("first")!.Name);
    }

    [Fact]
    public void AnAutomaticProfileIsRememberedOnDiskUntilItEnds()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        Assert.Null(book.ReadPutBack());
        book.WritePutBack(null);

        var record = book.Start(Make("Game", ("Mirror.MaxFps", 120)));
        Assert.Equal(120, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);
        var read = book.ReadPutBack()!;
        Assert.Equal("Game", read.Profile);
        Assert.Equal(record.Before.Keys, read.Before.Keys);
        Assert.Equal(120, read.Set["Mirror.MaxFps"].GetValue<int>());

        book.End(read, putBack: true);
        Assert.Null(book.ReadPutBack());
        Assert.Equal(new RexConfig().Mirror.MaxFps, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);

        book.End(book.Start(Make("Game", ("Mirror.MaxFps", 120))), putBack: false);
        Assert.Equal(120, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);
    }

    [Fact]
    public void ARecordThatIsNotOneMeansNoAutomaticProfile()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        var path = Path.Combine(book.Store.Folder, ".automatic.json");
        Directory.CreateDirectory(book.Store.Folder);

        File.WriteAllText(path, "[]");
        Assert.Null(book.ReadPutBack());
        File.WriteAllText(path, """{"profile": 5}""");
        Assert.Null(book.ReadPutBack());
        File.WriteAllText(path, "{ broken");
        Assert.Null(book.ReadPutBack());
        File.WriteAllText(path, """{"profile": "Game", "before": [], "set": {"Mirror.MaxFps": 120, "Mirror.MaxSize": null}}""");
        var record = book.ReadPutBack()!;
        Assert.Empty(record.Before);
        Assert.Equal(["Mirror.MaxFps"], record.Set.Keys);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(book.ReadPutBack());
        }
    }

    // ----- The commands -----

    [Theory]
    [InlineData("list", "", "", "")]
    [InlineData("show", "Gaming", "", "")]
    [InlineData("apply", "Gaming", "", "")]
    [InlineData("save", "Gaming", "", "")]
    [InlineData("delete", "Gaming", "", "")]
    [InlineData("rename", "Gaming", "Games", "")]
    [InlineData("export", "Gaming", "", "C:\\x.json")]
    [InlineData("import", "", "", "C:\\x.json")]
    public void ACompleteRequestIsAccepted(string verb, string name, string to, string file)
    {
        var request = new ProfileRequest(verb, name, to, file);
        Assert.Same(request, request.Check());
    }

    [Theory]
    [InlineData("explode", "Gaming", "", "")]
    [InlineData("apply", "", "", "")]
    [InlineData("rename", "Gaming", "", "")]
    [InlineData("export", "Gaming", "", "")]
    [InlineData("import", "", "", "")]
    public void AnIncompleteRequestIsRefusedWithTheUsage(string verb, string name, string to, string file)
    {
        var refused = Assert.Throws<ArgumentException>(() => new ProfileRequest(verb, name, to, file).Check());
        Assert.Equal("Usage: " + ProfileRequest.Usage, refused.Message);
        Assert.Contains(verb is "explode" ? "list" : verb, ProfileRequest.Verbs);
    }

    [Fact]
    public void ARequestCrossesThePipeIntact()
    {
        var request = new ProfileRequest("save", "Gaming", "Games", "C:\\x.json", ["Mirror", "Sound"], All: true);
        var fields = request.ToFields();
        Assert.Equal("Mirror,Sound", fields["groups"]);
        var back = ProfileRequest.FromFields(fields);
        Assert.Equal(request.Verb, back.Verb);
        Assert.Equal(request.Name, back.Name);
        Assert.Equal(request.To, back.To);
        Assert.Equal(request.File, back.File);
        Assert.Equal(request.Groups, back.Groups);
        Assert.True(back.All);

        var bare = new ProfileRequest("list").ToFields();
        Assert.Equal(["verb"], bare.Keys);
        var empty = ProfileRequest.FromFields(new Dictionary<string, string>());
        Assert.Equal("list", empty.Verb);
        Assert.Empty(empty.Groups!);
        Assert.False(empty.All);
    }

    [Fact]
    public void TheCommandsDoWhatTheySay()
    {
        using var package = new TestPackage();
        var book = ProfileBook.ForFiles(package.Paths);
        var now = DateTimeOffset.UtcNow;
        ProfileResult Run(ProfileRequest request) => ProfileVerbs.Run(book, request, now);

        var empty = Run(new ProfileRequest("list"));
        Assert.Contains("No profiles yet.", empty.Words, StringComparison.Ordinal);
        Assert.Contains("Now: your own settings", empty.Words, StringComparison.Ordinal);
        Assert.Null(empty.Data["current"]);
        Assert.Equal(6, empty.Data["presets"]!.AsArray().Count);

        package.EditConfig(c => c.Mirror.MaxFps = 30);
        var saved = Run(new ProfileRequest("save", "Mine"));
        Assert.Equal("Saved Mine with 1 setting.", saved.Words);
        Assert.Contains("settings.", Run(new ProfileRequest("save", "Everything", All: true)).Words, StringComparison.Ordinal);
        Assert.Equal("Saved Picture with 1 setting.", Run(new ProfileRequest("save", "Picture", Groups: ["Mirror"])).Words);

        var shown = Run(new ProfileRequest("show", "mine"));
        Assert.StartsWith("Mine (1 setting)", shown.Words, StringComparison.Ordinal);
        Assert.Contains("Mirror.MaxFps = 30", shown.Words, StringComparison.Ordinal);
        Assert.False(shown.Data["preset"]!.GetValue<bool>());
        Assert.True(Run(new ProfileRequest("show", "Quiet")).Data["preset"]!.GetValue<bool>());
        Assert.Throws<KeyNotFoundException>(() => Run(new ProfileRequest("show", "Nothing")));

        var applied = Run(new ProfileRequest("apply", "Mine"));
        Assert.Equal("Mine applied", applied.Words);
        Assert.Empty(applied.Data["unknown"]!.AsArray());
        Assert.Throws<KeyNotFoundException>(() => Run(new ProfileRequest("apply", "Nothing")));
        book.Store.Save(Make("Older", ("Gone.Setting", 1)));
        var older = Run(new ProfileRequest("apply", "Older"));
        Assert.Equal(["Gone.Setting"], older.Data["unknown"]!.AsArray().Select(n => n!.GetValue<string>()));
        Run(new ProfileRequest("delete", "Older"));
        book.Apply("Mine");

        File.WriteAllText(Path.Combine(book.Store.Folder, "Broken.json"), "nope");
        var listed = Run(new ProfileRequest("list"));
        Assert.Contains("1. Mine · 1 setting", listed.Words, StringComparison.Ordinal);
        Assert.Contains("Broken · could not be read: It is not a profile.", listed.Words, StringComparison.Ordinal);
        Assert.Contains("Now: Mine", listed.Words, StringComparison.Ordinal);
        Assert.Equal("Mine", listed.Data["current"]!.GetValue<string>());

        book.WritePutBack(new PutBack("Quiet", new Dictionary<string, JsonNode>(), new Dictionary<string, JsonNode>()));
        Assert.Contains("Now: Quiet (switched on by itself)", Run(new ProfileRequest("list")).Words, StringComparison.Ordinal);

        Assert.Equal("Mine is now Ours.", Run(new ProfileRequest("rename", "Mine", "Ours")).Words);
        var file = Path.Combine(package.Root, "ours.json");
        Assert.Equal($"Ours written to {file}.", Run(new ProfileRequest("export", "Ours", File: file)).Words);
        Assert.Equal("Imported as Ours (2).", Run(new ProfileRequest("import", File: file)).Words);
        Assert.Equal("Ours deleted.", Run(new ProfileRequest("delete", "Ours")).Words);
        Assert.Null(book.Store.Load("Ours"));
    }

    [Theory]
    [InlineData("profile-1", 1)]
    [InlineData("profile-9", 9)]
    [InlineData("profile-x", 0)]
    [InlineData("favourite-1", 0)]
    [InlineData(null, 0)]
    public void ProfileKeysAreNumbered(string? id, int number) => Assert.Equal(number, Shortcuts.ProfileNumber(id));
}
