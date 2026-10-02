using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>
/// Sending files to the phone across processes: a drag and drop with progress, apps installed with
/// the chosen flags, the Gallery told, names kept apart, File Explorer's Send to, copied files on
/// a key, a phone that leaves mid-way, and files handed over by a second launch.
/// </summary>
public sealed partial class AppEndToEndTests
{
    private static readonly TimeSpan FileTimeout = TimeSpan.FromSeconds(20);

    private static JsonNode FilesOf(JsonObject status) => status["files"]!;

    private static IpcRequest Paths(string command, params string[] paths) =>
        new(command, new Dictionary<string, string> { ["paths"] = JsonSerializer.Serialize(paths) });

    /// <summary>The usual fake phone, with pushes that take this long and files already on it.</summary>
    private static void WritePhone(TestPackage package, int pushMillis = 0, Dictionary<string, long>? remoteFiles = null, bool connected = true) =>
        package.WriteScenario(new
        {
            Devices = connected ? new[] { new { Serial = "FAKE123", State = "device", Product = "fake", Model = "Fake Phone" } } : [],
            Properties = new Dictionary<string, string>
            {
                ["ro.product.manufacturer"] = "Samsung",
                ["ro.product.model"] = "SM-G998B",
                ["ro.product.marketname"] = "Galaxy S21 Ultra",
                ["ro.build.version.release"] = "15",
                ["ro.build.version.sdk"] = "35",
            },
            PushMillis = pushMillis,
            RemoteFiles = remoteFiles ?? [],
        });

