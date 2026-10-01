using System.Text.Json;
using System.Text.Json.Nodes;
using Rex.Core;
using Rex.Tests.Support;

namespace Rex.Tests;

/// <summary>Sending files through the desktop window, including progress, questions and settings.</summary>
public sealed partial class AppUiTests
{
    private static JsonNode FilesOf(JsonObject status) => status["files"]!;

    private static IpcRequest PushRequest(params string[] paths) => new("push",
        new Dictionary<string, string> { ["paths"] = JsonSerializer.Serialize(paths) });

    [Fact(Timeout = 120_000)]
    public async Task Files_AQueuedCopyCanBeCancelledAndRetried()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: config => config.Transfer.ConfirmOverMb = 0);
        var file = Path.Combine(package.Root, "a document.txt");
        File.WriteAllText(file, new string('x', 4096));
        File.WriteAllText(package.SlowPushMarker, string.Empty);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        var reply = await app.SendAsync(PushRequest(file));
        Assert.True(reply.Ok, reply.Error);
        Assert.Equal(1, reply.Data!["queued"]!.GetValue<int>());
        var running = await app.WaitForStatusAsync(s => FilesOf(s)["running"]!.GetValue<int>() == 1, Soon, "the file to start");
        var id = FilesOf(running)["items"]![0]!["id"]!.GetValue<string>();
        app.Ui.Select("TabControls");
        await app.ScrollSidebarAsync(-8);
        app.Ui.Invoke("transfer-cancel-" + id);
        await app.WaitForStatusAsync(s => FilesOf(s)["items"]![0]!["state"]!.GetValue<string>() == "cancelled", Soon, "the file to be cancelled");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(call => call.Contains("rm -f --", StringComparison.Ordinal)), Soon, "the partial phone file to be removed");

        File.Delete(package.SlowPushMarker);
        app.Ui.Invoke("transfer-retry-" + id);
        await app.WaitForStatusAsync(s => FilesOf(s)["done"]!.GetValue<int>() == 1, Startup, "the file to arrive after retrying");
        await app.WaitUntilAsync(() => package.AdbCalls().Any(call => call.Contains("MEDIA_SCANNER_SCAN_FILE", StringComparison.Ordinal)), Soon, "the new phone file to be indexed");
        await app.SaveScreenshotAsync("ui-files-complete.png");
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Files_InstallingAnApkAsksBeforeItStarts()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true, configure: config => config.Transfer.ConfirmOverMb = 0);
        var apk = Path.Combine(package.Root, "example.apk");
        File.WriteAllText(apk, "test apk");
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        var cancelled = app.SendAsync(PushRequest(apk));
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", item => item.Name) == "Install", Soon, "the install question");
        await app.SaveScreenshotAsync("ui-files-install-question.png");
        app.Ui.Invoke("ConfirmCancel");
        Assert.Equal(0, (await cancelled).Data!["queued"]!.GetValue<int>());
        Assert.DoesNotContain(package.AdbCalls(), call => call.Contains(" install ", StringComparison.Ordinal));

        var accepted = app.SendAsync(PushRequest(apk));
        await app.WaitUntilAsync(() => app.Ui.Read("ConfirmAccept", item => item.Name) == "Install", Soon, "the install question again");
        app.Ui.Invoke("ConfirmAccept");
        Assert.Equal(1, (await accepted).Data!["queued"]!.GetValue<int>());
        await app.WaitForStatusAsync(s => FilesOf(s)["items"]![0]!["state"]!.GetValue<string>() == "installed", Startup, "the app to install");
        Assert.Contains(package.AdbCalls(), call => call.Contains("install -r", StringComparison.Ordinal) && call.Contains("example.apk", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Files_ScrcpysOwnDropAppearsInTheSameHistory()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        var file = Path.Combine(package.Root, "from-scrcpy.txt");
        File.WriteAllText(file, "hello");
        File.WriteAllText(package.FakeScrcpyDrop, file);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);

        var status = await app.WaitForStatusAsync(s => FilesOf(s)["done"]!.GetValue<int>() == 1, Startup, "scrcpy's drop in the history");
        Assert.Equal("from-scrcpy.txt", FilesOf(status)["items"]![0]!["name"]!.GetValue<string>());
        Assert.DoesNotContain(package.AdbCalls(), call => call.Contains(" push ", StringComparison.Ordinal));
        await app.QuitAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Settings_FileRowsSaveAndDisableTogether()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var package = new TestPackage(withFakeTools: true);
        using var app = new AppProcess(package);
        await app.WaitForPhaseAsync("mirroring", Startup);
        app.Ui.Select("TabSettings");
        app.Ui.ExpandGroup("GroupFiles");

        app.Ui.SetText("TransferFolder", "/sdcard/Documents");
        app.Ui.Toggle("TransferSortMedia", true);
        app.Ui.SelectComboItem("TransferFolders", "Refuse folders");
        app.Ui.Toggle("TransferReplace", false);
        app.Ui.Toggle("TransferAllowDowngrade", true);
        app.Ui.Toggle("TransferGrantPermissions", true);
        app.Ui.Toggle("TransferAllowTestApps", true);
        app.Ui.Toggle("TransferConfirmInstall", false);
        app.Ui.Toggle("TransferOpenAfterInstall", true);
        app.Ui.Toggle("TransferConfirmDrops", true);
        app.Ui.SelectComboItem("TransferConfirmOver", "500 MB");
        app.Ui.SelectComboItem("TransferWhenExists", "Ask");
        app.Ui.Toggle("TransferScanMedia", false);
        app.Ui.Toggle("TransferShowFolderAfter", true);
        app.Ui.SetValue("TransferAtOnce", 3);
        app.Ui.Toggle("TransferTaskbar", false);
        app.Ui.Toggle("TransferNotifyHidden", false);
        app.Ui.Toggle("TransferCancelOnLeave", false);
        app.Ui.SetValue("TransferHistory", 7);
        app.Ui.Toggle("TransferSendToMenu", true);
        app.Ui.Toggle("TransferInstallApks", false);

        await app.WaitUntilAsync(() => ConfigFile.Load(package.Paths.Config).Transfer is
        {
            Folder: "/sdcard/Documents/", SortMedia: true, Folders: "refuse", InstallApks: false,
            Replace: false, AllowDowngrade: true, GrantPermissions: true, AllowTestApps: true,
            ConfirmInstall: false, OpenAfterInstall: true, ConfirmDrops: true, ConfirmOverMb: 500,
            WhenNameExists: "ask", ScanMedia: false, ShowFolderAfter: true, AtOnce: 3,
            TaskbarProgress: false, NotifyWhenHidden: false, CancelWhenPhoneLeaves: false,
            History: 7, SendToMenu: true,
        }, Soon, "every Files row to save");
        Assert.True(File.Exists(package.SendToShortcut));
        Assert.False(app.Ui.Read("TransferReplace", item => item.IsEnabled));

        app.Ui.Toggle("TransferEnabled", false);
        await app.WaitUntilAsync(() => !ConfigFile.Load(package.Paths.Config).Transfer.Enabled, Soon, "file drops to turn off");
        Assert.False(app.Ui.Read("TransferFolder", item => item.IsEnabled));
        await app.WaitUntilAsync(() => !File.Exists(package.SendToShortcut), Soon, "the disabled File Explorer entry to be removed");
        await app.QuitAsync();
    }
}
