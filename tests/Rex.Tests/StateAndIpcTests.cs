using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class StateAndIpcTests
{
    [Fact]
    public void StateStore_RemembersDevicesLockModesAndCalibration()
    {
        using var package = new TestPackage();
        var store = new StateStore(package.Paths.State);

        store.RememberDevice("USB1", "Galaxy", "SM-G998B");
        store.SetLockScreenMode("USB1", LockScreenModes.Pattern);
        store.SetCalibration("USB1", new PatternCalibration(0.2, 0.3, 0.8, 0.7));

        var reloaded = new StateStore(package.Paths.State);
        var profile = reloaded.GetDevice("USB1")!;
        Assert.Equal("Galaxy", profile.Name);
        Assert.Equal(LockScreenModes.Pattern, profile.LockScreenMode);
        Assert.Equal(0.2, profile.Calibration!.Left);
        Assert.Equal("USB1", reloaded.PreferredSerial);

        Assert.Equal(1, reloaded.ResetLockScreen("USB1"));
        Assert.Equal(LockScreenModes.Unknown, reloaded.GetDevice("USB1")!.LockScreenMode);
        Assert.Null(reloaded.GetDevice("USB1")!.Calibration);
        Assert.Throws<ArgumentException>(() => reloaded.SetLockScreenMode("USB1", "face"));
        Assert.Throws<ArgumentException>(() => reloaded.SetCalibration("USB1", new PatternCalibration(0.9, 0.9, 0.1, 0.1)));
    }

    [Fact]
    public void StateStore_StaleInstancesMergeUpdatesInsteadOfOverwriting()
    {
        using var package = new TestPackage();
        var first = new StateStore(package.Paths.State);
        var stale = new StateStore(package.Paths.State);

        first.SetLockScreenMode("PHONE-A", LockScreenModes.Pattern);
        stale.SetLockScreenMode("PHONE-B", LockScreenModes.Other);

        var reloaded = new StateStore(package.Paths.State);
        Assert.Equal(LockScreenModes.Pattern, reloaded.GetDevice("PHONE-A")!.LockScreenMode);
        Assert.Equal(LockScreenModes.Other, reloaded.GetDevice("PHONE-B")!.LockScreenMode);
    }

    [Fact]
    public void StateStore_MigratesLegacyProfilesAndCalibrationFiles()
    {
        using var package = new TestPackage();
        File.WriteAllText(package.Paths.State, """
            { "PreferredSerial": "R3C", "WirelessHosts": ["10.21.217.100"],
              "DeviceProfiles": [ { "Serial": "R3C", "LockScreenMode": "pattern" }, { "Serial": "X", "LockScreenMode": "weird" } ] }
            """);
        Directory.CreateDirectory(Path.Combine(package.Root, "pattern-calibration"));
        File.WriteAllText(Path.Combine(package.Root, "pattern-calibration", "R3C.json"), """{"Left":0.1,"Top":0.2,"Right":0.9,"Bottom":0.8}""");

        var store = new StateStore(package.Paths.State);

        Assert.Equal("R3C", store.PreferredSerial);
        Assert.Equal(["10.21.217.100"], store.WirelessHosts);
        Assert.Equal(LockScreenModes.Pattern, store.GetDevice("R3C")!.LockScreenMode);
        Assert.Equal(0.9, store.GetDevice("R3C")!.Calibration!.Right);
        Assert.Equal(LockScreenModes.Unknown, store.GetDevice("X")!.LockScreenMode);
    }

    [Fact]
    public void StateStore_SurvivesCorruptFileAndKeepsUiState()
    {
        using var package = new TestPackage();
        File.WriteAllText(package.Paths.State, "{{{{");
        var store = new StateStore(package.Paths.State);
        Assert.Empty(store.Devices);

        store.SetUi(new UiState { Width = 1200, Height = 800, SidebarVisible = false, SidebarTab = "phone" });
        var reloaded = new StateStore(package.Paths.State);
        Assert.Equal(1200, reloaded.Ui.Width);
        Assert.False(reloaded.Ui.SidebarVisible);
        Assert.Equal("phone", reloaded.Ui.SidebarTab);
    }

    [Fact]
    public void Ipc_RoundTripsRequestsAndResponses()
    {
        var request = new IpcRequest("action", new Dictionary<string, string> { ["name"] = "sleep" });
        var parsed = Ipc.ParseRequest(Ipc.Serialize(request))!;
        Assert.Equal("action", parsed.Command);
        Assert.Equal("sleep", parsed.Arg("name"));
        Assert.Equal(string.Empty, parsed.Arg("missing"));
        Assert.Null(Ipc.ParseRequest("not json"));

        Assert.False(Ipc.TryParseRequest(
            """{"v":1,"command":"ping","args":{}}""",
            out _,
            out var versionError));
        Assert.Contains("Unsupported protocol version", versionError);

        var response = Ipc.ParseResponse(Ipc.Serialize(IpcResponse.Success(new JsonObject { ["zoom"] = 1.5 })));
        Assert.True(response.Ok);
        Assert.Equal(1.5, response.Data!["zoom"]!.GetValue<double>());

        var failure = Ipc.ParseResponse(Ipc.Serialize(IpcResponse.Fail("nope")));
        Assert.False(failure.Ok);
        Assert.Equal("nope", failure.Error);
        Assert.False(Ipc.ParseResponse("garbage").Ok);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"command\":42}")]
    public void Ipc_RejectsRequestsWithInvalidJsonTypes(string json)
    {
        Assert.Null(Ipc.ParseRequest(json));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"ok\":\"yes\"}")]
    [InlineData("{\"ok\":false,\"error\":[]}")]
    public void Ipc_RejectsResponsesWithInvalidJsonTypes(string json)
    {
        Assert.False(Ipc.ParseResponse(json).Ok);
    }

    [Fact]
    public async Task IpcClient_ReturnsNullWhenNobodyListens()
    {
        var client = new IpcClient("rex-tests-nobody-" + Guid.NewGuid().ToString("N"));
        Assert.Null(await client.SendAsync(new IpcRequest("ping"), TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
        Assert.False(await client.IsAppRunningAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AppPaths_DiscoversRootAndRejectsEscapes()
    {
        using var package = new TestPackage();
        Assert.Equal(package.Root, package.Paths.Root);
        Assert.Equal(Path.Combine(package.Root, "captures", "x"), package.Paths.Inside("captures/x"));
        Assert.Throws<InvalidOperationException>(() => package.Paths.Inside("../outside"));
        Assert.Throws<InvalidOperationException>(() => AppPaths.FromRoot(Path.GetTempPath()));
    }

    [Fact]
    public void ToolLocator_FindsNewestBundledVersion()
    {
        using var package = new TestPackage();
        foreach (var version in new[] { "v4.0", "v4.1", "junk" })
        {
            var dir = Path.Combine(package.Paths.ScrcpyTools, version);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scrcpy.exe"), "");
            File.WriteAllText(Path.Combine(dir, "adb.exe"), "");
        }

        var tools = ToolLocator.Find(package.Paths)!;

        Assert.Equal("v4.1", tools.Version);
        Assert.True(tools.IsComplete);
    }

    [Fact]
    public void ToolLocator_IgnoresInProgressInstallFoldersAndReadsVersionMarker()
    {
        using var package = new TestPackage();

        var staging = Path.Combine(package.Paths.ScrcpyTools, ".install-deadbeef");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "scrcpy.exe"), "");
        File.WriteAllText(Path.Combine(staging, "adb.exe"), "");

        var installed = Path.Combine(package.Paths.ScrcpyTools, "v4.1-abcdef123456");
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "scrcpy.exe"), "");
        File.WriteAllText(Path.Combine(installed, "adb.exe"), "");
        File.WriteAllText(Path.Combine(installed, ".rex-version"), "v4.1");

        var tools = ToolLocator.Find(package.Paths)!;

        Assert.Equal("v4.1", tools.Version);
        Assert.Equal(Path.Combine(installed, "adb.exe"), tools.Adb);
    }

    [Fact]
    public void Log_RotatesWhenTooLarge()
    {
        using var package = new TestPackage();
        var log = new RexLog(package.Paths.LogFile, new LoggingSettings { MaxBytes = 64 * 1024, KeepFiles = 2 });
        var line = new string('x', 2000);
        for (var i = 0; i < 40; i++)
        {
            log.Info(line);
        }

        Assert.True(File.Exists(package.Paths.LogFile + ".1"));
        Assert.True(new FileInfo(package.Paths.LogFile).Length < 70 * 1024);
        Assert.NotEmpty(log.Tail(3));
    }

    [Fact]
    public void Diagnostics_ReportSerializesToTextAndJson()
    {
        var report = new DiagnosticsReport("C:\\r", null, null, null, null, false, false,
            [new DiagnosedDevice(new AdbDevice("X", "unauthorized", false, "", ""), null, "approve")], ["line"]);

        Assert.Contains("NOT INSTALLED", report.ToText());
        Assert.False(report.SetupComplete);
        Assert.Equal("unauthorized", report.ToJson()["devices"]![0]!["state"]!.GetValue<string>());
    }
}
