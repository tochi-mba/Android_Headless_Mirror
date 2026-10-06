using System.Text.Json.Nodes;
using Rex.Cli;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Runs the CLI in-process against a package that contains the fake adb.</summary>
public sealed class CliTests
{
    public static TheoryData<string[], string[]> MachineArgumentCases => new()
    {
        { new[] { "agent", "status" }, new[] { "status" } },
        { new[] { "--json", "status" }, new[] { "status" } },
        { new[] { "status", "--json" }, new[] { "status" } },
        { new[] { "--plain", "status" }, new[] { "status" } },
        { new[] { "agent", "--json", "android", "list", "global" }, new[] { "android", "list", "global" } },
        { new[] { "agent" }, Array.Empty<string>() },
    };

    [Theory]
    [MemberData(nameof(MachineArgumentCases))]
    public void ExtractArguments_NormalizesMachineSpellings(string[] input, string[] expected) =>
        Assert.Equal(expected, MachineMode.ExtractArguments(input));

    [Theory]
    [InlineData("status")]
    [InlineData("help")]
    public void ExtractArguments_ReturnsNullForHumanCli(string command) =>
        Assert.Null(MachineMode.ExtractArguments([command]));

    [Fact]
    public void Positional_SkipsOptionsAndTheirValues()
    {
        Assert.Equal(["action", "sleep"], Arguments.Positional(["action", "sleep", "--serial", "X"]));
        Assert.Equal(["push", "one.txt"], Arguments.Positional(["push", "one.txt", "--TO", "/sdcard/Pictures/"]));
        Assert.Equal("X", Arguments.Option(["--SERIAL", "X"], "--serial"));
        Assert.Throws<ArgumentException>(() => Arguments.Require(["a"], 2, "usage"));
    }

    [Fact]
    public async Task Capabilities_DescribeTheContract()
    {
        using var package = new TestPackage(withFakeTools: true);
        var result = await MachineMode.RunAsync([], new CliContext(package.Paths));

        Assert.Equal(0, result.ExitCode);
        var doc = JsonNode.Parse(result.Json)!.AsObject();
        Assert.True(doc["ok"]!.GetValue<bool>());
        Assert.Equal(MachineMode.ProtocolVersion, doc["protocolVersion"]!.GetValue<int>());
        Assert.Contains("action", doc["data"]!["commands"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Contains(doc["data"]!["configPaths"]!.AsArray().Select(x => x!.GetValue<string>()), p => p == "Mirror.MaxFps");
        Assert.DoesNotContain('\n', result.Json);

        // Every action is described in words; key codes are how it is done, not what it does.
        var actions = doc["data"]!["actions"]!.AsArray();
        Assert.Equal(MirrorActions.All.Count, actions.Count);
        Assert.All(actions, a => Assert.DoesNotContain("KEYCODE", a!["detail"]!.GetValue<string>(), StringComparison.Ordinal));
        Assert.Contains(actions, a => a!["id"]!.GetValue<string>() == "mute" && a["detail"]!.GetValue<string>() == "Mute or unmute");
    }

    [Fact]
    public async Task Devices_UsesTheBundledFakeAdb()
    {
        using var package = new TestPackage(withFakeTools: true);
        var result = await MachineMode.RunAsync(["devices"], new CliContext(package.Paths));

        var doc = JsonNode.Parse(result.Json)!.AsObject();
        Assert.True(doc["ok"]!.GetValue<bool>(), result.Json);
        Assert.Equal("FAKE123", doc["data"]![0]!["serial"]!.GetValue<string>());
        Assert.Contains(package.AdbCalls(), line => line == "devices -l");
    }

    [Fact]
    public async Task Action_UsesAdbForNavigationWithoutTheApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        var result = await MachineMode.RunAsync(["action", "home"], new CliContext(package.Paths));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(package.AdbCalls(), line => line == "-s FAKE123 shell input keyevent KEYCODE_HOME");
    }

    [Fact]
    public async Task Action_OpensTheKeyboardSettingsWithoutTheApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        var result = await MachineMode.RunAsync(["action", "keyboard-layout"], new CliContext(package.Paths));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(package.AdbCalls(), line => line ==
            "-s FAKE123 shell am start -a android.settings.HARD_KEYBOARD_SETTINGS");
    }

