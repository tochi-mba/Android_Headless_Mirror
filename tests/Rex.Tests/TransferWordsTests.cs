using System.Diagnostics;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// What a transfer says about itself: a drop described before it happens, each state, sizes and
/// times; a transfer scrcpy started; a folder link that leads nowhere; and APK manifests that are
/// written in UTF-16 or are not manifests at all.
/// </summary>
public sealed class TransferWordsTests
{
    private static LocalEntry File(string name, long size = 10) => new(name, name, false, size);

    private static TransferItem Push(LocalEntry entry, string folder = "/sdcard/Download/") => new(entry, TransferKind.Push, folder);

    [Fact]
    public void ADropSaysWhatItWillDo()
    {
        var phone = "Pixel";
        Assert.Equal("Drop to send a.txt to Pixel · Download", TransferPlan.Describe([Push(File("a.txt"))], phone));
        Assert.Equal("Drop to send 2 files to Pixel · Download", TransferPlan.Describe([Push(File("a.txt")), Push(File("b.txt"))], phone));
        Assert.Equal("Drop to send 2 files to Pixel", TransferPlan.Describe([Push(File("a.jpg"), "/sdcard/Pictures/"), Push(File("b.txt"))], phone));
        Assert.Equal("Drop to install app.apk on Pixel", TransferPlan.Describe([new TransferItem(File("app.apk"), TransferKind.Install, string.Empty)], phone));
        Assert.Equal("Drop to install 2 apps on Pixel", TransferPlan.Describe(
            [new TransferItem(File("a.apk"), TransferKind.Install, string.Empty), new TransferItem(File("b.apk"), TransferKind.Install, string.Empty)], phone));
        Assert.Equal("Drop to send this folder (1 file) to Pixel · Download", TransferPlan.Describe([Push(new LocalEntry("p", "Photos", true, 10, 1))], phone));
        Assert.Equal("Drop to send this folder (124 files) to Pixel · Download", TransferPlan.Describe([Push(new LocalEntry("p", "Photos", true, 10, 124))], phone));
        Assert.Equal(TransferPlan.FolderRefused, TransferPlan.Describe([new TransferItem(new LocalEntry("p", "Photos", true, 1), TransferKind.Refuse, string.Empty, TransferPlan.FolderRefused)], phone));
        Assert.Equal("Nothing to send.", TransferPlan.Describe([], phone));
        Assert.Equal("Download", TransferPlan.FolderName("/sdcard/Download/"));
        Assert.Equal("/", TransferPlan.FolderName("/"));
    }

    [Theory]
    [InlineData(TransferState.Waiting, null, "Waiting")]
    [InlineData(TransferState.Sending, null, "Sending")]
    [InlineData(TransferState.Installing, null, "Installing")]
    [InlineData(TransferState.Done, null, "In Download")]
    [InlineData(TransferState.Installed, null, "Installed")]
    [InlineData(TransferState.Skipped, null, "Skipped: already there")]
    [InlineData(TransferState.Skipped, "Folders are not sent", "Skipped: Folders are not sent")]
    [InlineData(TransferState.Failed, null, "Failed: it did not arrive")]
    [InlineData(TransferState.Failed, "the phone disconnected", "Failed: the phone disconnected")]
    [InlineData(TransferState.Cancelled, null, "Cancelled")]
    public void EachStateHasItsWords(TransferState state, string? why, string words) =>
        Assert.Equal(words, TransferProgress.Words(state, "/sdcard/Download/", why));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1.5 GB")]
    public void SizesReadAsPeopleSayThem(long bytes, string words) => Assert.Equal(words, TransferProgress.Size(bytes));

