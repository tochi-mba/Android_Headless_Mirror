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
    public async Task Usb_ListsWindowsAdbInterfacesAndRepairNeedsElevation()
    {
        using var package = new TestPackage();
        var context = new CliContext(package.Paths);

        var list = JsonNode.Parse((await MachineMode.RunAsync(["usb"], context)).Json)!.AsObject();
        Assert.True(list["ok"]!.GetValue<bool>());
        foreach (var entry in list["data"]!.AsArray())
        {
            Assert.StartsWith(@"USB\", entry!["instanceId"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Equal(entry["present"]!.GetValue<bool>() && !entry["registered"]!.GetValue<bool>(), entry["unreachable"]!.GetValue<bool>());
        }

        var repair = await MachineMode.RunAsync(["usb", "repair"], context);
        var doc = JsonNode.Parse(repair.Json)!.AsObject();
        if (UsbAdbInterfaces.IsElevated)
        {
            Assert.True(doc["ok"]!.GetValue<bool>(), repair.Json);
        }
        else
        {
            Assert.Equal(1, repair.ExitCode);
            Assert.Contains("administrator", doc["error"]!["message"]!.GetValue<string>());
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
        Assert.Equal(LockScreenModes.Pattern, new StateStore(package.Paths.State).GetDevice("FAKE123")!.LockScreenMode);
    }
}
