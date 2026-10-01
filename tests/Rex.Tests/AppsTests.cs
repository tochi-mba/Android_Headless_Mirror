using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>The phone's apps: reading scrcpy's list, finding and ordering apps, and what is sent to the phone.</summary>
public sealed class AppsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Listing = """
        INFO: scrcpy 4.1 <https://github.com/Genymobile/scrcpy>
        [server] INFO: Device: [samsung] samsung SM-G998B (Android 15)
        [server] INFO: List of apps:
         * Phone                          com.android.dialer
         * Settings                       com.android.settings
         - A Very Long Application Name Here
                                          com.example.longname
         - Café Maps                      com.example.cafe
         - Example One                    com.example.one
         - Notes                          com.example.notes
         - Notes                          org.other.notes
         - Example One again              com.example.one

         - not an app line
        garbage that is not indented
         - Loose Name com.example.loose
         - Dangling long name that never gets a package line here at all
         -                                com.example.nameless
        """;

    private static IReadOnlyList<PhoneApp> Apps => AppList.Parse(Listing.Split('\n'));

    [Fact]
    public void ScrcpysAppListIsRead()
    {
        Assert.Equal(
        [
            new PhoneApp("Phone", "com.android.dialer", true),
            new PhoneApp("Settings", "com.android.settings", true),
            new PhoneApp("A Very Long Application Name Here", "com.example.longname", false),
            new PhoneApp("Café Maps", "com.example.cafe", false),
            new PhoneApp("Example One", "com.example.one", false),
            new PhoneApp("Notes", "com.example.notes", false),
            new PhoneApp("Notes", "org.other.notes", false),
            new PhoneApp("Loose Name", "com.example.loose", false),
            new PhoneApp("com.example.nameless", "com.example.nameless", false),
        ], Apps);
    }

    [Fact]
    public void NothingBeforeTheHeaderIsAnApp()
    {
        Assert.Empty(AppList.Parse([" - Example One                    com.example.one", "no header here"]));
        Assert.Empty(AppList.Parse([]));
    }

    [Fact]
    public void ALineWithWindowsEndingsIsReadToo()
    {
        var apps = AppList.Parse(["[server] INFO: List of apps:\r", " - Spotify                        com.spotify.music\r"]);
        Assert.Equal([new PhoneApp("Spotify", "com.spotify.music", false)], apps);
    }

    [Theory]
    [InlineData("", "com.android.dialer,com.android.settings,com.example.longname,com.example.cafe,com.example.one,com.example.notes,org.other.notes,com.example.loose,com.example.nameless")]
    [InlineData("   ", "com.android.dialer,com.android.settings,com.example.longname,com.example.cafe,com.example.one,com.example.notes,org.other.notes,com.example.loose,com.example.nameless")]
    [InlineData("cafe", "com.example.cafe")]
    [InlineData("CAFÉ", "com.example.cafe")]
    [InlineData("notes", "com.example.notes,org.other.notes")]
    [InlineData("one", "com.example.one,com.android.dialer")]
    [InlineData("example", "com.example.one,com.example.nameless,com.example.longname,com.example.cafe,com.example.notes,com.example.loose")]
    [InlineData("long name", "com.example.longname")]
    [InlineData("name", "com.example.longname,com.example.loose,com.example.nameless")]
    [InlineData("dialer", "com.android.dialer")]
    [InlineData("zzz", "")]
    public void SearchFindsByNameThenPackage(string query, string expected)
    {
        Assert.Equal(expected, string.Join(',', AppList.Search(Apps, query).Select(a => a.Package)));
    }

    [Fact]
    public void SearchPutsANameThatStartsWithItFirst()
    {
        PhoneApp[] apps = [new("My Maps", "com.a.maps", false), new("Maps", "com.b.maps", false), new("Roadmaps", "com.c.road", false), new("Other", "com.maps.other", false)];
        Assert.Equal("com.b.maps,com.a.maps,com.c.road,com.maps.other", string.Join(',', AppList.Search(apps, "maps").Select(a => a.Package)));
        Assert.Equal("com.a.maps", string.Join(',', AppList.Search(apps, "my  maps").Select(a => a.Package)));
        Assert.Equal(apps, AppList.Search(apps, null));
    }

    [Fact]
    public void AnAppIsFoundByPackageThenNameThenTheOnlyStart()
    {
        Assert.Equal("com.example.cafe", AppList.Resolve(Apps, "COM.EXAMPLE.CAFE").App!.Package);
        Assert.Equal("com.example.cafe", AppList.Resolve(Apps, " cafe maps ").App!.Package);
        Assert.Equal("com.example.longname", AppList.Resolve(Apps, "A Very").App!.Package);

        var twice = AppList.Resolve(Apps, "Notes");
        Assert.Null(twice.App);
        Assert.Equal(2, twice.Candidates.Count);
        Assert.Contains("org.other.notes", twice.Error, StringComparison.Ordinal);
        Assert.Contains("Use the package name", twice.Error, StringComparison.Ordinal);

        var none = AppList.Resolve(Apps, "Nothing");
        Assert.Null(none.App);
        Assert.Empty(none.Candidates);
        Assert.Equal("No app on the phone is called \"Nothing\".", none.Error);
        Assert.Null(AppList.Resolve(Apps, "  ").App);
    }

    [Theory]
    [InlineData("com.example.one", true)]
    [InlineData("a.b", true)]
    [InlineData("com.Example_2.x9", true)]
    [InlineData("example", false)]
    [InlineData("1com.example", false)]
    [InlineData("com..example", false)]
    [InlineData("com.example.", false)]
    [InlineData("com.example; reboot", false)]
    [InlineData("com.example.one'", false)]
    [InlineData("com.example/one", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void APackageNameIsCheckedBeforeItReachesThePhone(string? name, bool valid)
    {
        Assert.Equal(valid, PackageName.IsValid(name));
        Assert.False(PackageName.IsValid("a." + new string('b', PackageName.Longest)));
    }

    [Theory]
    [InlineData("com.example.one/.MainActivity", true)]
    [InlineData("com.example.one/com.example.one.Main$Inner", true)]
    [InlineData("com.example.one/Main", true)]
    [InlineData("com.example.one/", false)]
    [InlineData("/.Main", false)]
    [InlineData("example/.Main", false)]
    [InlineData("com.example.one/.Main;rm", false)]
    [InlineData("com.example.one", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AComponentIsCheckedToo(string? component, bool valid) => Assert.Equal(valid, PackageName.IsValidComponent(component));

    [Theory]
    [InlineData("Spotify", "S")]
    [InlineData("  éclair", "É")]
    [InlineData("1Password", "1")]
    [InlineData("!!!", "?")]
    [InlineData("", "?")]
    public void EveryAppGetsALetter(string name, string letter) => Assert.Equal(letter, LetterTile.Letter(name));

    [Fact]
    public void EveryAppGetsTheSameTintEveryTime()
    {
        Assert.Equal("?", LetterTile.Letter(null!));
        var tints = Apps.Select(a => LetterTile.Tint(a.Package)).ToArray();
        Assert.Equal(tints, Apps.Select(a => LetterTile.Tint(a.Package)));
        Assert.All(tints, t => Assert.InRange(t, 0, LetterTile.Tints - 1));
        Assert.True(tints.Distinct().Count() > 1);
        Assert.InRange(LetterTile.Tint(null!), 0, LetterTile.Tints - 1);
        Assert.Equal(LetterTile.Tint(string.Empty), LetterTile.Tint(null!));
    }

    [Fact]
    public void AppsSettingsAreKeptSane()
    {
        var settings = new AppsSettings
        {
            SortBy = "MOST-USED",
            Layout = "sideways",
            RecentCount = 999,
            FavouritesOnControlsMost = 0,
            Hidden = [" com.example.one ", "com.example.one", "not a package", null!, .. Enumerable.Range(0, 600).Select(i => $"com.example.p{i}")],
        };
        settings.Normalize();
        Assert.Equal("most-used", settings.SortBy);
        Assert.Equal("list", settings.Layout);
        Assert.Equal(AppsSettings.MostRecent, settings.RecentCount);
        Assert.Equal(1, settings.FavouritesOnControlsMost);
        Assert.Equal(AppsSettings.MostHidden, settings.Hidden.Count);
        Assert.Equal("com.example.one", settings.Hidden[0]);
        Assert.Equal("com.example.p0", settings.Hidden[1]);

        var low = new AppsSettings { RecentCount = -4, FavouritesOnControlsMost = 40, Hidden = null! };
        low.Normalize();
        Assert.Equal(0, low.RecentCount);
        Assert.Equal(AppsSettings.MostOnControls, low.FavouritesOnControlsMost);
        Assert.Empty(low.Hidden);

        var copy = settings.Copy();
        copy.Hidden.Clear();
        Assert.NotEmpty(settings.Hidden);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PhoneApp[] Shelf =
    [
        new("beta", "com.example.beta", false),
        new("Alpha", "com.example.alpha", false),
        new("Gamma", "com.example.gamma", false),
        new("Delta", "com.example.delta", false),
        new("Settings", "com.android.settings", true),
        new("Clock", "com.android.clock", true),
    ];

    private static readonly RecentApp[] Opened =
    [
        new("com.example.gamma", Now.AddMinutes(-1), 1),
        new("com.example.delta", Now.AddMinutes(-30), 9),
        new("com.android.settings", Now.AddMinutes(-5), 2),
        new("com.gone.app", Now, 4),
    ];

    private static string Show(IReadOnlyList<AppSection> sections) =>
        string.Join(" | ", sections.Select(s => s.Id + ": " + string.Join(",", s.Apps.Select(e =>
            e.App.Name + (e.Favourite ? "*" : string.Empty) + (e.Missing ? "?" : string.Empty) + (e.Hidden ? "-" : string.Empty)))));

    [Fact]
    public void AppsAreOrderedAsAsked()
    {
        var settings = new AppsSettings { Hidden = ["com.example.beta"] };
        string[] favourites = ["com.example.delta", "com.missing.app", "com.example.beta"];

        // An app the person opened is recent even when it came with the phone.
        Assert.Equal("favourites: Delta*,com.missing.app*?,beta*- | recent: Gamma,Settings | yours: Alpha,Delta*,Gamma",
            Show(AppOrder.Sections(Shelf, favourites, Opened, settings)));

        settings.ShowSystem = true;
        Assert.Equal("favourites: Delta*,com.missing.app*?,beta*- | recent: Gamma,Settings | yours: Alpha,Delta*,Gamma | system: Clock,Settings",
            Show(AppOrder.Sections(Shelf, favourites, Opened, settings)));

        Assert.Equal("favourites: Delta*,com.missing.app*?,beta*- | recent: Gamma,Settings | yours: Alpha,Delta*,Gamma | system: Clock,Settings | hidden: beta*-",
            Show(AppOrder.Sections(Shelf, favourites, Opened, settings, showHidden: true)));

        settings.SortBy = "recent";
        Assert.Equal("yours: Gamma,Delta,Alpha | system: Settings,Clock", Show(AppOrder.Sections(Shelf, [], Opened, settings)));

        settings.SortBy = "most-used";
        Assert.Equal("yours: Delta,Gamma,Alpha | system: Settings,Clock", Show(AppOrder.Sections(Shelf, [], Opened, settings)));

        settings.SortBy = "name";
        settings.ShowRecent = false;
        Assert.Equal("yours: Alpha,Delta,Gamma | system: Clock,Settings", Show(AppOrder.Sections(Shelf, [], Opened, settings)));

        settings.ShowRecent = true;
        settings.RecentCount = 0;
        Assert.Equal("yours: Alpha,Delta,Gamma | system: Clock,Settings", Show(AppOrder.Sections(Shelf, [], Opened, settings)));
        Assert.Empty(AppOrder.Sections([], [], [], new AppsSettings()));
    }

    [Fact]
    public void ASearchIsOneListOfMatchesThatReachesSystemApps()
    {
        var settings = new AppsSettings { Hidden = ["com.example.alpha"] };
        Assert.Equal("matches: Settings", Show(AppOrder.Sections(Shelf, [], [], settings, "sett")));
        Assert.Equal("matches: Delta*", Show(AppOrder.Sections(Shelf, ["com.example.delta"], [], settings, "delta")));
        Assert.Empty(AppOrder.Sections(Shelf, [], [], settings, "alpha"));
        Assert.Equal("matches: Alpha-", Show(AppOrder.Sections(Shelf, [], [], settings, "alpha", showHidden: true)));
    }

    [Fact]
    public void TheRecentGroupShowsAFewOnly()
    {
        var many = Enumerable.Range(0, 9).Select(i => new PhoneApp($"App {i}", $"com.example.a{i}", false)).ToArray();
        var recent = many.Select((a, i) => new RecentApp(a.Package, Now.AddMinutes(-i), 1)).ToArray();
        var group = AppOrder.Sections(many, [], recent, new AppsSettings()).Single(s => s.Id == AppOrder.Recent);
        Assert.Equal(AppOrder.RecentShown, group.Apps.Count);
        Assert.Equal("App 0", group.Apps[0].App.Name);
    }

    [Fact]
    public void OpeningStarringAndMovingKeepTheirOrder()
    {
        var once = AppOrder.Opened([], "com.a.one", Now, 3);
        var twice = AppOrder.Opened(once, "com.a.two", Now.AddMinutes(1), 3);
        var again = AppOrder.Opened(twice, "com.a.one", Now.AddMinutes(2), 3);
        Assert.Equal([new RecentApp("com.a.one", Now.AddMinutes(2), 2), new RecentApp("com.a.two", Now.AddMinutes(1), 1)], again);
        Assert.Single(AppOrder.Opened(again, "com.a.three", Now.AddMinutes(3), 1));
        Assert.Empty(AppOrder.Opened(again, "com.a.three", Now, -2));

        Assert.Equal(["a.a", "b.b"], AppOrder.Star(["a.a"], "b.b", true));
        Assert.Equal(["a.a", "b.b"], AppOrder.Star(["a.a", "b.b"], "b.b", true));
        Assert.Equal(["b.b"], AppOrder.Star(["a.a", "b.b"], "a.a", false));

        Assert.Equal(["b.b", "a.a", "c.c"], AppOrder.Move(["a.a", "b.b", "c.c"], "b.b", -1));
        Assert.Equal(["b.b", "a.a", "c.c"], AppOrder.Move(["a.a", "b.b", "c.c"], "a.a", 1));
        Assert.Equal(["b.b", "c.c", "a.a"], AppOrder.Move(["a.a", "b.b", "c.c"], "a.a", 10));
        Assert.Equal(["a.a", "b.b"], AppOrder.Move(["a.a", "b.b"], "a.a", -5));
        Assert.Equal(["a.a", "b.b"], AppOrder.Move(["a.a", "b.b"], "z.z", 1));
    }

    [Fact]
    public void TheRememberedAppsRoundTripThroughState()
    {
        var path = Path.Combine(Path.GetTempPath(), "rex-tests-" + Guid.NewGuid().ToString("N"), "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var store = new StateStore(path);
            Assert.Null(store.GetDevice("S1"));
            store.SetApps("S1", Shelf, Now);
            store.SetFavourite("S1", "com.example.gamma", true);
            store.SetFavourite("S1", "com.example.alpha", true);
            store.SetFavourite("S1", "com.example.alpha", true);
            store.MoveFavourite("S1", "com.example.alpha", -1);
            store.SetFavourite("S1", "com.example.gamma", false);
            store.SetFavourite("S1", "com.example.delta", true);
            store.NoteAppOpened("S1", "com.example.beta", Now, 2);
            store.NoteAppOpened("S1", "com.example.beta", Now.AddMinutes(1), 2);
            store.NoteAppOpened("S1", "com.example.alpha", Now.AddMinutes(2), 2);
            store.NoteAppOpened("S1", "com.example.delta", Now.AddMinutes(3), 2);

            var again = new StateStore(path).GetDevice("S1")!;
            Assert.Equal<PhoneApp>(Shelf, again.Apps!);
            Assert.Equal(Now, again.AppsReadUtc);
            Assert.Equal(["com.example.alpha", "com.example.delta"], again.FavouriteApps);
            Assert.Equal(["com.example.delta", "com.example.alpha"], again.RecentApps.Select(r => r.Package));

            // A phone seen before this version has no apps, favourites or recent apps yet.
            store.RememberDevice("S2", "Old phone", "X");
            var old = new StateStore(path).GetDevice("S2")!;
            Assert.Null(old.Apps);
            Assert.Empty(old.FavouriteApps);
            Assert.Empty(old.RecentApps);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "a minute ago")]
    [InlineData(150, "2 minutes ago")]
    [InlineData(3599, "59 minutes ago")]
    [InlineData(3600, "an hour ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(86_399, "23 hours ago")]
    [InlineData(86_400, "yesterday")]
    [InlineData(172_800, "2 days ago")]
    public void HowLongAgoIsSaidInWords(int seconds, string words) => Assert.Equal(words, TimeWords.Ago(Now.AddSeconds(-seconds), Now));

    // ----- Reading the list and talking to the phone -----

    [Fact]
    public async Task TheListIsReadWithScrcpyWithoutCleaningUp()
    {
        var runner = new FakeProcessRunner { Respond = _ => new ProcessResult(0, string.Empty, Listing) };
        var read = await AppLister.ReadAsync(runner, "scrcpy.exe", "S1", cancellationToken: Ct);
        Assert.True(read.Ok);
        Assert.Equal(Apps, read.Apps);
        Assert.Equal("scrcpy.exe", runner.Calls[0].FileName);
        Assert.Equal(["--serial=S1", "--list-apps", "--no-cleanup"], runner.Calls[0].Arguments);
    }

    [Theory]
    [InlineData(0, "", "", false, "The phone listed no apps.")]
    [InlineData(0, "", "", true, "The phone took too long to list its apps.")]
    [InlineData(1, "", "INFO: scrcpy 4.1\nERROR: Could not find any ADB device\n", false, "Could not find any ADB device")]
    [InlineData(1, "", "something broke", false, "something broke")]
    public async Task AFailedReadSaysWhy(int exit, string stdout, string stderr, bool timedOut, string why)
    {
        var runner = new FakeProcessRunner { Respond = _ => new ProcessResult(exit, stdout, stderr, timedOut) };
        var read = await AppLister.ReadAsync(runner, "scrcpy.exe", "S1", cancellationToken: Ct);
        Assert.False(read.Ok);
        Assert.Empty(read.Apps);
        Assert.Equal(why, read.Error);
    }

    private static (AdbClient Adb, FakeProcessRunner Runner) Phone(bool resolves = true, string answer = "")
    {
        var runner = new FakeProcessRunner
        {
            Respond = args => args.Contains("resolve-activity")
                ? new ProcessResult(0, resolves ? "priority=0 preferredOrder=0\ncom.example.one/.MainActivity\n" : "No activity found\n", string.Empty)
                : new ProcessResult(0, answer, string.Empty),
        };
        return (new AdbClient("adb", runner), runner);
    }

    private static string[] Shell(FakeProcessRunner runner) =>
        runner.Calls.Select(c => string.Join(' ', c.Arguments.SkipWhile(a => a != "shell").Skip(1))).ToArray();

    [Fact]
    public async Task LaunchingRunsTheRightCommand()
    {
        var (adb, runner) = Phone();
        Assert.True((await adb.LaunchAppAsync("S1", "com.example.one", fresh: false, cancellationToken: Ct)).Ok);
        Assert.True((await adb.LaunchAppAsync("S1", "com.example.one", fresh: true, displayId: 7, cancellationToken: Ct)).Ok);
        Assert.Equal(
        [
            "cmd package resolve-activity --brief -c android.intent.category.LAUNCHER com.example.one",
            "am start -n com.example.one/.MainActivity",
            "cmd package resolve-activity --brief -c android.intent.category.LAUNCHER com.example.one",
            "am start -S --display 7 -n com.example.one/.MainActivity",
        ], Shell(runner));
        Assert.Equal(["-s", "S1", "shell"], runner.Calls[0].Arguments[..3]);
    }

    [Fact]
    public async Task AnAppWithoutALauncherActivityIsOpenedAsTheLauncherWould()
    {
        var (adb, runner) = Phone(resolves: false);
        Assert.Null(await adb.ResolveLauncherAsync("S1", "com.example.one", cancellationToken: Ct));
        Assert.True((await adb.LaunchAppAsync("S1", "com.example.one", fresh: false, cancellationToken: Ct)).Ok);
        Assert.True((await adb.LaunchAppAsync("S1", "com.example.one", fresh: true, cancellationToken: Ct)).Ok);
        var refused = await adb.LaunchAppAsync("S1", "com.example.one", fresh: false, displayId: 3, cancellationToken: Ct);
        Assert.False(refused.Ok);
        Assert.Equal("This app cannot be opened on another screen.", refused.Text);
        Assert.Equal(
        [
            "monkey -p com.example.one -c android.intent.category.LAUNCHER 1",
            "am force-stop com.example.one",
            "monkey -p com.example.one -c android.intent.category.LAUNCHER 1",
        ], Shell(runner).Where(c => !c.StartsWith("cmd", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheChoresRunTheirOwnCommands()
    {
        var (adb, runner) = Phone(answer: "Success\n");
        Assert.True((await adb.ForceStopAsync("S1", "com.example.one", cancellationToken: Ct)).Ok);
        Assert.True((await adb.OpenAppInfoAsync("S1", "com.example.one", cancellationToken: Ct)).Ok);
        Assert.True((await adb.ClearAppDataAsync("S1", "com.example.one", cancellationToken: Ct)).Ok);
        Assert.Equal("Success", (await adb.UninstallAppAsync("S1", "com.example.one", cancellationToken: Ct)).Text);
        Assert.Equal(
        [
            "am force-stop com.example.one",
            "am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:com.example.one",
            "pm clear com.example.one",
            "pm uninstall com.example.one",
        ], Shell(runner));
    }

    [Theory]
    [InlineData("com.example.one; reboot")]
    [InlineData("example")]
    public async Task APackageThatIsNotOneNeverReachesThePhone(string package)
    {
        var (adb, runner) = Phone();
        Assert.Null(await adb.ResolveLauncherAsync("S1", package, cancellationToken: Ct));
        Assert.False((await adb.LaunchAppAsync("S1", package, fresh: false, cancellationToken: Ct)).Ok);
        Assert.False((await adb.ForceStopAsync("S1", package, cancellationToken: Ct)).Ok);
        Assert.False((await adb.UninstallAppAsync("S1", package, cancellationToken: Ct)).Ok);
        Assert.Contains("is not an app's package name", (await adb.ClearAppDataAsync("S1", package, cancellationToken: Ct)).Text, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(0, "Starting: Intent { cmp=com.example.one/.Main }", true, "Starting: Intent { cmp=com.example.one/.Main }")]
    [InlineData(0, "Starting: Intent\nError: Activity class {x} does not exist.", false, "Error: Activity class {x} does not exist.")]
    [InlineData(0, "Failure [DELETE_FAILED_INTERNAL_ERROR]", false, "Failure [DELETE_FAILED_INTERNAL_ERROR]")]
    [InlineData(0, "Failed", false, "Failed")]
    [InlineData(0, "** No activities found to run, monkey aborted.", false, "** No activities found to run, monkey aborted.")]
    [InlineData(255, "", false, "exit code 255")]
    public void WhatAmAndPmSayIsReadAsSuccessOrFailure(int exit, string stdout, bool ok, string text)
    {
        var result = AdbClient.Answer(new ProcessResult(exit, stdout, string.Empty));
        Assert.Equal(ok, result.Ok);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public async Task OneServerStartsAtATime()
    {
        var gate = new ServerStartGate();
        Assert.False(gate.Busy);
        var first = await gate.EnterAsync(Ct);
        Assert.True(gate.Busy);
        var second = gate.EnterAsync(Ct);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        first.Dispose();
        first.Dispose();
        var held = await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(gate.Busy);
        held.Dispose();
        Assert.False(gate.Busy);

        using var cancelled = new CancellationTokenSource();
        using (await gate.EnterAsync(TestContext.Current.CancellationToken))
        {
            var waiting = gate.EnterAsync(cancelled.Token);
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        Assert.False(gate.Busy);
    }
}