    private static string MakeFile(TestPackage package, string name, int bytes)
    {
        var path = Path.Combine(package.Root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static JsonNode? Item(JsonObject status, string name) =>
        FilesOf(status)["items"]!.AsArray().FirstOrDefault(i => i!["name"]!.GetValue<string>() == name);

    private static string? StateOf(JsonObject status, string name) => Item(status, name)?["state"]!.GetValue<string>();

    [Fact(Timeout = 180_000)]
    public async Task Files_ADroppedFileArrivesWithProgressAndWords()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        WritePhone(package, pushMillis: 4000);
        var file = MakeFile(package, "holiday video.mp4", 3 * 1024 * 1024);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        // Over the window, the drag says exactly what dropping will do, and arms the picture.
        var dragged = await app.SendAsync(Paths("drag", file));
        Assert.True(dragged.Ok, dragged.Error);
        Assert.Equal("Drop to send holiday video.mp4 to Galaxy S21 Ultra · Download", dragged.Data!["hint"]!.GetValue<string>());
        await app.WaitForStatusAsync(s => FilesOf(s)["armed"]!.GetValue<bool>(), FileTimeout, "the picture armed for the drop");

        Assert.Equal(1, (await app.SendAsync(Paths("drop", file))).Data!["queued"]!.GetValue<int>());
        var sending = await app.WaitForStatusAsync(
            s => Item(s, "holiday video.mp4") is { } item && item["state"]!.GetValue<string>() == "sending" && item["percent"]!.GetValue<int>() is > 0 and < 100,
            FileTimeout, "the file part of the way there");
        Assert.False(FilesOf(sending)["armed"]!.GetValue<bool>());
        await app.WaitForStatusAsync(s => StateOf(s, "holiday video.mp4") == "done", FileTimeout, "the file to arrive");

        Assert.Contains(package.AdbCalls(), c => c.Contains("push", StringComparison.Ordinal) && c.Contains("holiday video.mp4", StringComparison.Ordinal) && c.EndsWith("/sdcard/Download/", StringComparison.Ordinal));
        Assert.Contains(package.AdbCalls(), c => c.Contains("stat -c %s", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_AFailedPushSaysWhyInTheAppsWords()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        File.WriteAllText(package.FailPushMarker, string.Empty);
        var file = MakeFile(package, "notes.txt", 100);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        await app.SendAsync(Paths("push", file));
        var failed = await app.WaitForStatusAsync(s => StateOf(s, "notes.txt") == "failed", FileTimeout, "the push to fail");
        Assert.Equal("there is no space left on the phone", Item(failed, "notes.txt")!["error"]!.GetValue<string>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_AppsAreInstalledWithTheChosenFlagsAndOpened()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c =>
        {
            c.Transfer.ConfirmInstall = false;
            c.Transfer.AllowDowngrade = true;
            c.Transfer.GrantPermissions = true;
            c.Transfer.OpenAfterInstall = true;
        });
        var apk = Path.Combine(package.Root, "Example One.APK");
        TestApk.Write(apk, "com.example.one", "3.1");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        Assert.Equal(1, (await app.SendAsync(Paths("push", apk))).Data!["queued"]!.GetValue<int>());
        await app.WaitForStatusAsync(s => StateOf(s, "Example One.APK") == "installed", FileTimeout, "the app to install");
        Assert.Contains(package.AdbCalls(), c => c.Contains("install -r -d -g", StringComparison.Ordinal) && c.Contains("Example One.APK", StringComparison.Ordinal));
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("am start -n com.example.one/.MainActivity", StringComparison.Ordinal)), FileTimeout, "the installed app to open");
        Assert.DoesNotContain(package.AdbCalls(), c => c.Contains(" push ", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_TheGalleryIsToldOnlyWhenAsked()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        var photo = MakeFile(package, "beach.jpg", 2048);
        var quiet = MakeFile(package, "quiet.jpg", 2048);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        await app.SendAsync(Paths("push", photo));
        await app.WaitForStatusAsync(s => StateOf(s, "beach.jpg") == "done", FileTimeout, "the photo to arrive");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(c => c.Contains("MEDIA_SCANNER_SCAN_FILE -d file:///sdcard/Download/beach.jpg", StringComparison.Ordinal)), FileTimeout, "the Gallery told");

        new ConfigStore(package.Paths.Config).Set("Transfer.ScanMedia", "false");
        await app.WaitForStatusAsync(_ => ConfigFile.Load(package.Paths.Config).Transfer.ScanMedia == false, FileTimeout, "the setting");
        await app.SendAsync(Paths("push", quiet));
        await app.WaitForStatusAsync(s => StateOf(s, "quiet.jpg") == "done", FileTimeout, "the second photo to arrive");
        Assert.DoesNotContain(package.AdbCalls(), c => c.Contains("file:///sdcard/Download/quiet.jpg", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_ANameThePhoneHasIsKeptTwice()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        WritePhone(package, remoteFiles: new() { ["/sdcard/Download/report.pdf"] = 10, ["/sdcard/Download/report (2).pdf"] = 10 });
        var file = MakeFile(package, "report.pdf", 300);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        await app.SendAsync(Paths("push", file));
        await app.WaitForStatusAsync(s => StateOf(s, "report.pdf") == "done", FileTimeout, "the file to arrive");
        Assert.Contains(package.AdbCalls(), c => c.Contains("push", StringComparison.Ordinal) && c.EndsWith("\"/sdcard/Download/report (3).pdf\"", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_SendToInFileExplorerComesAndGoes()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        Assert.False(File.Exists(package.SendToShortcut));

        new ConfigStore(package.Paths.Config).Set("Transfer.SendToMenu", "true");
        await app.WaitUntilAsync(() => File.Exists(package.SendToShortcut), FileTimeout, "the Send to shortcut");
        new ConfigStore(package.Paths.Config).Set("Transfer.SendToMenu", "false");
        await app.WaitUntilAsync(() => !File.Exists(package.SendToShortcut), FileTimeout, "the shortcut to go");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_CopiedFilesAreSentWithTheirKey()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: c => c.Transfer.ConfirmOverMb = 0);
        var file = MakeFile(package, "copied in explorer.txt", 64);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        OnStaThread(() => System.Windows.Clipboard.SetFileDropList(new StringCollection { file }));
        await app.FocusAsync();
        await app.PressCtrlAltKeyAsync((byte)'V');
        await app.WaitForStatusAsync(s => StateOf(s, "copied in explorer.txt") == "done", FileTimeout, "the copied file to arrive");
        await app.QuitAsync();
    }

    [Fact(Timeout = 180_000)]
    public async Task Files_WhatIsWaitingIsCancelledWhenThePhoneLeaves()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        WritePhone(package, pushMillis: 8000);
        var first = MakeFile(package, "first.bin", 4096);
        var second = MakeFile(package, "second.bin", 4096);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);

        Assert.Equal(2, (await app.SendAsync(Paths("push", first, second))).Data!["queued"]!.GetValue<int>());
        await app.WaitForStatusAsync(s => StateOf(s, "first.bin") == "sending" && StateOf(s, "second.bin") == "waiting", FileTimeout, "one sending and one waiting");

        WritePhone(package, connected: false);
        var gone = await app.WaitForStatusAsync(s => StateOf(s, "first.bin") == "cancelled" && StateOf(s, "second.bin") == "cancelled", FileTimeout, "both cancelled");
        Assert.Equal(0, FilesOf(gone)["done"]!.GetValue<int>());
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task SendTo_QueuesFilesFromASecondInstanceUntilAPhoneConnects()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: config => config.Transfer.ConfirmOverMb = 0);
        WritePhone(package, connected: false);
        var file = Path.Combine(package.Root, "sent from explorer.txt");
        File.WriteAllText(file, "hello");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("waiting", StartupTimeout);

        using var second = app.StartAnother("--send", file);
        Assert.True(await Task.Run(() => second.WaitForExit(15_000), TestContext.Current.CancellationToken));
        Assert.Equal(0, second.ExitCode);
        Assert.DoesNotContain(package.AdbCalls(), call => call.Contains("push", StringComparison.Ordinal));

        WritePhone(package);
        await app.WaitForPhaseAsync("mirroring", StartupTimeout);
        await app.WaitForStatusAsync(status => FilesOf(status)["done"]!.GetValue<int>() == 1,
            StartupTimeout, "the file handed over by the second instance");
        Assert.Contains(package.AdbCalls(), call => call.Contains("push", StringComparison.Ordinal) && call.Contains("sent from explorer.txt", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    /// <summary>The clipboard belongs to single-threaded apartments; xUnit's threads are not one.</summary>
    private static void OnStaThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("The clipboard could not be set.", failure);
        }
    }
}
