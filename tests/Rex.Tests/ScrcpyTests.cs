using System.Text.Json;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

public sealed class ScrcpyTests
{
    [Fact]
    public void Build_ProducesEmbeddedSessionArguments()
    {
        var config = new RexConfig();
        config.Mirror.ExtraArgs = "--render-fit=letterbox \"--background-color=#123456\"";

        var args = ScrcpyArguments.Build(config, "USB123", isTcp: false, "Android Headless Mirror [USB123]", (10, 20, 300, 600), null);

        foreach (var expected in new[]
        {
            "--serial=USB123", "--window-title=Android Headless Mirror [USB123]", "--window-borderless",
            "--no-window-aspect-ratio-lock", "--mouse=sdk", "--keyboard=sdk", "--shortcut-mod=rctrl+ralt",
            "--window-x=10", "--window-y=20", "--window-width=300", "--window-height=600",
            "--turn-screen-off", "--stay-awake", "--keep-active", "--max-size=1920", "--max-fps=60",
            "--video-bit-rate=12M", "--video-codec=h264", "--audio-codec=opus", "--audio-buffer=50",
            "--render-fit=letterbox", "--background-color=#123456",
        })
        {
            Assert.Contains(expected, args);
        }

        Assert.DoesNotContain("--power-off-on-close", args);
        Assert.DoesNotContain("--audio-dup", args);
        Assert.DoesNotContain("--record=", args);
    }

