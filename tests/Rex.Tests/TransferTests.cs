using System.IO.Compression;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Planning, naming, progress and metadata for files sent to the phone.</summary>
public sealed class TransferTests
{
    [Theory]
    [InlineData("note.txt", false, true, false, "Push", "/sdcard/Download/")]
    [InlineData("APP.APK", false, true, false, "Install", "")]
    [InlineData("app.apk", false, false, false, "Push", "/sdcard/Download/")]
    [InlineData("holiday", true, true, false, "Push", "/sdcard/Download/")]
    [InlineData("holiday", true, true, true, "Refuse", "")]
    [InlineData("photo.JPG", false, true, false, "Push", "/sdcard/Pictures/")]
    [InlineData("clip.mp4", false, true, false, "Push", "/sdcard/Movies/")]
    [InlineData("song.FLAC", false, true, false, "Push", "/sdcard/Music/")]
    public void EachDroppedThingIsPlanned(string name, bool folder, bool install, bool refuseFolders, string kind, string target)
    {
        var settings = new TransferSettings { InstallApks = install, Folders = refuseFolders ? "refuse" : "send", SortMedia = name.Contains('.') && !name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) };
        var item = Assert.Single(TransferPlan.Plan([new LocalEntry("C:\\" + name, name, folder, 12, folder ? 3 : 1)], settings));
        Assert.Equal(kind, item.Kind.ToString());
        Assert.Equal(target, item.Target);
        Assert.Equal(folder && refuseFolders ? TransferPlan.FolderRefused : null, item.Why);
    }

    [Fact]
    public void ALocalEntryIsReadFromDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "rex-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "one.txt");
            File.WriteAllText(file, "hello");
            Directory.CreateDirectory(Path.Combine(root, "inside"));
            File.WriteAllBytes(Path.Combine(root, "inside", "two.bin"), [1, 2, 3]);
            Assert.Equal(new LocalEntry(file, "one.txt", false, 5), LocalEntry.Read(file));
            Assert.Equal((true, 2, 8L), (LocalEntry.Read(root)!.IsFolder, LocalEntry.Read(root)!.Files, LocalEntry.Read(root)!.Size));
            Assert.Null(LocalEntry.Read(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("photo.jpg", "photo (2).jpg")]
    [InlineData("photo (2).jpg", "photo (4).jpg")]
    [InlineData("README", "README (2)")]
    [InlineData(".gitignore", ".gitignore (2)")]
    [InlineData("archive.tar.gz", "archive.tar (2).gz")]
    public void KeepingBothNamesTheCopy(string name, string expected)
    {
        HashSet<string> taken = [name, "photo (3).jpg"];
        Assert.Equal(expected, NameClash.KeepBoth(name, taken));
        Assert.Equal("free.txt", NameClash.KeepBoth("free.txt", taken));
    }

    [Theory]
    [InlineData("ask", true, ClashAnswer.Ask)]
    [InlineData("rename", true, ClashAnswer.KeepBoth)]
    [InlineData("replace", true, ClashAnswer.Replace)]
    [InlineData("skip", true, ClashAnswer.Skip)]
    [InlineData("ask", false, ClashAnswer.Send)]
    public void WhatHappensWhenANameExists(string policy, bool exists, ClashAnswer answer) =>
        Assert.Equal(answer, NameClash.Decide(policy, exists));

    [Fact]
    public void ProgressSpeedAndTimeLeft()
    {
        var samples = new[] { (TimeSpan.Zero, 0L), (TimeSpan.FromSeconds(1), 100L), (TimeSpan.FromSeconds(3), 500L) };
        Assert.Equal(200, TransferProgress.Speed(samples));
        Assert.Equal(TimeSpan.FromSeconds(2.5), TransferProgress.Left(500, 1000, 200));
        Assert.Equal("50% · 200 B/s · about 3 s left", TransferProgress.Sending(500, 1000, 200));
        Assert.Equal(0, TransferProgress.Speed([]));
        Assert.Equal(0, TransferProgress.Speed([(TimeSpan.Zero, 1L), (TimeSpan.Zero, 2L)]));
        Assert.Null(TransferProgress.Left(0, 100, 0));
        Assert.Equal(100, TransferProgress.Percent(2, 1));
        Assert.Equal(0, TransferProgress.Percent(0, 0));
        Assert.Equal("1.0 GB", TransferProgress.Size(1024L * 1024 * 1024));
        Assert.Equal("2 min", TransferProgress.Duration(TimeSpan.FromSeconds(61)));
        Assert.Equal("1.5 h", TransferProgress.Duration(TimeSpan.FromMinutes(90)));
        Assert.Equal("Failed: there is no space left on the phone", TransferProgress.Words(TransferState.Failed, "", TransferProgress.Why("adb: No space left on device")));
        Assert.Contains("newer version", TransferProgress.Why("INSTALL_FAILED_VERSION_DOWNGRADE"), StringComparison.Ordinal);
        Assert.Contains("test build", TransferProgress.Why("INSTALL_FAILED_TEST_ONLY"), StringComparison.Ordinal);
        Assert.Contains("already installed", TransferProgress.Why("INSTALL_FAILED_ALREADY_EXISTS"), StringComparison.Ordinal);
        Assert.Contains("did not allow", TransferProgress.Why("Permission denied"), StringComparison.Ordinal);
        Assert.Equal("last", TransferProgress.Why("first\nlast"));
    }

    [Fact]
    public void TheQueueRunsAsManyAsAskedAndKeepsHistory()
    {
        var queue = new TransferQueue(2, 2);
        var jobs = queue.Add(Enumerable.Range(1, 4).Select(i => new TransferItem(new LocalEntry(i + ".txt", i + ".txt", false, 100), TransferKind.Push, "/sdcard/Download/")));
        Assert.Equal(2, queue.Take().Count);
        Assert.Empty(queue.Take());
        Assert.True(queue.Progress(jobs[0].Id, 45));
        Assert.Equal(45, jobs[0].Sent);
        Thread.Sleep(10);
        Assert.True(queue.Progress(jobs[0].Id, 55));
        Assert.True(jobs[0].BytesPerSecond > 0);
        Assert.NotNull(jobs[0].TimeLeft);
        Assert.True(queue.Finish(jobs[0].Id, true));
        Assert.Single(queue.Take());
        Assert.True(queue.Finish(jobs[1].Id, false, "no room"));
        Assert.True(queue.Retry(jobs[1].Id));
        Assert.True(queue.Cancel(jobs[1].Id));
        Assert.True(queue.Skip(jobs[2].Id, "already there"));
        Assert.Equal(2, queue.Jobs.Count(job => TransferProgress.IsFinished(job.State)));
        queue.Configure(3, 0);
        Assert.DoesNotContain(queue.Jobs, job => TransferProgress.IsFinished(job.State));
        queue.ClearFinished();
        Assert.False(queue.Progress(Guid.NewGuid(), 2));
        Assert.False(queue.Finish(Guid.NewGuid(), true));
        Assert.False(queue.Retry(jobs[0].Id));
    }

    [Theory]
    [InlineData("/sdcard/Download", true)]
    [InlineData(" /storage/emulated/0/Documents/ ", true)]
    [InlineData("/data/local/tmp", false)]
    [InlineData("../sdcard/x", false)]
    [InlineData("/sdcard/../data", false)]
    [InlineData("C:\\Users\\x", false)]
    [InlineData("/sdcard/a'b", false)]
    [InlineData("/sdcard/a$b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void APhoneFolderIsChecked(string? folder, bool valid) => Assert.Equal(valid, TransferSettings.IsValidFolder(folder));

    [Fact]
    public void TransferSettingsAreKeptSane()
    {
        var settings = new TransferSettings { Folder = "/sdcard/Documents", Folders = "SEND", WhenNameExists = "wat", ConfirmOverMb = 1, AtOnce = 99, History = -2 };
        settings.Normalize();
        Assert.Equal("/sdcard/Documents/", settings.Folder);
        Assert.Equal("send", settings.Folders);
        Assert.Equal("rename", settings.WhenNameExists);
        Assert.Equal(10, settings.ConfirmOverMb);
        Assert.Equal(TransferSettings.MostAtOnce, settings.AtOnce);
        Assert.Equal(0, settings.History);

        var invalid = new TransferSettings { Folder = "/data", ConfirmOverMb = -1, AtOnce = 0, History = 999 };
        invalid.Normalize();
        Assert.Equal(TransferSettings.DefaultFolder, invalid.Folder);
        Assert.Equal(0, invalid.ConfirmOverMb);
        Assert.Equal(1, invalid.AtOnce);
        Assert.Equal(TransferSettings.MostHistory, invalid.History);
        Assert.NotNull(TransferSettings.WhyNotFolder("/data"));
        Assert.Null(TransferSettings.WhyNotFolder("/sdcard/x"));
        Assert.Equal("/storage/emulated/0/", TransferSettings.RootOf("/storage/emulated/0/Pictures/"));
    }

    [Fact]
    public void InstallFlagsBecomeArguments()
    {
        Assert.Equal(["-s", "S", "install", "-r", "app.apk"], AdbClient.InstallArguments("S", "app.apk", new InstallFlags()));
        Assert.Equal(["-s", "S", "install", "-d", "-g", "-t", "app.apk"],
            AdbClient.InstallArguments("S", "app.apk", new InstallFlags(false, true, true, true)));
    }

    [Theory]
    [InlineData("INFO: Request to install C:\\A B\\app.apk", "Requested", true, "C:\\A B\\app.apk", null)]
    [InlineData("INFO: Installing C:\\A B\\app.apk...", "Started", true, "C:\\A B\\app.apk", null)]
    [InlineData("INFO: C:\\A B\\app.apk successfully installed", "Succeeded", true, "C:\\A B\\app.apk", null)]
    [InlineData("ERROR: Failed to install C:\\A B\\app.apk", "Failed", true, "C:\\A B\\app.apk", null)]
    [InlineData("[client] INFO: Request to push C:\\A B\\photo.jpg", "Requested", false, "C:\\A B\\photo.jpg", null)]
    [InlineData("INFO: Pushing C:\\A B\\photo.jpg...", "Started", false, "C:\\A B\\photo.jpg", null)]
    [InlineData("INFO: C:\\A B\\photo.jpg successfully pushed to /sdcard/Download/photo.jpg", "Succeeded", false, "C:\\A B\\photo.jpg", "/sdcard/Download/photo.jpg")]
    [InlineData("ERROR: Failed to push C:\\A B\\photo.jpg to /sdcard/Download/photo.jpg", "Failed", false, "C:\\A B\\photo.jpg", "/sdcard/Download/photo.jpg")]
    public void ScrcpysOwnDropLinesAreRead(string line, string phase, bool install, string path, string? target)
    {
        var parsed = Assert.IsType<ScrcpyArguments.FileTransferLine>(ScrcpyArguments.ParseFileTransfer(line));
        Assert.Equal(phase, parsed.Phase.ToString());
        Assert.Equal((install, path, target), (parsed.Install, parsed.Path, parsed.Target));
    }

    [Fact]
    public void UnrelatedScrcpyLinesAreNotTransfers()
    {
        Assert.Null(ScrcpyArguments.ParseFileTransfer(null));
        Assert.Null(ScrcpyArguments.ParseFileTransfer("INFO: Texture: 1080x2400"));
        Assert.Null(ScrcpyArguments.ParseFileTransfer("INFO: Pushing ..."));
        Assert.Null(ScrcpyArguments.ParseFileTransfer("Failed to push x"));
    }

    [Fact]
    public void ThePushTargetAndDropSwitchReachTheMainSession()
    {
        var config = new RexConfig();
        var before = ScrcpyArguments.LaunchSettings(config, false);
        config.Transfer.Folder = "/sdcard/Documents/";
        // A new folder never asks for a restart: the window's own drops use it at once.
        Assert.Equal(before, ScrcpyArguments.LaunchSettings(config, false));
        Assert.Contains("--push-target=/sdcard/Documents/", ScrcpyArguments.Build(config, "S", false, "T", null, null));
        Assert.DoesNotContain(ScrcpyArguments.Build(config, "S", false, "copy", null, null, copyIndex: 0), a => a.StartsWith(ScrcpyArguments.PushTarget, StringComparison.Ordinal));
        config.Transfer.Enabled = false;
        Assert.Contains(ScrcpyArguments.NoFileDrop, ScrcpyArguments.Build(config, "S", false, "T", null, null));
        Assert.NotEqual(before, ScrcpyArguments.LaunchSettings(config, false));
        Assert.DoesNotContain(ScrcpyArguments.NoFileDrop, ScrcpyArguments.Build(config, "S", false, "copy", null, null, copyIndex: 0));
        config.Transfer.Enabled = true;
        config.Mirror.ExtraArgs = "--push-target=/sdcard/Old/";
        var main = ScrcpyArguments.Build(config, "S", false, "T", null, null);
        Assert.Single(main, argument => argument.StartsWith(ScrcpyArguments.PushTarget, StringComparison.Ordinal));
        Assert.Contains("--push-target=/sdcard/Documents/", main);
    }

    [Fact]
    public void ConfigSetRefusesAFolderOutsideThePhonesStorage()
    {
        ConfigValidation.Check("Transfer.Folder", "/sdcard/Documents", new RexConfig());
        var error = Assert.Throws<FormatException>(() => ConfigValidation.Check("Transfer.Folder", "/data/local/tmp", new RexConfig()));
        Assert.Contains("phone's storage", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdbCopiesInstallsAndLooksAfterFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new FakeProcessRunner
        {
            Respond = args => args.Contains("stat -c %s '/sdcard/Download/a b.txt'")
                ? new ProcessResult(0, "42\n", string.Empty)
                : args.Contains("ls -1 /sdcard/Download/")
                    ? new ProcessResult(0, "one.txt\ntwo.txt\n", string.Empty)
                    : new ProcessResult(0, "Success\n", string.Empty),
        };
        var adb = new AdbClient("adb.exe", runner);
        Assert.True((await adb.PushAsync("S", "C:\\a b.txt", "/sdcard/Download/", 1, ct)).Ok);
        Assert.False((await adb.PushAsync("S", "a", "/data/", 1, ct)).Ok);
        Assert.True((await adb.InstallAsync("S", "app.apk", new InstallFlags(), ct)).Ok);
        Assert.Equal(42, await adb.RemoteSizeAsync("S", "/sdcard/Download/a b.txt", ct));
        Assert.Equal(["one.txt", "two.txt"], (await adb.ListNamesAsync("S", "/sdcard/Download/", ct)).Order().ToArray());
        Assert.Empty(await adb.ListNamesAsync("S", "/data/", ct));
        Assert.True((await adb.DeleteFileAsync("S", "/sdcard/Download/a b.txt", ct)).Ok);
        Assert.True((await adb.DeleteEntryAsync("S", "/sdcard/Download/folder", recursive: true, ct)).Ok);
        Assert.True((await adb.ScanMediaAsync("S", "/sdcard/Pictures/a.jpg", ct)).Ok);
        // The file name is the person's own, so it reaches the phone's shell quoted as one argument.
        Assert.True((await adb.ScanMediaAsync("S", "/sdcard/Pictures/it's $(reboot).jpg", ct)).Ok);
        Assert.Contains(runner.Calls, call => call.Arguments.Contains(
            "am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d 'file:///sdcard/Pictures/it'\"'\"'s $(reboot).jpg'"));
        Assert.True((await adb.OpenFolderAsync("S", "/sdcard/Download/", ct)).Ok);
        Assert.False((await adb.OpenFolderAsync("S", "/data/", ct)).Ok);
        Assert.Equal("/sdcard/Download/a b.txt", AdbClient.RemotePath("/sdcard/Download/", "a b.txt"));
        Assert.Throws<ArgumentException>(() => AdbClient.RemotePath("/data/", "a"));
        Assert.Throws<ArgumentException>(() => AdbClient.RemotePath("/sdcard/", "a/b"));
        Assert.False((await adb.DeleteEntryAsync("S", "/data/local/tmp/nope", recursive: true, ct)).Ok);
        Assert.Contains(runner.Calls, call => call.Arguments.Contains("rm -rf -- /sdcard/Download/folder"));

        runner.Respond = _ => new ProcessResult(1, "", "no device");
        Assert.False((await adb.InstallAsync("S", "app.apk", new InstallFlags(), ct)).Ok);
        Assert.Null(await adb.RemoteSizeAsync("S", "/sdcard/Download/nope", ct));
    }

    [Fact]
    public void TheAppsPackageIsReadFromItsManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "rex-apk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var apk = Path.Combine(root, "app.apk");
            using (var zip = ZipFile.Open(apk, ZipArchiveMode.Create))
            {
                using var output = zip.CreateEntry("AndroidManifest.xml").Open();
                output.Write(TestApk.Manifest("com.example.app", "2.4"));
            }

            Assert.Equal(new ApkIdentity("com.example.app", "2.4"), ApkManifest.Read(apk));
            File.WriteAllText(Path.Combine(root, "not.zip"), "hello");
            Assert.Null(ApkManifest.Read(Path.Combine(root, "not.zip")));
            Assert.Null(ApkManifest.Read(Path.Combine(root, "missing.apk")));

            var empty = Path.Combine(root, "empty.apk");
            using (ZipFile.Open(empty, ZipArchiveMode.Create)) { }
            Assert.Null(ApkManifest.Read(empty));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