    [Fact]
    public void AQueueTakesTheStartOfATransferScrcpyBegan()
    {
        var queue = new TransferQueue(1, 5);
        var job = queue.Add([new TransferItem(File("a.apk", 200), TransferKind.Install, string.Empty)])[0];
        Assert.Equal(0, job.Percent);
        Assert.True(queue.Start(job.Id, installing: true));
        Assert.Equal(TransferState.Installing, job.State);
        Assert.False(queue.Start(job.Id, installing: true));
        Assert.False(queue.Start(Guid.NewGuid(), installing: false));
        Assert.True(queue.Finish(job.Id, true));
        Assert.Equal(TransferState.Installed, job.State);
        Assert.Equal(100, job.Percent);
        Assert.False(queue.Cancel(job.Id));
        Assert.False(queue.Skip(job.Id));
    }

    [Fact]
    public void AFolderLinkThatLeadsNowhereIsNotSent()
    {
        var root = Path.Combine(Path.GetTempPath(), "rex-link-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            using (var junction = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            {
                junction.WaitForExit();
                Assert.Equal(0, junction.ExitCode);
            }

            Directory.Delete(target);
            Assert.True(Directory.Exists(link));
            Assert.Null(LocalEntry.Read(link));
        }
        finally
        {
            // A junction is removed on its own; a recursive delete refuses one whose target has gone.
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AManifestInUtf16IsReadToo()
    {
        Assert.Equal(new ApkIdentity("com.example.wide", "9.0"), ApkManifest.Read(TestApk.Manifest("com.example.wide", "9.0", utf16: true)));

        // Long strings use the longer length forms: past 127 bytes in UTF-8, past 32767 characters in UTF-16.
        var longPackage = "com.example." + new string('a', 180);
        Assert.Equal(longPackage, ApkManifest.Read(TestApk.Manifest(longPackage, "1"))!.Package);
        var longVersion = new string('7', 40_000);
        Assert.Equal(longVersion, ApkManifest.Read(TestApk.Manifest("com.example.long", longVersion, utf16: true))!.VersionName);
    }

    [Fact]
    public void WhatIsNotAManifestGivesNothingAndNeverThrows()
    {
        Assert.Null(ApkManifest.Read([]));
        Assert.Null(ApkManifest.Read(new byte[8]));
        Assert.Null(ApkManifest.Read(TestApk.Manifest("not a package", "1")));
        Assert.Null(ApkManifest.Read(TestApk.Manifest("com.example.one", "1", element: "application")));

        // A chunk header shorter than any chunk's.
        Assert.Null(ApkManifest.Read(Broken(xml => TestApk.Put16(xml, TestApk.PoolAt + 2, 4))));
        // A string pool too small to hold its own header.
        Assert.Null(ApkManifest.Read(Broken(xml => { TestApk.Put16(xml, TestApk.PoolAt + 2, 8); TestApk.Put32(xml, TestApk.PoolAt + 4, 20); })));
        // A string pool that claims more strings than it has room for.
        Assert.Null(ApkManifest.Read(Broken(xml => TestApk.Put32(xml, TestApk.PoolAt + 8, 200_000))));
        // A string that runs off the end of the pool.
        Assert.Null(ApkManifest.Read(Broken(xml => TestApk.Put32(xml, TestApk.PoolAt + 28, 100_000))));
        // A UTF-8 string without its terminator.
        Assert.Null(ApkManifest.Read(Broken(xml => xml[TestApk.PoolAt + 28 + 5 * 4 + 2 + "manifest".Length] = (byte)'x')));
        // A UTF-16 string without its terminator.
        var wide = TestApk.Manifest("com.example.one", "1.0", utf16: true);
        TestApk.Put16(wide, TestApk.PoolAt + 28 + 5 * 4 + 2 + "manifest".Length * 2, 'x');
        Assert.Null(ApkManifest.Read(wide));
        // Attributes narrower than an attribute.
        Assert.Null(ApkManifest.Read(Broken(xml => TestApk.Put16(xml, TestApk.ElementAt(xml) + 26, 10))));
        // A file that says it is longer than it is.
        Assert.Null(ApkManifest.Read(Broken(xml => TestApk.Put32(xml, 4, xml.Length + 1))));
    }

    private static byte[] Broken(Action<byte[]> breakIt)
    {
        var xml = TestApk.Manifest("com.example.one", "1.0");
        breakIt(xml);
        return xml;
    }
}