    [Fact]
    public void Build_HonoursSessionAndAudioSwitches()
    {
        var config = new RexConfig();
        config.Session.TurnScreenOff = false;
        config.Session.KeepActive = false;
        config.Session.PowerOffOnClose = true;
        config.Mirror.Audio = false;
        config.Mirror.MaxSize = 0;
        config.Mirror.MaxFps = 0;

        var tcp = ScrcpyArguments.Build(config, "10.0.0.2:5555", isTcp: true, "T", null, "C:\\rec\\a.mp4");

        Assert.DoesNotContain("--turn-screen-off", tcp);
        Assert.DoesNotContain("--stay-awake", tcp);
        Assert.DoesNotContain("--keep-active", tcp);
        Assert.Contains("--power-off-on-close", tcp);
        Assert.Contains("--no-audio", tcp);
        Assert.Contains("--record=C:\\rec\\a.mp4", tcp);
        Assert.DoesNotContain(tcp, a => a.StartsWith("--max-size", StringComparison.Ordinal));
        Assert.DoesNotContain(tcp, a => a.StartsWith("--max-fps", StringComparison.Ordinal));
        Assert.DoesNotContain(tcp, a => a.StartsWith("--window-x", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--serial=OTHER")]
    [InlineData("--window-title=Hijacked")]
    [InlineData("--mouse=uhid")]
    [InlineData("--shortcut-mod=lctrl")]
    [InlineData("--no-control")]
    [InlineData("--no-window")]
    [InlineData("--otg")]
    [InlineData("-S")]
    [InlineData("--fullscreen")]
    public void SplitExtraArgs_RejectsManagedOptions(string forbidden) =>
        Assert.Throws<FormatException>(() => ScrcpyArguments.SplitExtraArgs(forbidden));

    [Fact]
    public void SplitExtraArgs_SplitsLikeAShell()
    {
        Assert.Equal(["--a=1", "--b=two words", "c\"d"], ScrcpyArguments.SplitExtraArgs("--a=1 '--b=two words' \"c\\\"d\""));
        Assert.Empty(ScrcpyArguments.SplitExtraArgs("   "));
        Assert.Throws<FormatException>(() => ScrcpyArguments.SplitExtraArgs("\"unterminated"));
        Assert.Throws<FormatException>(() => ScrcpyArguments.SplitExtraArgs("a\nb"));
    }

    [Theory]
    [InlineData("12M", true)]
    [InlineData("8000K", true)]
    [InlineData("8000000", true)]
    [InlineData("12 M", false)]
    [InlineData("", false)]
    public void IsValidBitRate(string value, bool expected) => Assert.Equal(expected, ScrcpyArguments.IsValidBitRate(value));

    [Fact]
    public void Installer_ParsesReleaseAndChecksums()
    {
        using var json = JsonDocument.Parse("""
            { "tag_name": "v4.1", "assets": [
                { "name": "scrcpy-linux-x86_64-v4.1.tar.gz", "browser_download_url": "https://x/linux" },
                { "name": "scrcpy-win64-v4.1.zip", "browser_download_url": "https://x/win64" },
                { "name": "SHA256SUMS.txt", "browser_download_url": "https://x/sums" } ] }
            """);

        var release = ScrcpyInstaller.ParseRelease(json.RootElement);

        Assert.Equal("v4.1", release.Tag);
        Assert.Equal("scrcpy-win64-v4.1.zip", release.AssetName);
        Assert.Equal("https://x/win64", release.AssetUrl);
        Assert.Equal("https://x/sums", release.ChecksumUrl);

        const string sums = "abc123  scrcpy-linux-x86_64-v4.1.tar.gz\nDEADBEEF *scrcpy-win64-v4.1.zip\n";
        Assert.Equal("deadbeef", ScrcpyInstaller.ParseChecksum(sums, "scrcpy-win64-v4.1.zip"));
        Assert.Null(ScrcpyInstaller.ParseChecksum(sums, "missing.zip"));
    }

    [Fact]
    public void Installer_FinalizesBesideLegacyPartialInstallWithoutDeletingIt()
    {
        using var package = new TestPackage();
        var source = Path.Combine(package.Root, "verified-source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "scrcpy.exe"), "new scrcpy");
        File.WriteAllText(Path.Combine(source, "adb.exe"), "new adb");

        var legacy = Path.Combine(package.Paths.ScrcpyTools, "v4.1");
        Directory.CreateDirectory(legacy);
        var legacyAdb = Path.Combine(legacy, "adb.exe");
        File.WriteAllText(legacyAdb, "locked legacy adb");

        // On Windows this reproduces the important property of a running adb.exe:
        // the old file cannot be deleted while the handle does not share delete access.
        using var lockHandle = new FileStream(legacyAdb, FileMode.Open, FileAccess.Read, FileShare.Read);

        var tools = ScrcpyInstaller.InstallVerifiedDirectory(
            package.Paths,
            source,
            "v4.1",
            new string('a', 64));

        Assert.True(tools.IsComplete);
        Assert.Equal("v4.1", tools.Version);
        var installedDirectory = Path.GetDirectoryName(tools.Adb)!;
        Assert.NotEqual(legacy, installedDirectory);
        Assert.EndsWith($"v4.1-{new string('a', 12)}", installedDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(legacyAdb));
    }

    [Fact]
    public void Installer_ReusesCompleteContentAddressedInstall()
    {
        using var package = new TestPackage();
        var source = Path.Combine(package.Root, "verified-source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "scrcpy.exe"), "scrcpy");
        File.WriteAllText(Path.Combine(source, "adb.exe"), "adb");
        var digest = new string('b', 64);

        var first = ScrcpyInstaller.InstallVerifiedDirectory(package.Paths, source, "v4.1", digest);
        var second = ScrcpyInstaller.InstallVerifiedDirectory(package.Paths, source, "v4.1", digest);

        Assert.Equal(first.Scrcpy, second.Scrcpy);
        Assert.Equal(first.Adb, second.Adb);
        Assert.Equal("v4.1", second.Version);
        Assert.Single(Directory.GetDirectories(package.Paths.ScrcpyTools)
            .Where(path => !Path.GetFileName(path).StartsWith(".install-", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Installer_RefusesReleaseWithoutChecksums()
    {
        using var json = JsonDocument.Parse("""{ "tag_name": "v4.1", "assets": [ { "name": "scrcpy-win64-v4.1.zip", "browser_download_url": "u" } ] }""");
        var ex = Assert.Throws<InvalidOperationException>(() => ScrcpyInstaller.ParseRelease(json.RootElement));
        Assert.Contains("SHA256SUMS", ex.Message);
    }

    [Fact]
    public void Actions_HaveShortcutsOrAdbCommands()
    {
        foreach (var action in MirrorActions.All)
        {
            switch (action.Kind)
            {
                case ActionKind.Adb:
                    Assert.NotNull(MirrorActions.AdbCommand(action.Id));
                    break;
                case ActionKind.Scrcpy:
                    Assert.NotNull(ScrcpyShortcuts.For(action.Id));
                    break;
                case ActionKind.App:
                    Assert.Null(MirrorActions.AdbCommand(action.Id));
                    break;
            }
        }

        Assert.Equal(MirrorActions.All.Count, MirrorActions.Ids.Distinct().Count());
        Assert.NotNull(MirrorActions.Find("SLEEP"));
        Assert.Null(MirrorActions.Find("nope"));
    }
}