    [Fact]
    public async Task HumanAction_OpensTheKeyboardSettingsWithoutTheApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.Equal(0, await Commands.RunAsync(["action", "keyboard-layout"], new CliContext(package.Paths)));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("Keyboard layout", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(package.AdbCalls(), line => line ==
            "-s FAKE123 shell am start -a android.settings.HARD_KEYBOARD_SETTINGS");
    }

    [Fact]
    public async Task Action_NeedsTheAppForScrcpyShortcuts()
    {
        using var package = new TestPackage(withFakeTools: true);
        Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, "rex-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await MachineMode.RunAsync(["action", "sleep"], new CliContext(package.Paths));
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("not running", result.Json);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, null);
        }
    }

    [Theory]
    [InlineData("rotation-portrait", "user_rotation 0")]
    [InlineData("rotation-landscape", "user_rotation 1")]
    [InlineData("rotation-auto", "accelerometer_rotation 1")]
    public async Task Action_ChangesPhoneOrientationWithoutTheApp(string action, string setting)
    {
        using var package = new TestPackage(withFakeTools: true);
        var result = await MachineMode.RunAsync(["action", action], new CliContext(package.Paths));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(package.AdbCalls(), call => call == $"-s FAKE123 shell \"settings put system {setting}\"");
    }

    [Fact]
    public async Task Android_ListAndProtectedWrite()
    {
        using var package = new TestPackage(withFakeTools: true);
        var context = new CliContext(package.Paths);

        var list = JsonNode.Parse((await MachineMode.RunAsync(["android", "list", "system", "--filter", "font"], context)).Json)!.AsObject();
        var rows = list["data"]!["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("font_scale", rows[0]!["key"]!.GetValue<string>());

        var blocked = await MachineMode.RunAsync(["android", "set", "global", "adb_enabled", "0"], context);
        Assert.Equal(1, blocked.ExitCode);
        Assert.Contains("protected", blocked.Json);
        Assert.DoesNotContain(package.AdbCalls(), line => line.Contains("adb_enabled 0", StringComparison.Ordinal));

        var ok = await MachineMode.RunAsync(["android", "set", "system", "font_scale", "1.15"], context);
        Assert.Equal(0, ok.ExitCode);
    }

    [Fact]
    public async Task Phone_ListsTheCatalogueAndWritesOneSetting()
    {
        using var package = new TestPackage(withFakeTools: true);
        var context = new CliContext(package.Paths);

        var listed = JsonNode.Parse((await MachineMode.RunAsync(["phone", "list"], context)).Json)!.AsObject();
        Assert.True(listed["ok"]!.GetValue<bool>(), listed.ToJsonString());
        var rows = listed["data"]!.AsArray();
        var brightness = rows.Single(r => r!["id"]!.GetValue<string>() == "brightness")!;
        Assert.Equal("128", brightness["value"]!.GetValue<string>());
        Assert.Equal("Display", brightness["group"]!.GetValue<string>());
        Assert.Equal("slider", brightness["kind"]!.GetValue<string>());
        Assert.True(brightness["canReset"]!.GetValue<bool>());
        // The fake phone never reported these keys, so the catalogue leaves them out.
        Assert.DoesNotContain(rows, r => r!["id"]!.GetValue<string>() == "night-light");

        var set = await MachineMode.RunAsync(["phone", "set", "show-touches", "1"], context);
        Assert.Equal(0, set.ExitCode);
        Assert.Contains(package.AdbCalls(), call => call.Contains("settings put system show_touches 1", StringComparison.Ordinal));

        var reset = await MachineMode.RunAsync(["phone", "reset", "show-touches"], context);
        Assert.Equal(0, reset.ExitCode);
        Assert.Contains(package.AdbCalls(), call => call.Contains("settings delete system show_touches", StringComparison.Ordinal));

        var rejected = await MachineMode.RunAsync(["phone", "set", "brightness", "900"], context);
        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("between", rejected.Json);
    }

    [Fact]
    public async Task Config_SetAndRestoreThroughMachineMode()
    {
        using var package = new TestPackage();
        var context = new CliContext(package.Paths);

        var set = JsonNode.Parse((await MachineMode.RunAsync(["config", "set", "Mirror.MaxFps", "90"], context)).Json)!.AsObject();
        Assert.Equal("90", set["data"]!["value"]!.GetValue<string>());

        var restore = JsonNode.Parse((await MachineMode.RunAsync(["config", "restore"], context)).Json)!.AsObject();
        Assert.True(restore["data"]!["restored"]!.GetValue<bool>());
        Assert.Equal("60", context.Config.Get("Mirror.MaxFps").Value);
    }

    [Fact]
    public async Task Config_SetRejectsMalformedExtraArgsInMachineMode()
    {
        using var package = new TestPackage();
        var context = new CliContext(package.Paths);

        var result = await MachineMode.RunAsync(["config", "set", "Mirror.ExtraArgs", "\"unterminated"], context);

        Assert.Equal(1, result.ExitCode);
        var doc = JsonNode.Parse(result.Json)!.AsObject();
        Assert.False(doc["ok"]!.GetValue<bool>());
        Assert.Equal("FormatException", doc["error"]!["type"]!.GetValue<string>());
        Assert.Equal(string.Empty, context.Config.Get("Mirror.ExtraArgs").Value);
        Assert.DoesNotContain('\n', result.Json);
    }

    [Fact]
    public async Task Sound_NeedsTheApp()
    {
        using var package = new TestPackage();
        Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, "rex-tests-nobody-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new CliContext(package.Paths);
            var result = await MachineMode.RunAsync(["sound", "40"], context);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("not running", JsonNode.Parse(result.Json)!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

            var refused = await MachineMode.RunAsync(["sound", "louder"], context);
            Assert.Equal("ArgumentException", JsonNode.Parse(refused.Json)!["error"]!["type"]!.GetValue<string>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, null);
        }
    }

    [Fact]
    public async Task App_ListOpenAndStarWithoutTheDesktopApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, "rex-tests-nobody-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new CliContext(package.Paths);
            var listed = JsonNode.Parse((await MachineMode.RunAsync(["app", "list"], context)).Json)!;
            var apps = listed["data"]!["apps"]!.AsArray();
            Assert.Equal(12, apps.Count);
            Assert.DoesNotContain(apps, a => a!["system"]!.GetValue<bool>());
            Assert.Equal("A Very Long Application Name Here", apps[0]!["name"]!.GetValue<string>());
            Assert.Equal(14, JsonNode.Parse((await MachineMode.RunAsync(["app", "list", "--system"], context)).Json)!["data"]!["apps"]!.AsArray().Count);
            Assert.Single(JsonNode.Parse((await MachineMode.RunAsync(["app", "list", "dialer"], context)).Json)!["data"]!["apps"]!.AsArray());
            // Read with scrcpy, and remembered for the phone as the app would.
            Assert.Equal(14, new StateStore(package.Paths.State).GetDevice("FAKE123")!.Apps!.Count);

            var opened = await MachineMode.RunAsync(["app", "open", "Café", "Maps"], context);
            Assert.Equal(0, opened.ExitCode);
            Assert.Equal("com.example.cafe", JsonNode.Parse(opened.Json)!["data"]!["opened"]!.GetValue<string>());
            Assert.Contains(package.AdbCalls(), c => c.Contains("am start -n com.example.cafe/.MainActivity", StringComparison.Ordinal));
            Assert.Equal("com.example.cafe", new StateStore(package.Paths.State).GetDevice("FAKE123")!.RecentApps[0].Package);

            Assert.Equal(0, (await MachineMode.RunAsync(["app", "close", "com.example.cafe"], context)).ExitCode);
            Assert.Equal(0, (await MachineMode.RunAsync(["app", "info", "com.example.cafe"], context)).ExitCode);
            Assert.Contains(package.AdbCalls(), c => c.Contains("am force-stop com.example.cafe", StringComparison.Ordinal));

            Assert.Equal(0, (await MachineMode.RunAsync(["app", "favourite", "com.example.cafe", "on"], context)).ExitCode);
            Assert.Equal(["com.example.cafe"], new StateStore(package.Paths.State).GetDevice("FAKE123")!.FavouriteApps);
            Assert.Equal(0, await Commands.RunAsync(["app", "favourite", "com.example.cafe", "off"], context));
            Assert.Empty(new StateStore(package.Paths.State).GetDevice("FAKE123")!.FavouriteApps);
            Assert.Equal(0, await Commands.RunAsync(["app", "list", "notes"], context));
            Assert.Equal(0, await Commands.RunAsync(["app", "open", "com.example.one"], context));
            Assert.Equal(0, await Commands.RunAsync(["app", "close", "com.example.one"], context));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, null);
        }
    }

    [Fact]
    public async Task Keys_AreListedSetAndPutBack()
    {
        using var package = new TestPackage(withFakeTools: true);
        var context = new CliContext(package.Paths);
        JsonNode Data(MachineResult r) => JsonNode.Parse(r.Json)!["data"]!;
        JsonNode Key(JsonNode data, string list, string action) => data[list]!.AsArray().Single(k => k!["action"]!.GetValue<string>() == action)!;

        var listed = Data(await MachineMode.RunAsync(["keys"], context));
        Assert.Equal("Ctrl+Alt+H", Key(listed, "window", "home")["key"]!.GetValue<string>());
        Assert.Null(Key(listed, "window", "power")["key"]);

        var moved = Data(await MachineMode.RunAsync(["keys", "set", "home", "Ctrl+Alt+J"], context));
        Assert.Equal("Ctrl+Alt+J", Key(moved, "window", "home")["key"]!.GetValue<string>());
        Assert.Equal("Ctrl+Alt+H", Key(moved, "window", "home")["shipped"]!.GetValue<string>());
        Assert.Equal(0, await Commands.RunAsync(["keys", "set", "power", "Ctrl+Alt+Q"], context));
        Assert.Equal(0, await Commands.RunAsync(["keys", "set", "like", "K", "--browse"], context));
        Assert.Equal(0, await Commands.RunAsync(["keys", "set", "recents", "none"], context));
        var config = ConfigFile.Load(package.Paths.Config);
        Assert.Equal([("home", "Ctrl+Alt+J"), ("power", "Ctrl+Alt+Q"), ("recents", "")], config.Keys.Window.Select(b => (b.Action, b.Key)));
        Assert.Equal([("like", "K")], config.Keys.Browse.Select(b => (b.Action, b.Key)));
        Assert.Equal(0, await Commands.RunAsync(["keys"], context));

        // A key another action has is refused in words, and nothing changes.
        var refused = await MachineMode.RunAsync(["keys", "set", "back", "Ctrl+Alt+J"], context);
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("This key already does: Home.", JsonNode.Parse(refused.Json)!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => Commands.RunAsync(["keys", "swap"], context));

        Assert.Equal(0, await Commands.RunAsync(["keys", "reset", "home"], context));
        Assert.DoesNotContain(ConfigFile.Load(package.Paths.Config).Keys.Window, b => b.Action == "home");
        Assert.Equal(0, await Commands.RunAsync(["keys", "reset", "--browse"], context));
        Assert.Empty(ConfigFile.Load(package.Paths.Config).Keys.Browse);
        Assert.Equal(0, await Commands.RunAsync(["keys", "reset"], context));
        Assert.Empty(ConfigFile.Load(package.Paths.Config).Keys.Window);
    }

    [Fact]
    public async Task Encoders_AreListedWithoutTheDesktopApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, "rex-tests-nobody-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new CliContext(package.Paths);
            var listed = JsonNode.Parse((await MachineMode.RunAsync(["encoders"], context)).Json)!["data"]!;
            Assert.Equal("FAKE123", listed["serial"]!.GetValue<string>());
            Assert.Equal("h264", listed["codec"]!.GetValue<string>());
            Assert.Equal(string.Empty, listed["chosen"]!.GetValue<string>());
            Assert.Equal(
                ["c2.exynos.h264.encoder", "c2.android.avc.encoder", "c2.exynos.hevc.encoder", "c2.android.hevc.encoder", "OMX.google.h264.encoder"],
                listed["encoders"]!.AsArray().Select(e => e!["name"]!.GetValue<string>()));
            Assert.Contains(package.ScrcpyLog(), line => line.StartsWith("list-encoders --serial=FAKE123 --list-encoders --no-cleanup", StringComparison.Ordinal));

            // One chosen with rex config set is marked as chosen in the human list.
            Assert.Equal(0, await Commands.RunAsync(["config", "set", "Mirror.VideoEncoder", "c2.exynos.h264.encoder"], context));
            Assert.Equal(0, await Commands.RunAsync(["encoders"], context));
            await Assert.ThrowsAsync<FormatException>(() => Commands.RunAsync(["config", "set", "Mirror.VideoEncoder", "not one; reboot"], context));
            Assert.Equal("c2.exynos.h264.encoder", ConfigFile.Load(package.Paths.Config).Mirror.VideoEncoder);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, null);
        }
    }

    [Fact]
    public async Task App_RefusesWhatIsNotAnApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, "rex-tests-nobody-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new CliContext(package.Paths);
            var refused = await MachineMode.RunAsync(["app", "open", "x;", "reboot"], context);
            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("No app on the phone is called", JsonNode.Parse(refused.Json)!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains("More than one app", JsonNode.Parse((await MachineMode.RunAsync(["app", "open", "Notes"], context)).Json)!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Equal(1, (await MachineMode.RunAsync(["app", "close", "not-a-package"], context)).ExitCode);
            Assert.Equal(1, (await MachineMode.RunAsync(["app", "favourite", "nope", "on"], context)).ExitCode);
            Assert.Equal(1, (await MachineMode.RunAsync(["app", "favourite", "com.example.one", "maybe"], context)).ExitCode);
            Assert.Equal(1, (await MachineMode.RunAsync(["app", "dance"], context)).ExitCode);
            Assert.Equal(1, await Commands.RunAsync(["app", "open", "Notes"], context));
            Assert.Equal(1, await Commands.RunAsync(["app", "info", "not-a-package"], context));
            Assert.DoesNotContain(package.AdbCalls(), c => c.Contains("am start", StringComparison.Ordinal) || c.Contains("reboot", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, null);
        }
    }

    [Fact]
    public async Task Files_PushAndInstallWorkWithoutTheDesktopApp()
    {
        using var package = new TestPackage(withFakeTools: true);
        var photo = Path.Combine(package.Root, "photo one.jpg");
        var apk = Path.Combine(package.Root, "example.apk");
        File.WriteAllText(photo, "picture");
        File.WriteAllText(apk, "fake apk");
        var context = new CliContext(package.Paths);

        var pushed = await MachineMode.RunAsync(["push", photo, "--to", "/sdcard/Pictures/"], context);
        Assert.Equal(0, pushed.ExitCode);
        var push = JsonNode.Parse(pushed.Json)!;
        Assert.Equal("/sdcard/Pictures/", push["data"]!["items"]![0]!["to"]!.GetValue<string>());
        Assert.Contains(package.AdbCalls(), call => call.Contains("push", StringComparison.Ordinal) && call.Contains("photo one.jpg", StringComparison.Ordinal));

        var installed = await MachineMode.RunAsync(["install", apk, "--downgrade", "--grant", "--test", "--no-replace"], context);
        Assert.Equal(0, installed.ExitCode);
        Assert.Contains(package.AdbCalls(), call => call.Contains("install -d -g -t", StringComparison.Ordinal) && call.Contains("example.apk", StringComparison.Ordinal));

        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.Equal(0, await Commands.RunAsync(["push", photo], context));
            Assert.Equal(0, await Commands.RunAsync(["install", apk], context));
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("Sent photo one.jpg", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Installed example.apk", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_ReportInvalidTargetsAndAdbFailures()
    {
        using var package = new TestPackage(withFakeTools: true);
        var file = Path.Combine(package.Root, "large.bin");
        File.WriteAllText(file, "contents");
        var context = new CliContext(package.Paths);

        var unsafeTarget = await MachineMode.RunAsync(["push", file, "--to", "/data/local/tmp/"], context);
        Assert.Equal(1, unsafeTarget.ExitCode);
        Assert.Contains("phone's storage", JsonNode.Parse(unsafeTarget.Json)!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        File.WriteAllText(package.FailPushMarker, string.Empty);
        var failed = await MachineMode.RunAsync(["push", file], context);
        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("One or more files failed", failed.Json, StringComparison.Ordinal);

        var notApk = await MachineMode.RunAsync(["install", file], context);
        Assert.Equal(1, notApk.ExitCode);
        Assert.Contains("APK file is required", notApk.Json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigSet_RefusesAChordWithoutCtrl()
    {
        using var package = new TestPackage();
        var context = new CliContext(package.Paths);

        var result = await MachineMode.RunAsync(["config", "set", "GlobalKeys.ShowHide", "Alt+Q"], context);

        Assert.Equal(1, result.ExitCode);
        var doc = JsonNode.Parse(result.Json)!.AsObject();
        Assert.Equal("FormatException", doc["error"]!["type"]!.GetValue<string>());
        Assert.Contains("needs Ctrl", doc["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(GlobalKeysSettings.DefaultShowHide, context.Config.Get("GlobalKeys.ShowHide").Value);
    }

    [Fact]
    public async Task Usb_ListsWindowsAdbInterfacesReadOnly()
    {
        // Listing reads the real PC and changes nothing; everything that repairs is in UsbTests,
        // against a stand-in PC, because CI's runners are elevated and would really run it.
        using var package = new TestPackage();
        var context = new CliContext(package.Paths);

        var list = JsonNode.Parse((await MachineMode.RunAsync(["usb"], context)).Json)!.AsObject();
        Assert.True(list["ok"]!.GetValue<bool>());
        foreach (var entry in list["data"]!.AsArray())
        {
            Assert.StartsWith(@"USB\", entry!["instanceId"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Equal(entry["present"]!.GetValue<bool>() && !entry["registered"]!.GetValue<bool>(), entry["unreachable"]!.GetValue<bool>());
        }
    }

    [Fact]
    public async Task UnknownCommand_FailsWithOneJsonDocument()
    {
        using var package = new TestPackage();
        var result = await MachineMode.RunAsync(["frobnicate"], new CliContext(package.Paths));

        Assert.Equal(1, result.ExitCode);
        var doc = JsonNode.Parse(result.Json)!.AsObject();
        Assert.False(doc["ok"]!.GetValue<bool>());
        Assert.Equal("ArgumentException", doc["error"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task HumanCommands_PrintAndExitCleanly()
    {
        using var package = new TestPackage(withFakeTools: true);
        var context = new CliContext(package.Paths);
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            Assert.Equal(0, await Commands.RunAsync(["help"], context));
            Assert.Equal(0, await Commands.RunAsync(["devices"], context));
            Assert.Equal(0, await Commands.RunAsync(["action", "list"], context));
            Assert.Equal(0, await Commands.RunAsync(["lock-mode", "FAKE123", "pattern"], context));
            await Assert.ThrowsAsync<ArgumentException>(() => Commands.RunAsync(["nope"], context));
        }
        finally
        {
            Console.SetOut(original);
        }

        var text = output.ToString();
        Assert.Contains("rex open", text);
        Assert.Contains("FAKE123", text);
        Assert.Contains("sleep", text);
        // The action list says what each action does, not the key code it sends.
        Assert.Contains("volume-up        Volume up          Raise the volume", text);
        Assert.DoesNotContain("KEYCODE_", text);
        Assert.Equal(LockScreenModes.Pattern, new StateStore(package.Paths.State).GetDevice("FAKE123")!.LockScreenMode);
    }

    [Fact]
    public async Task Profile_SaveApplyShareAndDeleteWithoutTheApp()
    {
        using var package = new TestPackage();
        Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, "rex-tests-nobody-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new CliContext(package.Paths);
            async Task<JsonObject> Run(params string[] args)
            {
                var result = await MachineMode.RunAsync(["profile", .. args], context);
                return JsonNode.Parse(result.Json)!.AsObject();
            }

            package.EditConfig(c => c.Mirror.MaxFps = 30);
            var saved = await Run("save", "Mine");
            Assert.True(saved["ok"]!.GetValue<bool>(), saved.ToJsonString());
            Assert.Equal(30, saved["data"]!["settings"]!["Mirror.MaxFps"]!.GetValue<int>());

            package.EditConfig(c => c.Mirror.MaxFps = 60);
            Assert.True((await Run("apply", "mine"))["ok"]!.GetValue<bool>());
            Assert.Equal(30, ConfigFile.Load(package.Paths.Config).Mirror.MaxFps);
            Assert.Equal("Mine", new StateStore(package.Paths.State).Ui.Profile);

            var listed = await Run("list");
            Assert.Equal("Mine", listed["data"]!["current"]!.GetValue<string>());
            Assert.True((await Run("show", "Gaming"))["data"]!["preset"]!.GetValue<bool>());

            Assert.True((await Run("rename", "Mine", "Ours"))["ok"]!.GetValue<bool>());
            var file = Path.Combine(package.Root, "ours.json");
            Assert.True((await Run("export", "Ours", file))["ok"]!.GetValue<bool>());
            Assert.Equal("Ours (2)", (await Run("import", file))["data"]!["name"]!.GetValue<string>());
            Assert.True((await Run("delete", "Ours"))["ok"]!.GetValue<bool>());

            var missing = await Run("apply", "Nothing");
            Assert.False(missing["ok"]!.GetValue<bool>());
            Assert.Equal("KeyNotFoundException", missing["error"]!["type"]!.GetValue<string>());
            var usage = await Run("rename", "Ours (2)");
            Assert.Equal("ArgumentException", usage["error"]!["type"]!.GetValue<string>());
            Assert.Contains(ProfileRequest.Usage, usage["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

            // The human form says the same in words, and a relative file is taken from where rex runs.
            using var output = new StringWriter();
            var original = Console.Out;
            Console.SetOut(output);
            try
            {
                Assert.Equal(0, await Commands.RunAsync(["profile", "apply", "Quiet"], context));
            }
            finally
            {
                Console.SetOut(original);
            }

            Assert.Contains("Quiet applied", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath("x.json"), ProfileCommands.Request(["profile", "export", "Ours (2)", "x.json"]).File);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Ipc.PipeNameOverride, null);
        }
    }
}
